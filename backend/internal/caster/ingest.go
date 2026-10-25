package caster

import (
	"encoding/json"
	"io"
	"math"
	"net/http"
	"strconv"
	"strings"

	"github.com/caesarakalaeii/subnautica-creator-royale/backend/internal/config"
	"github.com/caesarakalaeii/subnautica-creator-royale/backend/internal/telemetry"
)

// ingestPath is where the mod pushes. Push rather than poll, so no inbound route to the game host
// is needed.
const ingestPath = "/ingest"

// maxPushBytes bounds one push: a full lobby is about 8 KiB, so ample headroom while still capping
// what an anonymous caller can make the process buffer.
const maxPushBytes = 1 << 20

// bearerPrefix is the only auth scheme this endpoint understands.
const bearerPrefix = "Bearer "

// badTokenRate is how many log lines per second a refused push may produce. It bounds the log, not
// the endpoint: every bad token gets a 401, since a 429 would tell a guesser something.
const badTokenRate = 1.0

// ingestResponse is what a push gets back. LastSequence is the cursor the mod resumes from.
// NewEvents and LastSequence have no omitempty: zero is a meaningful answer.
type ingestResponse struct {
	Accepted     bool   `json:"accepted"`
	NewEvents    int    `json:"newEvents"`
	LastSequence int64  `json:"lastSequence"`
	Error        string `json:"error,omitempty"`

	// Commands is what an operator has asked the mod to do, oldest first. It rides the reply because
	// the booth cannot reach the game server. Always an array, even on a refusal.
	Commands []offeredCommand `json:"commands"`

	// Rules is a tournament config an operator has written and the game server has not adopted yet.
	// Omitted when there is none.
	Rules *offeredRules `json:"rules,omitempty"`

	// Bracket is the roster of the group now playing, re-offered on every push and replacing what
	// the mod held. Omitted when there is none, and the mod keeps what it has.
	Bracket *offeredBracket `json:"bracket,omitempty"`
}

// offeredCommand is one verb the mod should run, as typed, and the id it acknowledges by.
type offeredCommand struct {
	ID   int64  `json:"id"`
	Verb string `json:"verb"`
}

// ingest reads one telemetry push into the store. Each failure says whether the service is not
// configured, the caller not authorised, or the push not understood.
func (s *Server) ingest(w http.ResponseWriter, r *http.Request) {
	booth := s.booths.asked(r, s.now())

	// A known booth with no ingest token (e.g. LMS_INGEST_TOKEN unset) is a 503, not a 401: the
	// deployment is wrong, not the caller. An unknown booth falls through to the 401 below so
	// booths cannot be enumerated.
	if booth != nil && !booth.ingestConfigured() {
		reply(w, http.StatusServiceUnavailable,
			ingestResponse{Error: "ingest is not configured on this service"})
		return
	}

	if !booth.mayPush(r) {
		if s.badTokenLimit.allow() {
			s.logger.Warn("rejected a telemetry push", "reason", "bad token",
				"booth", askedFor(r), "remote", r.RemoteAddr,
				"also_refused_since", s.badTokenLimit.drainRefusals())
		}

		reply(w, http.StatusUnauthorized, ingestResponse{Error: "bad or missing bearer token"})

		return
	}

	// Rate limit after the token check, so an anonymous flood cannot spend the mod's allowance.
	if !s.pushLimit.allow() {
		if s.pushLimit.refusals() == 1 {
			// Log once when limiting starts and once with the count when it ends.
			s.logger.Warn("rate-limiting telemetry pushes",
				"limit_per_second", s.cfg.IngestRate, "remote", r.RemoteAddr)
		}

		w.Header().Set("Retry-After", retryAfter(s.cfg.IngestRate))

		// Return the cursor even here so the mod resends from the right place.
		reply(w, http.StatusTooManyRequests, ingestResponse{
			Error:        "too many pushes; this service is rate limited",
			LastSequence: booth.store.LastSequence(),
		})

		return
	}

	if refused := s.pushLimit.drainRefusals(); refused > 0 {
		s.logger.Warn("no longer rate-limiting telemetry pushes", "refused", refused)
	}

	payload, err := io.ReadAll(http.MaxBytesReader(w, r.Body, maxPushBytes))
	if err != nil {
		reply(w, http.StatusRequestEntityTooLarge,
			ingestResponse{Error: "push is larger than this service accepts"})
		return
	}

	snapshot, err := telemetry.Parse(payload)
	if err != nil {
		// Logged as an error: a version mismatch means the mod and booth are different builds.
		s.logger.Error("rejected a telemetry push", "reason", err.Error())
		reply(w, http.StatusBadRequest, ingestResponse{Error: err.Error()})
		return
	}

	// The booth is the one whose token was presented; nothing in the payload can redirect the push
	// to another booth.
	added := booth.store.Apply(snapshot)

	// Forwarded verbatim, to this booth's fan-out only. Never blocks; slow clients are dropped.
	booth.hub.Broadcast(payload)

	// Debug: this arrives about once a second.
	s.logger.Debug("telemetry",
		"booth", booth.ID(),
		"players", len(snapshot.Players),
		"events_sent", len(snapshot.Events),
		"events_new", added,
		"last_seq", booth.store.LastSequence())

	reply(w, http.StatusAccepted, ingestResponse{
		Accepted:     true,
		NewEvents:    added,
		LastSequence: booth.store.LastSequence(),
		Commands:     s.owedCommands(booth, r),
		Rules:        s.owedRules(booth, r),
		Bracket:      s.owedBracket(booth, r),
	})
}

// askedFor is the booth id a request named, for logging a refusal. Never sent back, so a refusal
// does not reveal whether the id exists.
func askedFor(r *http.Request) string {
	if id := r.PathValue(boothParam); id != "" {
		return id
	}

	return config.TournamentBooth
}

// retryAfter is how long until a token is back, in whole seconds, derived from the rate so a rate
// below one a second does not invite immediate retries.
func retryAfter(perSecond float64) string {
	if perSecond <= 0 {
		return "1"
	}

	return strconv.Itoa(int(math.Max(1, math.Ceil(1/perSecond))))
}

// bearerToken is the token a request presented in its Authorization header, or empty. Checking it
// is credential.matches, the package's only token comparison.
func bearerToken(r *http.Request) string {
	header := r.Header.Get("Authorization")
	if !strings.HasPrefix(header, bearerPrefix) {
		return ""
	}

	return strings.TrimPrefix(header, bearerPrefix)
}

// reply writes the JSON body. It never echoes the payload back, only what this service made of it.
func reply(w http.ResponseWriter, status int, body ingestResponse) {
	// Always an array, never null, including on a 401 or 429.
	if body.Commands == nil {
		body.Commands = []offeredCommand{}
	}

	w.Header().Set("Content-Type", "application/json")
	w.WriteHeader(status)
	_ = json.NewEncoder(w).Encode(body)
}
