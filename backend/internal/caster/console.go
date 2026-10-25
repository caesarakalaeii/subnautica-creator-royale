package caster

import (
	"context"
	"encoding/json"
	"fmt"
	"io"
	"net/http"
	"sync"
	"time"

	"github.com/caesarakalaeii/subnautica-creator-royale/backend/internal/tournament"
)

// consolePath is the operator's channel: the mod posts to it with the ingest token, and the console
// page reads it with the operator token, since it names everybody connected.
const consolePath = "/console"

// consoleVersion is the console document version, independent of the telemetry contract's.
const consoleVersion = 1

// maxConsoleBytes bounds one report; a real one is a few kilobytes.
const maxConsoleBytes = 256 << 10

// consoleReport is what the mod says about itself. It is not match state and never reaches Store.
type consoleReport struct {
	Version int    `json:"v"`
	At      string `json:"at"`

	// AcceptsCommands is false when the server's config refuses commands
	// (`telemetry.acceptsCommands`).
	AcceptsCommands bool `json:"acceptsCommands"`

	// Ran is the highest command id the server has run since it started. It resets on restart, so
	// the booth's row states decide what is still owed.
	Ran int64 `json:"ran"`

	// Connected is everybody in the world, flagged by whether the match counts them. Unlike the
	// broadcast's `players[]`, it includes ghosts.
	Connected []consolePresence `json:"connected"`

	// Verbs is what that build can do, so the console's controls cannot drift from `AdminCommands`.
	Verbs []consoleVerb `json:"verbs"`

	// Answers is what the mod did about the commands it has run since the last report.
	Answers []consoleAnswer `json:"answers"`

	// RulesRevision is which config revision the server is running. Zero is the file on its own
	// disk, not "no rules".
	RulesRevision int64 `json:"rulesRevision"`

	// Rules is what it did about the revisions it has been sent.
	Rules []consoleRules `json:"rules"`

	// Config is the config the server is actually running, verbatim, so the editor can start from
	// it. Optional: older mods omit it.
	Config string `json:"config"`
}

// consoleRules is what the game server said about one config revision.
type consoleRules struct {
	Revision int64 `json:"revision"`
	Adopted  bool  `json:"adopted"`

	// Final distinguishes "written, waiting for the lobby" from a settled outcome.
	Final   bool   `json:"final"`
	Message string `json:"message"`

	// Problems is the loader's own field paths, verbatim. Objects rather than strings to match the
	// mod's hand-written encoder.
	Problems []consoleProblem `json:"problems"`
}

type consoleProblem struct {
	Problem string `json:"problem"`
}

type consolePresence struct {
	Name    string `json:"name"`
	InMatch bool   `json:"inMatch"`
}

type consoleVerb struct {
	Verb         string `json:"verb"`
	TakesSubject bool   `json:"takesSubject"`
	MustBeTyped  bool   `json:"mustBeTyped"`
}

// consoleAnswer is the match's own sentence about one command, word for word.
type consoleAnswer struct {
	ID     int64  `json:"id"`
	Verb   string `json:"verb"`
	Answer string `json:"answer"`
}

// console is the last report and when it arrived, kept apart from the match store.
type console struct {
	mu     sync.RWMutex
	held   *consoleReport
	seenAt time.Time

	// intended is what the booth worked out from the last report.
	intended intent
}

// Apply keeps this report as the current one.
func (c *console) Apply(report consoleReport, at time.Time) {
	c.mu.Lock()
	defer c.mu.Unlock()

	c.held = &report
	c.seenAt = at
}

// Latest is the report and when it arrived, or nil before the mod has posted one.
func (c *console) Latest() (*consoleReport, time.Time) {
	c.mu.RLock()
	defer c.mu.RUnlock()

	return c.held, c.seenAt
}

// Intend keeps what the booth worked out from the last report.
func (c *console) Intend(decided intent) {
	c.mu.Lock()
	defer c.mu.Unlock()

	c.intended = decided
}

// Intended is the booth's own view, for the page to draw beside the mod's.
func (c *console) Intended() intent {
	c.mu.RLock()
	defer c.mu.RUnlock()

	return c.intended
}

