package caster

import (
	"encoding/json"
	"fmt"
	"net/http"
	"net/http/httptest"
	"strconv"
	"strings"
	"testing"
	"time"

	"github.com/caesarakalaeii/subnautica-creator-royale/backend/internal/tournament"
)

const oneCommand = `{"verb":"status","by":"ops"}`

// pushWithWatermark is a telemetry push that says which command the mod has run up to.
func pushWithWatermark(t *testing.T, server *Server, ran int64) ingestResponse {
	t.Helper()

	request := httptest.NewRequest(http.MethodPost, ingestPath,
		strings.NewReader(string(examplePayload(t))))
	request.Header.Set("Authorization", "Bearer "+token)
	request.Header.Set(commandWatermarkHeader, strconv.FormatInt(ran, 10))

	recorder := httptest.NewRecorder()
	server.Handler().ServeHTTP(recorder, request)

	if recorder.Code != http.StatusAccepted {
		t.Fatalf("push got %d, want 202: %s", recorder.Code, recorder.Body.String())
	}

	return decode(t, recorder)
}

func queue(t *testing.T, server *Server, verb string) commandResponse {
	t.Helper()

	body := fmt.Sprintf(`{"verb":%q,"by":"ops"}`, verb)
	recorder := ask(t, server, http.MethodPost, commandsPath, body, operatorToken)

	if recorder.Code != http.StatusAccepted {
		t.Fatalf("queueing %q got %d, want 202: %s", verb, recorder.Code, recorder.Body.String())
	}

	var queued commandResponse
	if err := json.Unmarshal(recorder.Body.Bytes(), &queued); err != nil {
		t.Fatalf("reply was not JSON: %v (%q)", err, recorder.Body.String())
	}

	return queued
}

func TestACommandIsRefusedWithoutTheOperatorToken(t *testing.T) {
	t.Parallel()

	server := boothServer(t, &fakeRecords{})

	for _, presented := range []string{"", token, "watching", "wrong"} {
		recorder := ask(t, server, http.MethodPost, commandsPath, oneCommand, presented)

		if recorder.Code != http.StatusUnauthorized {
			t.Errorf("token %q got %d, want 401", presented, recorder.Code)
		}
	}
}

func TestACommandIsQueuedWithTheOperatorToken(t *testing.T) {
	t.Parallel()

	records := &fakeRecords{}
	queued := queue(t, boothServer(t, records), "round force")

	if !queued.Queued || queued.ID == 0 {
		t.Fatalf("a queued command comes back with the id it was queued as: %+v", queued)
	}

	if queued.State != tournament.StateQueued {
		t.Errorf("state = %q, want %q", queued.State, tournament.StateQueued)
	}

	if len(records.commands) != 1 || records.commands[0].Verb != "round force" {
		t.Fatalf("the verb goes into the queue as typed: %+v", records.commands)
	}

	if records.commands[0].By != "ops" {
		t.Errorf("who asked is what the token cannot answer: %+v", records.commands[0])
	}
}

func TestAVerbIsSqueezedTheWayTheModSqueezesIt(t *testing.T) {
	t.Parallel()

	records := &fakeRecords{}
	queued := queue(t, boothServer(t, records), "  podclear   all  ")

	if queued.Verb != "podclear all" {
		t.Fatalf("verb = %q, want %q", queued.Verb, "podclear all")
	}
}

func TestACommandWithNothingInItIsRefusedWithASentence(t *testing.T) {
	t.Parallel()

	server := boothServer(t, &fakeRecords{})

	for _, body := range []string{
		`{"verb":"","by":"ops"}`,
		`{"verb":"   ","by":"ops"}`,
		`{"verb":"status","by":""}`,
		`not json at all`,
	} {
		recorder := ask(t, server, http.MethodPost, commandsPath, body, operatorToken)

		if recorder.Code != http.StatusBadRequest {
			t.Errorf("%s got %d, want 400", body, recorder.Code)
		}
	}
}

func TestAVerbThisBuildHasNeverHeardOfIsStillQueued(t *testing.T) {
	t.Parallel()

	queued := queue(t, boothServer(t, &fakeRecords{}), "wobble")

	if !queued.Queued {
		t.Fatalf("the mod refuses a verb, not the booth: %+v", queued)
	}
}

func TestACommandIsRefusedWithNoDatabaseRatherThanKeptInMemory(t *testing.T) {
	t.Parallel()

	server := boothServer(t, nil)
	recorder := ask(t, server, http.MethodPost, commandsPath, oneCommand, operatorToken)

	if recorder.Code != http.StatusServiceUnavailable {
		t.Fatalf("got %d, want 503: %s", recorder.Code, recorder.Body.String())
	}
}

func TestACommandRidesBackOnTheReplyToAPush(t *testing.T) {
	t.Parallel()

	records := &fakeRecords{}
	server := boothServer(t, records)

	queued := queue(t, server, "start")
	reply := pushWithWatermark(t, server, 0)

	if len(reply.Commands) != 1 {
		t.Fatalf("the push should carry the one queued command: %+v", reply.Commands)
	}

	if reply.Commands[0].ID != queued.ID || reply.Commands[0].Verb != "start" {
		t.Fatalf("reply carried %+v, want id %d and 'start'", reply.Commands[0], queued.ID)
	}

	if records.commands[0].State != tournament.StateSent {
		t.Errorf("a command that went out is marked sent: %+v", records.commands[0])
	}
}

