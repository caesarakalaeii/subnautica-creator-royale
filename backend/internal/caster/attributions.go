package caster

import (
	"context"
	"encoding/json"
	"io"
	"net/http"
	"strconv"
	"strings"
	"time"

	"github.com/caesarakalaeii/subnautica-creator-royale/backend/internal/tournament"
)

// attributionsPath is where an operator says who killed whom.
const attributionsPath = "/attributions"

// groupsPath is where the bracket's shape is written, with the operator token.
const groupsPath = "/groups"

// adjustmentsPath is where an operator gives or takes points for a reason.
const adjustmentsPath = "/adjustments"

// maxAdjustment bounds one adjustment either way; anything larger is almost certainly a typo.
const maxAdjustment = 100

// maxWriteBytes bounds one write: ample for a whole bracket, small enough to cap memory.
const maxWriteBytes = 64 << 10

// writeRate is how many writes a second the operator token may spend, with burstWindow of burst.
// Not configurable: operator writes are human clicks, far below this.
const writeRate = 5.0

// Records is the booth's durable store as the handlers need it. Nil is a valid deployment with no
// database: the bracket serves what the mod pushed and writes are refused.
type Records interface {
	Add(ctx context.Context, row tournament.Attribution) (tournament.Attribution, error)
	Attributions(ctx context.Context) ([]tournament.Attribution, error)
	AddAdjustment(ctx context.Context, row tournament.Adjustment) (tournament.Adjustment, error)
	Adjustments(ctx context.Context) ([]tournament.Adjustment, error)
	Groups(ctx context.Context) ([]tournament.Group, error)
	Final(ctx context.Context) ([]tournament.FinalSeries, error)
	SetGroups(ctx context.Context, groups []tournament.Group, final []tournament.FinalSeries) error

	// The command queue.
	Queue(ctx context.Context, verb, by string) (tournament.Command, error)
	Expire(ctx context.Context, before time.Time) (int, error)
	Owed(ctx context.Context, above int64) ([]tournament.Command, error)
	MarkSent(ctx context.Context, ids []int64, at time.Time) error
	MarkRan(ctx context.Context, id int64, answer string, at time.Time) error
	Commands(ctx context.Context, limit int) ([]tournament.Command, error)
	Outstanding(ctx context.Context) ([]string, error)
	Withdraw(ctx context.Context, by string, keep []string) (int, error)

	// The roster and its exceptions.
	People(ctx context.Context) ([]tournament.Person, error)
	SetPeople(ctx context.Context, people []tournament.Person) error
	Overrides(ctx context.Context) ([]tournament.Override, error)
	Override(ctx context.Context, person int64, role, note, by string) (tournament.Override, error)

	// The tournament's rules.
	WriteConfig(ctx context.Context, yaml, by, note string) (tournament.Config, error)
	NewestConfig(ctx context.Context) (tournament.Config, bool, error)
	ConfigOwed(ctx context.Context, running int64) (tournament.Config, bool, error)
	ConfigSentTo(ctx context.Context, revision int64, at time.Time) error
	ConfigSettled(ctx context.Context, revision int64, adopted, final bool, answer string,
		problems []string, at time.Time) error
	Configs(ctx context.Context, limit int) ([]tournament.Config, error)

	// ConfigAt is one revision with its document, which Configs omits to keep polls small.
	ConfigAt(ctx context.Context, revision int64) (tournament.Config, bool, error)
}

// RecordsFor narrows the store of record to one booth. Handlers only ever see an already scoped
// Records, so no query can forget its booth. A func type because Go has no covariant returns.
type RecordsFor func(booth string) Records

// attributionRequest is what an operator posts. The match is named explicitly, since attribution
// often happens after the match, when another group is running.
type attributionRequest struct {
	Group      int    `json:"group"`
	Generation int    `json:"generation"`
	Victim     string `json:"victim"`

	// Actor is null (or omitted) to say the kill is nobody's, which is how one is taken back.
	Actor *string `json:"actor"`

	// By is whoever is saying so. Required because the shared operator token cannot tell who.
	By string `json:"by"`
}

// attributionResponse is what a write gets back: the row as the database stamped it.
type attributionResponse struct {
	Written    bool   `json:"written"`
	ID         int64  `json:"id"`
	Group      int    `json:"group"`
	Generation int    `json:"generation"`
	Victim     string `json:"victim"`
	Actor      string `json:"actor,omitempty"`
	At         string `json:"at"`
	Error      string `json:"error,omitempty"`
}

