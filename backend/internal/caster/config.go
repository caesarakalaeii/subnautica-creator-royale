package caster

import (
	"net/http"
	"strconv"
	"strings"
	"time"

	"github.com/caesarakalaeii/subnautica-creator-royale/backend/internal/tournament"
)

// rulesPath is where an operator writes the tournament's rules.
const rulesPath = "/rules"

// rulesRevisionHeader is how the mod says which rules revision it is running. A mod that omits it
// is not taking its rules from here and is sent none.
const rulesRevisionHeader = "X-Lms-Rules-Revision"

// maxRulesBytes bounds one config document: about ten times the fully commented
// `tournament.example.yaml`.
const maxRulesBytes = 256 << 10

// rulesDrawn is how many revisions the console is given, without their documents.
const rulesDrawn = 20

// rulesRequest is what an operator writes. The YAML is not validated here: the game server loads it
// with its own loader and reports problems by field path, so there is a single validator.
type rulesRequest struct {
	Yaml string `json:"yaml"`
	By   string `json:"by"`
	Note string `json:"note"`
}

// rulesResponse is what writing one gets back, including the revision the console then watches.
type rulesResponse struct {
	Written  bool   `json:"written"`
	Revision int64  `json:"revision"`
	State    string `json:"state"`
	Error    string `json:"error,omitempty"`
}

// offeredRules is one config on its way to the mod.
type offeredRules struct {
	Revision int64  `json:"revision"`
	Yaml     string `json:"yaml"`
	By       string `json:"by"`
}

// setRules keeps a new revision of the tournament's rules.
func (s *Server) setRules(w http.ResponseWriter, r *http.Request) {
	booth, ok := s.authorisedToWrite(w, r, rulesPath)
	if !ok {
		return
	}

	records := s.recordsFor(booth)
	if records == nil {
		writeError(w, http.StatusServiceUnavailable,
			"there is no database on this service, so the rules cannot be kept")

		return
	}

	asked, ok := decodeJSON[rulesRequest](w, r, maxRulesBytes)
	if !ok {
		return
	}

	by := strings.TrimSpace(asked.By)
	if by == "" {
		writeError(w, http.StatusBadRequest,
			"by has to name whoever is changing the rules; it goes into the game server's own log")

		return
	}

	// Only two checks here: not empty, and no tabs (the broadcast to clients is tab-delimited).
	// Everything else is the game server's loader's job.
	if strings.TrimSpace(asked.Yaml) == "" {
		writeError(w, http.StatusBadRequest, "there is no config in that; an empty file is not an edit")
		return
	}

	if strings.ContainsRune(asked.Yaml, '\t') {
		writeError(w, http.StatusBadRequest,
			"that config has a tab in it. YAML does not allow one as indentation and the clients "+
				"cannot be sent it; use spaces")

		return
	}

	written, err := records.WriteConfig(r.Context(), asked.Yaml, by, strings.TrimSpace(asked.Note))
	if err != nil {
		s.logger.Error("could not write the rules", "booth", booth.ID(),
			"error", err.Error(), "by", by)
		writeError(w, http.StatusServiceUnavailable, "the database refused the rules; they were not kept")

		return
	}

	s.logger.Info("rules written", "booth", booth.ID(), "revision", written.Revision, "by", by,
		"bytes", len(asked.Yaml), "note", written.Note)

	replyJSON(w, http.StatusAccepted, rulesResponse{
		Written: true, Revision: written.Revision, State: tournament.ConfigQueued,
	})
}

