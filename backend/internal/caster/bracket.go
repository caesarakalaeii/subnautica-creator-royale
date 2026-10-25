package caster

import (
	"net/http"

	"github.com/caesarakalaeii/subnautica-creator-royale/backend/internal/tournament"
)

// bracketPath serves the tournament bracket to whoever holds that booth's view token.
const bracketPath = "/bracket"

// Database states reported in the bracket reply, so an empty bracket can be told apart from an
// unreadable one.
const (
	recordsOK          = "ok"
	recordsNone        = "none"
	recordsUnreachable = "unreachable"
)

// bracketResponse is what a booth's watchers see: standings and pairings only. It deliberately
// carries no live match state (positions, who is alive, which group is playing), even behind the
// view token, since it is less guarded than the live feed.
type bracketResponse struct {
	// Standings is the folded table, or nil before the mod has pushed one.
	Standings *tournament.Table `json:"standings"`

	// Groups is the bracket's shape, and it is empty until somebody writes it.
	Groups []tournament.Group `json:"groups"`

	// Final is who the standings would seat in the final, computed rather than typed.
	Final finalSeats `json:"final"`

	// Attributions is how many rows the fold read; the rows themselves are not exposed.
	Attributions int `json:"attributions"`

	// Records says whether the store of record answered, so a page can tell an empty tournament from
	// an unreachable database.
	Records string `json:"records"`

	// Public is true when this bracket was published, so a page can skip asking for the view token.
	// Omitted when false.
	Public bool `json:"public,omitempty"`
}

// finalSeats is the computed final.
type finalSeats struct {
	// Seats is how many places the final has.
	Seats int `json:"seats"`

	// Seated is who holds them, best first, and shorter than Seats while the table is. Not called
	// `players`, since this route carries no live player state.
	Seated []string `json:"seated"`

	// Spec is the final's spec as written with the groups, empty when there is none and the top of
	// the whole table is seated instead.
	Spec []tournament.FinalSeries `json:"spec"`

	// From is Seated again with the series that seated each, so the page can say why somebody is
	// in. Empty when there is no spec, because then nothing but the table seated them.
	From []tournament.FinalSeat `json:"from"`
}

// bracket serves one booth's tournament to whoever holds its view token. It degrades rather than
// fails: without the database it omits attributions and says so; without a push it omits the table.
func (s *Server) bracket(w http.ResponseWriter, r *http.Request) {
	booth := s.booths.asked(r, s.now())

	// Publication is checked first, so a published booth serves its bracket even without a view
	// token configured.
	if !booth.isPublic() {
		// As in live: a known booth without a view token is a 503; an unknown booth falls through
		// to the 401, which must read identically on every route so booth ids are not enumerable.
		if booth != nil && !booth.viewConfigured() {
			writeError(w, http.StatusServiceUnavailable, "the view token is not configured on this service")
			return
		}

		if !booth.mayView(r) {
			if s.badTokenLimit.allow() {
				s.logger.Warn("refused a bracket", "reason", "bad token",
					"booth", askedFor(r), "remote", r.RemoteAddr,
					"also_refused_since", s.badTokenLimit.drainRefusals())
			}

			writeError(w, http.StatusUnauthorized, "bad or missing view token")

			return
		}
	}

	rows, state := s.attributions(booth, r)
	adjustments, adjustState := s.adjustments(booth, r)
	if adjustState == recordsUnreachable {
		state = recordsUnreachable
	}

	response := bracketResponse{
		Groups:       []tournament.Group{},
		Attributions: len(rows),
		Records:      state,
		Final:        finalSeats{Seats: s.cfg.FinalSeats, Seated: []string{}, Spec: []tournament.FinalSeries{}, From: []tournament.FinalSeat{}},
		Public:       booth.isPublic(),
	}

	if groups, err := s.groups(booth, r); err != nil {
		// Already logged by s.groups.
		response.Records = recordsUnreachable
	} else {
		response.Groups = groups
	}

	if records := s.recordsFor(booth); records != nil {
		if final, err := records.Final(r.Context()); err != nil {
			s.logger.Warn("could not read the final", "booth", booth.ID(), "error", err.Error())
			response.Records = recordsUnreachable
		} else if len(final) > 0 {
			response.Final.Spec = final
			response.Final.Seats = 0
			for _, row := range final {
				response.Final.Seats += row.Seats
			}
		}
	}

	snapshot, pushed := booth.store.Latest()
	if pushed && snapshot.Standings != nil {
		table := tournament.Fold(*snapshot.Standings, tournament.Credits(rows),
			tournament.Adjusted(adjustments))
		response.Standings = &table

		if len(response.Final.Spec) > 0 {
			response.Final.From = tournament.SeatFinal(*snapshot.Standings, response.Groups,
				response.Final.Spec, rows, adjustments)
			for _, seat := range response.Final.From {
				response.Final.Seated = append(response.Final.Seated, seat.Name)
			}
		} else {
			response.Final.Seated = seat(table, s.cfg.FinalSeats)
		}
	}

	// Changes whenever an attribution is written, so never cached.
	w.Header().Set("Cache-Control", "no-store")
	replyJSON(w, http.StatusOK, response)
}

// attributions is every row the store of record holds, and what to call the store's state.
func (s *Server) attributions(booth *Booth, r *http.Request) ([]tournament.Attribution, string) {
	records := s.recordsFor(booth)
	if records == nil {
		return nil, recordsNone
	}

	rows, err := records.Attributions(r.Context())
	if err != nil {
		// Warn: the page still serves and the feed is unaffected.
		s.logger.Warn("could not read the attributions", "booth", booth.ID(), "error", err.Error())

		return nil, recordsUnreachable
	}

	return rows, recordsOK
}

// adjustments is every adjustment the store of record holds, degrading the way attributions does.
func (s *Server) adjustments(booth *Booth, r *http.Request) ([]tournament.Adjustment, string) {
	records := s.recordsFor(booth)
	if records == nil {
		return nil, recordsNone
	}

	rows, err := records.Adjustments(r.Context())
	if err != nil {
		s.logger.Warn("could not read the adjustments", "booth", booth.ID(), "error", err.Error())

		return nil, recordsUnreachable
	}

	return rows, recordsOK
}

func (s *Server) groups(booth *Booth, r *http.Request) ([]tournament.Group, error) {
	records := s.recordsFor(booth)
	if records == nil {
		return []tournament.Group{}, nil
	}

	groups, err := records.Groups(r.Context())
	if err != nil {
		s.logger.Warn("could not read the bracket", "booth", booth.ID(), "error", err.Error())

		return nil, err
	}

	// Always an array, never null, so the page can map over it.
	if groups == nil {
		return []tournament.Group{}, nil
	}

	return groups, nil
}

// seat is who the standings put in the final: the top seats entries in table order (count set by
// `LMS_FINAL_SEATS`). Used only when no final spec is written; otherwise tournament.SeatFinal
// applies. Ties at the cut follow the table's deterministic order (by name).
func seat(table tournament.Table, seats int) []string {
	if seats <= 0 {
		return []string{}
	}

	players := make([]string, 0, seats)
	for _, row := range table.Entries {
		if len(players) == seats {
			break
		}

		players = append(players, row.Name)
	}

	return players
}