// attribute writes one attribution. Each refusal says whether the service is not configured, has no
// database, the caller is not authorised, or the request is not understood.
func (s *Server) attribute(w http.ResponseWriter, r *http.Request) {
	booth, ok := s.authorisedToWrite(w, r, attributionsPath)
	if !ok {
		return
	}

	records := s.recordsFor(booth)
	if records == nil {
		// Not held in memory: a restart would silently lose it, which is worse than a refusal.
		writeError(w, http.StatusServiceUnavailable,
			"there is no database on this service, so an attribution cannot be kept")

		return
	}

	asked, ok := decodeJSON[attributionRequest](w, r, maxWriteBytes)
	if !ok {
		return
	}

	row, reason := attributionFrom(asked)
	if reason != "" {
		writeError(w, http.StatusBadRequest, reason)
		return
	}

	written, err := records.Add(r.Context(), row)
	if err != nil {
		// Logged as an error: the feed is unaffected, so this is where a database outage shows.
		s.logger.Error("could not write an attribution", "error", err.Error(),
			"group", row.Group, "generation", row.Generation, "victim", row.Victim)

		writeError(w, http.StatusServiceUnavailable,
			"the database refused the attribution; it was not kept")

		return
	}

	s.logger.Info("attribution", "id", written.ID, "group", written.Group,
		"generation", written.Generation, "victim", written.Victim,
		"actor", written.Actor, "by", written.By)

	replyJSON(w, http.StatusCreated, attributionResponse{
		Written:    true,
		ID:         written.ID,
		Group:      written.Group,
		Generation: written.Generation,
		Victim:     written.Victim,
		Actor:      written.Actor,
		At:         written.At.UTC().Format("2006-01-02T15:04:05.000Z"),
	})
}

// attributionFrom validates one request, and the reason is written for whoever typed it.
func attributionFrom(asked attributionRequest) (tournament.Attribution, string) {
	if asked.Group < 1 {
		return tournament.Attribution{}, "group has to be the number of a group, counting from one"
	}

	if asked.Generation < 1 {
		return tournament.Attribution{}, "generation has to be the attempt at that group, counting from one"
	}

	victim := strings.TrimSpace(asked.Victim)
	if victim == "" {
		return tournament.Attribution{}, "victim has to name the player who died"
	}

	by := strings.TrimSpace(asked.By)
	if by == "" {
		return tournament.Attribution{}, "by has to name whoever is attributing this, for reading afterwards"
	}

	actor := ""
	if asked.Actor != nil {
		actor = strings.TrimSpace(*asked.Actor)
	}

	// Refused explicitly: a self-kill scores nothing, so storing it would look like a lost write.
	if actor != "" && actor == victim {
		return tournament.Attribution{},
			"actor and victim are the same player; name nobody to take a kill back"
	}

	return tournament.Attribution{
		Group:      asked.Group,
		Generation: asked.Generation,
		Victim:     victim,
		Actor:      actor,
		By:         by,
	}, ""
}

// adjustmentRequest is what an operator posts to give or take points. The group decides which
// series the points count in.
type adjustmentRequest struct {
	Group  int    `json:"group"`
	Player string `json:"player"`
	Points int    `json:"points"`
	Reason string `json:"reason"`
	By     string `json:"by"`
}

// adjust writes one adjustment, with the same four refusals an attribution has.
func (s *Server) adjust(w http.ResponseWriter, r *http.Request) {
	booth, ok := s.authorisedToWrite(w, r, adjustmentsPath)
	if !ok {
		return
	}

	records := s.recordsFor(booth)
	if records == nil {
		writeError(w, http.StatusServiceUnavailable,
			"there is no database on this service, so an adjustment cannot be kept")

		return
	}

	asked, ok := decodeJSON[adjustmentRequest](w, r, maxWriteBytes)
	if !ok {
		return
	}

	row, reason := adjustmentFrom(asked)
	if reason != "" {
		writeError(w, http.StatusBadRequest, reason)
		return
	}

	written, err := records.AddAdjustment(r.Context(), row)
	if err != nil {
		s.logger.Error("could not write an adjustment", "error", err.Error(),
			"group", row.Group, "player", row.Name)

		writeError(w, http.StatusServiceUnavailable,
			"the database refused the adjustment; it was not kept")

		return
	}

	s.logger.Info("adjustment", "id", written.ID, "group", written.Group, "player", written.Name,
		"points", written.Points, "reason", written.Reason, "by", written.By)

	replyJSON(w, http.StatusCreated, map[string]any{
		"written": true, "id": written.ID, "group": written.Group, "player": written.Name,
		"points": written.Points, "reason": written.Reason,
		"at": written.At.UTC().Format("2006-01-02T15:04:05.000Z"),
	})
}