// consoleView is what the operator's page draws.
type consoleView struct {
	// Report is nil before any mod has posted one.
	Report *consoleReport `json:"report"`

	// ReportedAt is when this service received the report, on this service's clock.
	ReportedAt string `json:"reportedAt"`

	// Commands is the tail of the queue with what happened to each, newest first.
	Commands []queuedCommand `json:"commands"`

	// Intent is what the booth thinks the roles should be, drawn beside what the mod reports.
	Intent intent `json:"intent"`

	// People, Groups and Overrides are the roster editor's own data.
	People    []tournament.Person   `json:"people"`
	Groups    []tournament.Group    `json:"groups"`
	Overrides []tournament.Override `json:"overrides"`

	// Rules is the newest revision with its document; Revisions is the recent history without
	// documents, to keep the poll small.
	Rules     *tournament.Config  `json:"rules"`
	Revisions []tournament.Config `json:"revisions"`

	// Records says whether the store of record answered, so a page can tell an empty queue from an
	// unreachable database.
	Records string `json:"records"`

	// Roster says whether the roster read specifically succeeded. The editor pre-fills from People,
	// and saving an empty roster from a failed read would remove everyone.
	Roster string `json:"roster"`

	// Running is the document the game server reports it is running, from the report rather than the
	// database. The editor opens on it unless a newer revision is still pending (see console.js).
	// Omitted when the mod has not reported one.
	Running *tournament.Config `json:"running,omitempty"`
}

// queuedCommand is one row of the queue as the page draws it, with times as strings or null.
type queuedCommand struct {
	ID     int64  `json:"id"`
	Verb   string `json:"verb"`
	By     string `json:"by"`
	State  string `json:"state"`
	Answer string `json:"answer"`

	QueuedAt string  `json:"queuedAt"`
	SentAt   *string `json:"sentAt"`
	RanAt    *string `json:"ranAt"`
}

// takeConsole reads one report from the mod.
func (s *Server) takeConsole(w http.ResponseWriter, r *http.Request) {
	booth := s.booths.asked(r, s.now())

	if booth != nil && !booth.ingestConfigured() {
		writeError(w, http.StatusServiceUnavailable, "ingest is not configured on this service")
		return
	}

	if !booth.mayPush(r) {
		if s.badTokenLimit.allow() {
			s.logger.Warn("refused a console report", "reason", "bad token",
				"booth", askedFor(r), "remote", r.RemoteAddr,
				"also_refused_since", s.badTokenLimit.drainRefusals())
		}

		writeError(w, http.StatusUnauthorized, "bad or missing bearer token")

		return
	}

	// The push bucket, not the write bucket: this is the mod's allowance.
	if s.pushLimit != nil && !s.pushLimit.allow() {
		w.Header().Set("Retry-After", retryAfter(s.cfg.IngestRate))
		writeError(w, http.StatusTooManyRequests, "too many pushes; this service is rate limited")

		return
	}

	payload, err := io.ReadAll(http.MaxBytesReader(w, r.Body, maxConsoleBytes))
	if err != nil {
		writeError(w, http.StatusRequestEntityTooLarge, "that is larger than this service accepts")
		return
	}

	report, reason := consoleFrom(payload)
	if reason != "" {
		// Logged as an error: a version mismatch means the mod and booth are different builds.
		s.logger.Error("refused a console report", "booth", booth.ID(), "reason", reason)
		writeError(w, http.StatusBadRequest, reason)

		return
	}

	booth.console.Apply(report, time.Now())
	s.fileAnswers(booth, r, report.Answers)
	s.fileRules(booth, r, report.Rules)

	// Reconcile on each report rather than on a timer: that is when the mod's state is known.
	booth.console.Intend(s.reconcile(booth, r, report))

	s.logger.Debug("console", "booth", booth.ID(), "connected", len(report.Connected),
		"answers", len(report.Answers), "ran", report.Ran)

	replyJSON(w, http.StatusAccepted, map[string]any{"accepted": true})
}

// consoleFrom parses one report, and the reason is written for whoever is reading a server log.
func consoleFrom(payload []byte) (consoleReport, string) {
	var report consoleReport
	if err := json.Unmarshal(payload, &report); err != nil {
		return consoleReport{}, "that is not the JSON this path takes: " + err.Error()
	}

	if report.Version != consoleVersion {
		return consoleReport{}, fmt.Sprintf(
			"this service speaks console version %d and that report is version %d; "+
				"the mod and the booth were deployed from different builds",
			consoleVersion, report.Version)
	}

	// Always arrays, never null, for the page.
	if report.Connected == nil {
		report.Connected = []consolePresence{}
	}
	if report.Verbs == nil {
		report.Verbs = []consoleVerb{}
	}
	if report.Answers == nil {
		report.Answers = []consoleAnswer{}
	}
	if report.Rules == nil {
		report.Rules = []consoleRules{}
	}

	return report, ""
}

// fileAnswers records what the mod said against the command rows. A database error never fails
// the report.
func (s *Server) fileAnswers(booth *Booth, r *http.Request, answers []consoleAnswer) {
	records := s.recordsFor(booth)
	if records == nil || len(answers) == 0 {
		return
	}

	ctx, cancel := contextWithDeadline(r)
	defer cancel()

	now := time.Now()

	for _, answer := range answers {
		if answer.ID <= 0 {
			continue
		}

		if err := records.MarkRan(ctx, answer.ID, answer.Answer, now); err != nil {
			s.logger.Warn("could not file what the mod said about a command",
				"error", err.Error(), "id", answer.ID)

			return
		}

		s.logger.Info("command answered", "id", answer.ID, "verb", answer.Verb,
			"answer", answer.Answer)
	}
}

