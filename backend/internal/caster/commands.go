package caster

import (
	"net/http"
	"strconv"
	"strings"
	"time"

	"github.com/caesarakalaeii/subnautica-creator-royale/backend/internal/tournament"
)

// commandsPath is where an operator asks the mod to do something.
const commandsPath = "/commands"

// commandWatermarkHeader is how the mod says which command it has run up to. A header keeps the
// push body (the telemetry contract) unchanged. A mod that omits it is offered no commands.
const commandWatermarkHeader = "X-Lms-Commands-Ran"

// maxVerbLength bounds what may be written into the queue: well above `podclear all` plus a player
// name, small enough that an operator token cannot flood the table.
const maxVerbLength = 200

// commandDeadline bounds what the database may cost the push path, so a slow or missing database
// never blocks a push the match depends on.
const commandDeadline = 2 * time.Second

// commandsDrawn is how much of the queue's history the console is given.
const commandsDrawn = 50

// commandRequest is what the console posts. The verb is passed through as typed; the mod's
// `MatchHost` is the only authority on what a verb may do.
type commandRequest struct {
	Verb string `json:"verb"`

	// By is whoever is asking. Required because the operator token is shared and cannot tell who.
	By string `json:"by"`
}

// commandResponse is what queueing one gets back, including the id the console then watches.
type commandResponse struct {
	Queued bool   `json:"queued"`
	ID     int64  `json:"id"`
	Verb   string `json:"verb"`
	State  string `json:"state"`
	Error  string `json:"error,omitempty"`
}

// queueCommand takes one command from the console.
func (s *Server) queueCommand(w http.ResponseWriter, r *http.Request) {
	booth, ok := s.authorisedToWrite(w, r, commandsPath)
	if !ok {
		return
	}

	records := s.recordsFor(booth)
	if records == nil {
		// Not held in memory: a restart would silently lose it, which is worse than a refusal.
		writeError(w, http.StatusServiceUnavailable,
			"there is no database on this service, so a command cannot be queued")

		return
	}

	asked, ok := decodeJSON[commandRequest](w, r, maxWriteBytes)
	if !ok {
		return
	}

	verb, by, reason := commandFrom(asked)
	if reason != "" {
		writeError(w, http.StatusBadRequest, reason)
		return
	}

	queued, err := records.Queue(r.Context(), verb, by)
	if err != nil {
		s.logger.Error("could not queue a command", "booth", booth.ID(), "error", err.Error(),
			"verb", verb, "by", by)
		writeError(w, http.StatusServiceUnavailable,
			"the database refused the command; it was not queued")

		return
	}

	s.logger.Info("command queued", "booth", booth.ID(), "id", queued.ID, "verb", verb, "by", by)

	replyJSON(w, http.StatusAccepted, commandResponse{
		Queued: true, ID: queued.ID, Verb: verb, State: tournament.StateQueued,
	})
}

// commandFrom validates one request, and the reason is written for whoever typed it.
func commandFrom(asked commandRequest) (verb, by, reason string) {
	// Collapse whitespace the way the mod does (AdminCommands.Squeezed).
	verb = strings.Join(strings.Fields(asked.Verb), " ")
	if verb == "" {
		return "", "", "verb has to name what the mod should do, such as status or `round force`"
	}

	if len(verb) > maxVerbLength {
		return "", "", "that is longer than any command this build has"
	}

	by = strings.TrimSpace(asked.By)
	if by == "" {
		return "", "", "by has to name whoever is asking, for reading afterwards"
	}

	return verb, by, ""
}

// owedCommands is what this push should carry back, and it never fails the push. Within
// commandDeadline it expires stale commands, reads those above the mod's watermark and marks them
// sent; a database error only logs a warning.
func (s *Server) owedCommands(booth *Booth, r *http.Request) []offeredCommand {
	records := s.recordsFor(booth)
	if records == nil {
		return []offeredCommand{}
	}

	watermark, speaks := watermarkOf(r)
	if !speaks {
		// The mod does not accept commands (older build or `telemetry.acceptsCommands: false`).
		return []offeredCommand{}
	}

	ctx, cancel := contextWithDeadline(r)
	defer cancel()

	now := time.Now()

	if s.cfg.CommandsExpire() {
		gone, err := records.Expire(ctx, now.Add(-s.cfg.CommandTTL))
		if err != nil {
			s.logger.Warn("could not expire the commands", "booth", booth.ID(),
				"error", err.Error())

			return []offeredCommand{}
		}

		if gone > 0 {
			// Warn: each one is an operator's command that never ran.
			s.logger.Warn("gave up on commands nothing collected", "booth", booth.ID(),
				"commands", gone, "waited", s.cfg.CommandTTL.String())
		}
	}

	owed, err := records.Owed(ctx, watermark)
	if err != nil {
		s.logger.Warn("could not read what the mod is owed", "booth", booth.ID(),
			"error", err.Error())

		return []offeredCommand{}
	}

	if len(owed) == 0 {
		return []offeredCommand{}
	}

	ids := make([]int64, 0, len(owed))
	offered := make([]offeredCommand, 0, len(owed))

	for _, row := range owed {
		ids = append(ids, row.ID)
		offered = append(offered, offeredCommand{ID: row.ID, Verb: row.Verb})
	}

	if err := records.MarkSent(ctx, ids, now); err != nil {
		// Still send them: a stale label on the console is better than a withheld command.
		s.logger.Warn("could not mark commands sent", "booth", booth.ID(),
			"error", err.Error(), "commands", len(ids))
	}

	s.logger.Info("handing commands to the mod", "booth", booth.ID(),
		"commands", len(offered), "above", watermark)

	return offered
}

// watermarkOf reads the mod's cursor off the request, and says whether it sent one. An unparsable
// header counts as absent rather than failing the push.
func watermarkOf(r *http.Request) (int64, bool) {
	raw := strings.TrimSpace(r.Header.Get(commandWatermarkHeader))
	if raw == "" {
		return 0, false
	}

	ran, err := strconv.ParseInt(raw, 10, 64)
	if err != nil || ran < 0 {
		return 0, false
	}

	return ran, true
}