// adjustmentFrom validates one request, and the reason is written for whoever typed it.
func adjustmentFrom(asked adjustmentRequest) (tournament.Adjustment, string) {
	player := strings.TrimSpace(asked.Player)
	reason := strings.TrimSpace(asked.Reason)
	by := strings.TrimSpace(asked.By)

	switch {
	case asked.Group < 1:
		return tournament.Adjustment{}, "group has to be the number of the game it is for, counting from one"
	case player == "":
		return tournament.Adjustment{}, "player has to name who the points are for"
	case asked.Points == 0:
		return tournament.Adjustment{}, "points has to give or take at least one"
	case asked.Points > maxAdjustment || asked.Points < -maxAdjustment:
		return tournament.Adjustment{}, "points has to be between -100 and 100"
	case reason == "":
		return tournament.Adjustment{}, "reason has to say why, for whoever reads the table afterwards"
	case by == "":
		return tournament.Adjustment{}, "by has to name whoever is adjusting this, for reading afterwards"
	}

	return tournament.Adjustment{Group: asked.Group, Name: player, Points: asked.Points,
		Reason: reason, By: by}, ""
}

// groupsRequest is the bracket's shape, written whole: it replaces the bracket rather than
// appending, so mistakes can be fixed from the page.
type groupsRequest struct {
	Groups []groupRequest `json:"groups"`

	// Final is which series seat the final and how many each sends. Absent means the top
	// LMS_FINAL_SEATS of the whole table.
	Final []tournament.FinalSeries `json:"final"`
}

type groupRequest struct {
	Number  int      `json:"number"`
	Name    string   `json:"name"`
	Series  string   `json:"series"`
	Members []string `json:"members"`
}

// setGroups replaces the bracket's shape.
func (s *Server) setGroups(w http.ResponseWriter, r *http.Request) {
	booth, ok := s.authorisedToWrite(w, r, groupsPath)
	if !ok {
		return
	}

	records := s.recordsFor(booth)
	if records == nil {
		writeError(w, http.StatusServiceUnavailable,
			"there is no database on this service, so the bracket cannot be kept")

		return
	}

	asked, ok := decodeJSON[groupsRequest](w, r, maxWriteBytes)
	if !ok {
		return
	}

	groups, final, reason := groupsFrom(asked)
	if reason != "" {
		writeError(w, http.StatusBadRequest, reason)
		return
	}

	if err := records.SetGroups(r.Context(), groups, final); err != nil {
		s.logger.Error("could not write the bracket", "error", err.Error())
		writeError(w, http.StatusServiceUnavailable, "the database refused the bracket; it was not kept")

		return
	}

	s.logger.Info("bracket written", "groups", len(groups), "final_series", len(final))

	replyJSON(w, http.StatusOK, map[string]any{"written": true, "groups": len(groups)})
}