func TestACommandTheModHasRunIsNotOfferedAgain(t *testing.T) {
	t.Parallel()

	server := boothServer(t, &fakeRecords{})
	queued := queue(t, server, "pause")

	if reply := pushWithWatermark(t, server, queued.ID); len(reply.Commands) != 0 {
		t.Fatalf("a mod that has run %d is owed nothing: %+v", queued.ID, reply.Commands)
	}
}

func TestACommandNobodyHasAcknowledgedIsOfferedAgain(t *testing.T) {
	t.Parallel()

	server := boothServer(t, &fakeRecords{})
	queue(t, server, "skip")

	first := pushWithWatermark(t, server, 0)
	second := pushWithWatermark(t, server, 0)

	if len(first.Commands) != 1 || len(second.Commands) != 1 {
		t.Fatalf("both pushes should carry it: %+v then %+v", first.Commands, second.Commands)
	}
}

func TestAModThatDoesNotSpeakCommandsIsOfferedNone(t *testing.T) {
	t.Parallel()

	records := &fakeRecords{}
	server := boothServer(t, records)

	queue(t, server, "start")

	reply := decode(t, push(t, server, examplePayload(t), token))

	if len(reply.Commands) != 0 {
		t.Fatalf("no header means no commands: %+v", reply.Commands)
	}

	if records.commands[0].State != tournament.StateQueued {
		t.Errorf("and nothing is marked sent: %+v", records.commands[0])
	}
}

func TestAWatermarkThatWillNotParseIsTreatedAsNoHeader(t *testing.T) {
	t.Parallel()

	server := boothServer(t, &fakeRecords{})
	queue(t, server, "start")

	request := httptest.NewRequest(http.MethodPost, ingestPath,
		strings.NewReader(string(examplePayload(t))))
	request.Header.Set("Authorization", "Bearer "+token)
	request.Header.Set(commandWatermarkHeader, "not a number")

	recorder := httptest.NewRecorder()
	server.Handler().ServeHTTP(recorder, request)

	// The push still lands.
	if recorder.Code != http.StatusAccepted {
		t.Fatalf("got %d, want 202: %s", recorder.Code, recorder.Body.String())
	}

	if commands := decode(t, recorder).Commands; len(commands) != 0 {
		t.Fatalf("and it carries nothing: %+v", commands)
	}
}

func TestACommandNobodyCollectedExpiresRatherThanFiringLater(t *testing.T) {
	t.Parallel()

	records := &fakeRecords{}
	server := boothServer(t, records)

	queue(t, server, "start")

	// Backdated past the TTL.
	records.commands[0].QueuedAt = time.Now().Add(-time.Hour)

	if reply := pushWithWatermark(t, server, 0); len(reply.Commands) != 0 {
		t.Fatalf("an expired command must not fire: %+v", reply.Commands)
	}

	if records.commands[0].State != tournament.StateExpired {
		t.Errorf("and the row says so, as evidence: %+v", records.commands[0])
	}
}

func TestAFreshCommandSurvivesTheExpirySweepOnEveryPush(t *testing.T) {
	t.Parallel()

	records := &fakeRecords{}
	server := boothServer(t, records)

	queue(t, server, "status")
	records.commands[0].QueuedAt = time.Now()

	if reply := pushWithWatermark(t, server, 0); len(reply.Commands) != 1 {
		t.Fatalf("a command queued a moment ago is not two minutes old: %+v", reply.Commands)
	}
}

func TestADatabaseThatHasGoneAwayDoesNotCostThePush(t *testing.T) {
	t.Parallel()

	records := &fakeRecords{broken: true}
	server := boothServer(t, records)

	reply := pushWithWatermark(t, server, 0)

	if !reply.Accepted {
		t.Fatalf("the match still goes on the wire: %+v", reply)
	}

	if len(reply.Commands) != 0 {
		t.Fatalf("and there is nothing to run: %+v", reply.Commands)
	}
}

func TestEveryReplyCarriesACommandsArray(t *testing.T) {
	t.Parallel()

	server := boothServer(t, &fakeRecords{})

	for _, recorder := range []*httptest.ResponseRecorder{
		push(t, server, examplePayload(t), "wrong-token"),
		push(t, server, []byte(`{"nonsense":true}`), token),
	} {
		if !strings.Contains(recorder.Body.String(), `"commands":[]`) {
			t.Errorf("reply %q has no commands array", recorder.Body.String())
		}
	}
}

func TestOneReplyCarriesAtMostAReplysWorth(t *testing.T) {
	t.Parallel()

	records := &fakeRecords{}
	server := boothServer(t, records)

	// Queued directly, bypassing the route's write limit, to build a backlog longer than one reply.
	for i := 0; i < tournament.MaxCommandsPerReply+3; i++ {
		if _, err := records.Queue(t.Context(), "status", "ops"); err != nil {
			t.Fatalf("queue: %v", err)
		}
	}

	if got := len(pushWithWatermark(t, server, 0).Commands); got != tournament.MaxCommandsPerReply {
		t.Fatalf("one reply carries %d, not %d", tournament.MaxCommandsPerReply, got)
	}
}

func TestGettingTheCommandsPathIsNotAllowed(t *testing.T) {
	t.Parallel()

	recorder := ask(t, boothServer(t, &fakeRecords{}), http.MethodGet, commandsPath, "", operatorToken)

	if recorder.Code != http.StatusMethodNotAllowed {
		t.Fatalf("got %d, want 405", recorder.Code)
	}
}
