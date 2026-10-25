package caster

import (
	"net/http"
	"strconv"
)

// The mod cannot reach the database; its only link to the booth is the connection it opened, so the
// roster of the group now playing rides the ingest reply, like the commands and the rules.

// offeredBracket is the state document a push reply carries: the roster of the group now playing
// only. The final's seats are computed by the standings (bracket.go), never carried here.
type offeredBracket struct {
	Groups []offeredBracketGroup `json:"groups"`
}

// offeredBracketGroup is one group of the bracket and who is in it, as the bracket page wrote them.
type offeredBracketGroup struct {
	Number int    `json:"number"`
	Name   string `json:"name"`

	// Members is the Nitrox names the mod matches its players against. Always an array, never null.
	Members []string `json:"members"`
}

// owedBracket is the roster this push should carry back, or nil. It never fails the push: a
// database error costs the mod its bracket, not the match its feed. It is re-offered on every push,
// with no acknowledgement or revision.
func (s *Server) owedBracket(booth *Booth, r *http.Request) *offeredBracket {
	records := s.recordsFor(booth)
	if records == nil {
		return nil
	}

	// The playing group comes from the mod's last push, the same source the reconciler uses.
	snapshot, pushed := booth.store.Latest()
	if !pushed || snapshot.Match == nil {
		return nil
	}
	playing := snapshot.Match.Group

	ctx, cancel := contextWithDeadline(r)
	defer cancel()

	groups, err := records.Groups(ctx)
	if err != nil {
		s.logger.Warn("could not read the bracket for the ingest reply", "booth", booth.ID(),
			"error", err.Error())

		return nil
	}

	for _, group := range groups {
		if group.Number != playing {
			continue
		}

		if len(group.Members) == 0 {
			// An empty group is an unfilled bracket; offering it would read as "remove everybody".
			return nil
		}

		members := make([]string, len(group.Members))
		copy(members, group.Members)

		return &offeredBracket{Groups: []offeredBracketGroup{{
			Number: group.Number, Name: group.Name, Members: members,
		}}}
	}

	// Unknown group (mid-edit or a typo): offer nothing and the mod keeps what it has.
	s.logger.Info("no bracket row for the group now playing", "booth", booth.ID(),
		"group", strconv.Itoa(playing))

	return nil
}