func groupsFrom(asked groupsRequest) ([]tournament.Group, []tournament.FinalSeries, string) {
	groups := make([]tournament.Group, 0, len(asked.Groups))
	numbers := map[int]bool{}
	carried := map[string]bool{}

	for _, group := range asked.Groups {
		if group.Number < 1 {
			return nil, nil, "every group needs a number, counting from one"
		}

		if numbers[group.Number] {
			return nil, nil, "two groups cannot share a number"
		}
		numbers[group.Number] = true

		name := strings.TrimSpace(group.Name)
		if name == "" {
			return nil, nil, "every group needs a name, because the page draws it"
		}

		members := make([]string, 0, len(group.Members))
		seen := map[string]bool{}

		for _, member := range group.Members {
			player := strings.TrimSpace(member)
			if player == "" {
				return nil, nil, "a group cannot hold a player with no name"
			}

			// Refused rather than deduplicated, which would hide a group one player short.
			if seen[player] {
				return nil, nil, "the same player is in group " + name + " twice"
			}
			seen[player] = true

			members = append(members, player)
		}

		series := strings.TrimSpace(group.Series)
		if series != "" {
			carried[series] = true
		}

		groups = append(groups, tournament.Group{
			Number: group.Number, Name: name, Series: series, Members: members,
		})
	}

	final := make([]tournament.FinalSeries, 0, len(asked.Final))
	listed := map[string]bool{}

	for _, row := range asked.Final {
		series := strings.TrimSpace(row.Series)

		// Each of these would seat fewer players than the final claims.
		if !carried[series] {
			return nil, nil, "the final names series " + strconv.Quote(series) + ", which no group carries"
		}

		if listed[series] {
			return nil, nil, "the final names series " + strconv.Quote(series) + " twice"
		}
		listed[series] = true

		if row.Seats < 1 {
			return nil, nil, "series " + strconv.Quote(series) + " has to send at least one player to the final"
		}

		final = append(final, tournament.FinalSeries{Series: series, Seats: row.Seats})
	}

	return groups, final, ""
}

// authorisedToWrite is the token check every write shares, and it writes the refusal itself. It
// returns the booth whose operator token was presented. Token first, then rate limit, so an
// unauthenticated caller cannot spend the operator's allowance.
func (s *Server) authorisedToWrite(w http.ResponseWriter, r *http.Request, path string) (*Booth, bool) {
	booth := s.booths.asked(r, s.now())

	// A known booth with no operator token is a 503; an unknown booth falls through to the 401 so
	// ids cannot be enumerated.
	if booth != nil && !booth.writeConfigured() {
		writeError(w, http.StatusServiceUnavailable, "writing is not configured on this service")

		return nil, false
	}

	if !booth.mayWrite(r) {
		if s.badOperatorLimit.allow() {
			s.logger.Warn("refused a write", "reason", "bad operator token", "path", path,
				"booth", askedFor(r), "remote", r.RemoteAddr,
				"also_refused_since", s.badOperatorLimit.drainRefusals())
		}

		// Always 401, never 429: a different answer would tell a guesser something.
		writeError(w, http.StatusUnauthorized, "bad or missing operator token")

		return nil, false
	}

	if !s.writeLimit.allow() {
		if s.writeLimit.refusals() == 1 {
			s.logger.Warn("rate-limiting writes", "limit_per_second", writeRate,
				"path", path, "remote", r.RemoteAddr)
		}

		w.Header().Set("Retry-After", retryAfter(writeRate))
		writeError(w, http.StatusTooManyRequests, "too many writes; this service is rate limited")

		return nil, false
	}

	if refused := s.writeLimit.drainRefusals(); refused > 0 {
		s.logger.Warn("no longer rate-limiting writes", "refused", refused)
	}

	return booth, true
}

// recordsFor is the store of record narrowed to one booth, or nil when there is no database. It is
// the only way a handler reaches the database, so every query is booth-scoped.
func (s *Server) recordsFor(booth *Booth) Records {
	if s.records == nil || booth == nil {
		return nil
	}

	return s.records(booth.ID())
}

// writeError is a refusal, in the one shape every route on this service uses.
func writeError(w http.ResponseWriter, status int, reason string) {
	replyJSON(w, status, map[string]string{"error": reason})
}

// replyJSON writes any body. It never echoes what was sent, only what this service made of it.
func replyJSON(w http.ResponseWriter, status int, body any) {
	w.Header().Set("Content-Type", "application/json")
	w.WriteHeader(status)
	_ = json.NewEncoder(w).Encode(body)
}

// decodeJSON reads one bounded body and parses it, writing the shared refusals for operator write
// paths. The console report and ingest push use their own reply shapes.
func decodeJSON[T any](w http.ResponseWriter, r *http.Request, limit int64) (T, bool) {
	var asked T

	payload, err := io.ReadAll(http.MaxBytesReader(w, r.Body, limit))
	if err != nil {
		writeError(w, http.StatusRequestEntityTooLarge, "that is larger than this service accepts")
		return asked, false
	}

	if err := json.Unmarshal(payload, &asked); err != nil {
		writeError(w, http.StatusBadRequest, "that is not the JSON this path takes: "+err.Error())
		return asked, false
	}

	return asked, true
}