// showRules hands one revision's document back whole, so a bad edit can be reverted. The listing
// (`Store.Configs`) omits the YAML to keep the console's frequent polls small. The reply carries
// only the document and its identity; `answer` and `problems` come from the listing. It requires
// the operator token, like the rest of the console.
func (s *Server) showRules(w http.ResponseWriter, r *http.Request) {
	booth, ok := s.authorisedToWrite(w, r, rulesPath)
	if !ok {
		return
	}

	records := s.recordsFor(booth)
	if records == nil {
		// No database configured, as distinct from an unreachable one below.
		writeError(w, http.StatusServiceUnavailable,
			"there is no database on this service, so there are no revisions to read")

		return
	}

	asked := r.PathValue("revision")

	revision, err := strconv.ParseInt(asked, 10, 64)
	if err != nil || revision <= 0 {
		// Revision 0 means the game server's own file, which this booth never stored.
		writeError(w, http.StatusBadRequest,
			"a revision is a positive whole number; revision 0 is the file on the game server's "+
				"own disk, which this booth never kept")

		return
	}

	ctx, cancel := contextWithDeadline(r)
	defer cancel()

	row, has, err := records.ConfigAt(ctx, revision)
	if err != nil {
		s.logger.Error("could not read a revision", "error", err.Error(), "revision", revision)
		writeError(w, http.StatusServiceUnavailable,
			"the booth cannot reach its database, so that revision could not be read")

		return
	}

	if !has {
		writeError(w, http.StatusNotFound, "this booth has no revision "+asked)

		return
	}

	w.Header().Set("Cache-Control", "no-store")
	replyJSON(w, http.StatusOK, row)
}

// owedRules is the config this push should carry back, or nil. Like owedCommands it is bounded by
// commandDeadline and never fails the push.
func (s *Server) owedRules(booth *Booth, r *http.Request) *offeredRules {
	records := s.recordsFor(booth)
	if records == nil {
		return nil
	}

	running, speaks := rulesRevisionOf(r)
	if !speaks {
		// The mod does not accept rules (older build or `telemetry.acceptsCommands: false`).
		return nil
	}

	ctx, cancel := contextWithDeadline(r)
	defer cancel()

	owed, has, err := records.ConfigOwed(ctx, running)
	if err != nil {
		s.logger.Warn("could not read what rules the game server is owed", "error", err.Error())

		return nil
	}

	if !has {
		return nil
	}

	if err := records.ConfigSentTo(ctx, owed.Revision, time.Now()); err != nil {
		// Still send it: a stale label is better than withheld rules.
		s.logger.Warn("could not mark the rules sent", "error", err.Error(), "revision", owed.Revision)
	}

	s.logger.Info("handing the rules to the game server", "booth", booth.ID(),
		"revision", owed.Revision, "running", running)

	return &offeredRules{Revision: owed.Revision, Yaml: owed.Yaml, By: owed.By}
}

// rulesRevisionOf reads which revision the game server is running, and whether it sent one.
func rulesRevisionOf(r *http.Request) (int64, bool) {
	raw := strings.TrimSpace(r.Header.Get(rulesRevisionHeader))
	if raw == "" {
		return 0, false
	}

	running, err := strconv.ParseInt(raw, 10, 64)
	if err != nil || running < 0 {
		return 0, false
	}

	return running, true
}

// fileRules records what the game server said about the revisions it has been sent. Like
// fileAnswers, a database error never fails the report.
func (s *Server) fileRules(booth *Booth, r *http.Request, outcomes []consoleRules) {
	records := s.recordsFor(booth)
	if records == nil || len(outcomes) == 0 {
		return
	}

	ctx, cancel := contextWithDeadline(r)
	defer cancel()

	now := time.Now()

	for _, outcome := range outcomes {
		if outcome.Revision <= 0 {
			continue
		}

		problems := make([]string, 0, len(outcome.Problems))
		for _, problem := range outcome.Problems {
			problems = append(problems, problem.Problem)
		}

		if err := records.ConfigSettled(ctx, outcome.Revision, outcome.Adopted, outcome.Final,
			outcome.Message, problems, now); err != nil {
			s.logger.Warn("could not file what the game server said about the rules",
				"error", err.Error(), "revision", outcome.Revision)

			return
		}

		s.logger.Info("rules answered", "revision", outcome.Revision, "adopted", outcome.Adopted,
			"final", outcome.Final, "answer", outcome.Message, "problems", len(problems))
	}
}
