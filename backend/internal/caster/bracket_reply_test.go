package caster

import (
	"net/http"
	"strings"
	"testing"

	"github.com/caesarakalaeii/subnautica-creator-royale/backend/internal/tournament"
)

func TestTheIngestReplyCarriesThePlayingGroupsRoster(t *testing.T) {
	t.Parallel()

	server, _ := rostered(t, 2, "Ada", "Bea")

	reply := decode(t, pushAuthorised(t, server, examplePayload(t)))

	if reply.Bracket == nil || len(reply.Bracket.Groups) != 1 {
		t.Fatalf("bracket = %+v, want the one group now playing", reply.Bracket)
	}

	offered := reply.Bracket.Groups[0]
	if offered.Number != 2 || offered.Name != "Group A" ||
		strings.Join(offered.Members, ",") != "Ada,Bea" {
		t.Fatalf("offered group = %+v, want number 2 with Ada and Bea", offered)
	}
}

func TestTheBracketIsOmittedWhenNothingHasBeenPushed(t *testing.T) {
	t.Parallel()

	records := &fakeRecords{}
	if err := records.SetGroups(t.Context(), []tournament.Group{
		{Number: 2, Name: "Group A", Members: []string{"Ada", "Bea"}, Seats: seatsFor([]string{"Ada", "Bea"})},
	}, nil); err != nil {
		t.Fatalf("set groups: %v", err)
	}
	server := boothServer(t, records)

	// A push with no match: before the first start or after a reset.
	recorder := push(t, server, []byte(`{"v":6,"at":"2026-10-24T12:00:00.000Z"}`), token)

	if recorder.Code != http.StatusAccepted {
		t.Fatalf("push = %d, want accepted", recorder.Code)
	}
	if decoded := decode(t, recorder); decoded.Bracket != nil {
		t.Fatalf("bracket = %+v, want it absent with no match pushed", decoded.Bracket)
	}
	if strings.Contains(recorder.Body.String(), `"bracket"`) {
		t.Fatalf("reply carried a bracket field at all: %q", recorder.Body.String())
	}
}

func TestAnEmptyPlayingGroupIsOmittedRatherThanOffered(t *testing.T) {
	t.Parallel()

	server, _ := rostered(t, 2)

	if decoded := decode(t, pushAuthorised(t, server, examplePayload(t))); decoded.Bracket != nil {
		t.Fatalf("bracket = %+v, want an empty group omitted", decoded.Bracket)
	}
}

func TestTheBracketIsOmittedWithoutADatabase(t *testing.T) {
	t.Parallel()

	server := newTestServer(t, token)
	server.Store().Apply(playing(2))

	if decoded := decode(t, pushAuthorised(t, server, examplePayload(t))); decoded.Bracket != nil {
		t.Fatalf("bracket = %+v, want it absent without a database", decoded.Bracket)
	}
}

func TestTheBracketIsOmittedWhenTheDatabaseIsUnreachable(t *testing.T) {
	t.Parallel()

	records := &fakeRecords{broken: true}
	server := boothServer(t, records)
	server.Store().Apply(playing(2))

	recorder := pushAuthorised(t, server, examplePayload(t))
	if recorder.Code != http.StatusAccepted {
		t.Fatalf("push = %d, want it accepted anyway", recorder.Code)
	}
	if decoded := decode(t, recorder); decoded.Bracket != nil {
		t.Fatalf("bracket = %+v, want it absent when the database is broken", decoded.Bracket)
	}
}

func TestTheBracketIsOfferedAgainOnTheNextPush(t *testing.T) {
	t.Parallel()

	server, _ := rostered(t, 2, "Ada", "Bea")
	pushAuthorised(t, server, examplePayload(t))
	pushAuthorised(t, server, examplePayload(t))

	reply := decode(t, pushAuthorised(t, server, examplePayload(t)))

	if reply.Bracket == nil || len(reply.Bracket.Groups) != 1 {
		t.Fatalf("bracket on the third push = %+v, want it still offered", reply.Bracket)
	}
}