// showConsole is what the operator's page draws. It requires the operator token, not the view
// token: it names who is connected and holds the unannounced roster.
func (s *Server) showConsole(w http.ResponseWriter, r *http.Request) {
	booth, ok := s.authorisedToWrite(w, r, consolePath)
	if !ok {
		return
	}

	report, seenAt := booth.console.Latest()

	view := consoleView{
		Report: report,

		// From the report, outside the database branch, so it is present even without a database.
		Running: runningRules(report),

		Commands:  []queuedCommand{},
		Records:   recordsNone,
		Roster:    recordsNone,
		Intent:    booth.console.Intended(),
		People:    []tournament.Person{},
		Groups:    []tournament.Group{},
		Overrides: []tournament.Override{},
		Revisions: []tournament.Config{},
	}

	// Always arrays, never null, for the page.
	if view.Intent.Ghosts == nil {
		view.Intent = intent{
			Ghosts: []string{}, Contestants: []string{}, Unbound: []string{}, Pending: []string{},
			Why: "nothing has reported yet, so the booth has nothing to reconcile against",
		}
	}

	if !seenAt.IsZero() {
		view.ReportedAt = stamped(seenAt)
	}

	if records := s.recordsFor(booth); records != nil {
		ctx, cancel := contextWithDeadline(r)
		defer cancel()

		view.Records = recordsOK
		view.Roster = recordsOK

		if rows, err := records.Commands(ctx, commandsDrawn); err != nil {
			s.logger.Warn("could not read the command queue", "error", err.Error())
			view.Records = recordsUnreachable
		} else {
			view.Commands = drawn(rows)
		}

		// Each read below keeps the empty-array default when the store returns nil.
		if people, err := records.People(ctx); err != nil {
			s.logger.Warn("could not read the roster", "error", err.Error())
			view.Records = recordsUnreachable

			// Flagged separately: the editor pre-fills from People and must not save it back empty.
			view.Roster = recordsUnreachable
		} else if people != nil {
			view.People = people
		}

		if groups, err := records.Groups(ctx); err != nil {
			s.logger.Warn("could not read the bracket", "error", err.Error())
			view.Records = recordsUnreachable
		} else if groups != nil {
			view.Groups = groups
		}

		if overrides, err := records.Overrides(ctx); err != nil {
			s.logger.Warn("could not read the overrides", "error", err.Error())
			view.Records = recordsUnreachable
		} else if overrides != nil {
			view.Overrides = overrides
		}

		if newest, has, err := records.NewestConfig(ctx); err != nil {
			s.logger.Warn("could not read the rules", "error", err.Error())
			view.Records = recordsUnreachable
		} else if has {
			view.Rules = &newest
		}

		// No revision written through this booth yet: fall back to the game server's own file.
		if view.Rules == nil {
			view.Rules = runningRules(report)
		}

		if revisions, err := records.Configs(ctx, rulesDrawn); err != nil {
			s.logger.Warn("could not read the revisions", "error", err.Error())
			view.Records = recordsUnreachable
		} else if revisions != nil {
			view.Revisions = revisions
		}
	}

	w.Header().Set("Cache-Control", "no-store")
	replyJSON(w, http.StatusOK, view)
}

// runningRules is the file the game server says it has on its own disk, as a config row with
// revision 0 (as on the wire) and no state. Nil when the mod did not report one.
func runningRules(report *consoleReport) *tournament.Config {
	if report == nil || report.Config == "" {
		return nil
	}

	return &tournament.Config{Revision: 0, Yaml: report.Config, By: "the game server"}
}

// drawn turns the queue's rows into what the page reads.
func drawn(rows []tournament.Command) []queuedCommand {
	drawn := make([]queuedCommand, 0, len(rows))

	for _, row := range rows {
		one := queuedCommand{
			ID: row.ID, Verb: row.Verb, By: row.By, State: row.State, Answer: row.Answer,
			QueuedAt: stamped(row.QueuedAt),
		}

		if row.SentAt != nil {
			at := stamped(*row.SentAt)
			one.SentAt = &at
		}

		if row.RanAt != nil {
			at := stamped(*row.RanAt)
			one.RanAt = &at
		}

		drawn = append(drawn, one)
	}

	return drawn
}

// stamped formats an instant the way the telemetry contract does: UTC, milliseconds, with a Z.
func stamped(at time.Time) string {
	return at.UTC().Format("2006-01-02T15:04:05.000Z")
}

// contextWithDeadline bounds a request's database work by commandDeadline.
func contextWithDeadline(r *http.Request) (context.Context, context.CancelFunc) {
	return context.WithTimeout(r.Context(), commandDeadline)
}
