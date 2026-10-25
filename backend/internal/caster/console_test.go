package caster

import (
	"encoding/json"
	"net/http"
	"strconv"
	"strings"
	"testing"

	"github.com/caesarakalaeii/subnautica-creator-royale/backend/internal/tournament"
)

const oneReport = `{
  "v": 1,
  "at": "2026-10-24T12:00:00.000Z",
  "acceptsCommands": true,
  "ran": 0,
  "connected": [
    {"name": "Ada", "inMatch": true},
    {"name": "Bea", "inMatch": true},
    {"name": "Caster", "inMatch": false}
  ],
  "verbs": [
    {"verb": "status", "takesSubject": false, "mustBeTyped": false},
    {"verb": "podclear all", "takesSubject": false, "mustBeTyped": true},
    {"verb": "ghost", "takesSubject": true, "mustBeTyped": false}
  ],
  "answers": []
}`

func report(t *testing.T, server *Server, body, bearer string) *http.Response {
	t.Helper()

	return ask(t, server, http.MethodPost, consolePath, body, bearer).Result()
}

func viewConsole(t *testing.T, server *Server) consoleView {
	t.Helper()

	recorder := ask(t, server, http.MethodGet, consolePath, "", operatorToken)
	if recorder.Code != http.StatusOK {
		t.Fatalf("GET %s got %d: %s", consolePath, recorder.Code, recorder.Body.String())
	}

	var view consoleView
	if err := json.Unmarshal(recorder.Body.Bytes(), &view); err != nil {
		t.Fatalf("console is not JSON: %v (%q)", err, recorder.Body.String())
	}

	return view
}

func TestTheConsoleNamesAGhostAndTheBroadcastDoesNot(t *testing.T) {
	t.Parallel()

	server := boothServer(t, &fakeRecords{})

	if got := report(t, server, oneReport, token).StatusCode; got != http.StatusAccepted {
		t.Fatalf("posting a report got %d, want 202", got)
	}

	view := viewConsole(t, server)

	if view.Report == nil || len(view.Report.Connected) != 3 {
		t.Fatalf("the console names everybody connected: %+v", view.Report)
	}

	ghosts := 0
	for _, one := range view.Report.Connected {
		if !one.InMatch {
			ghosts++
		}
	}

	if ghosts != 1 {
		t.Fatalf("exactly one of the three is not in the match, got %d: %+v", ghosts, view.Report.Connected)
	}
}

func TestAConsoleReportNeedsTheIngestToken(t *testing.T) {
	t.Parallel()

	server := boothServer(t, &fakeRecords{})

	for _, presented := range []string{"", "watching", operatorToken, "wrong"} {
		if got := report(t, server, oneReport, presented).StatusCode; got != http.StatusUnauthorized {
			t.Errorf("token %q got %d, want 401", presented, got)
		}
	}
}

func TestReadingTheConsoleNeedsTheOperatorToken(t *testing.T) {
	t.Parallel()

	server := boothServer(t, &fakeRecords{})
	report(t, server, oneReport, token)

	for _, presented := range []string{"", token, "watching", "wrong"} {
		recorder := ask(t, server, http.MethodGet, consolePath, "", presented)

		if recorder.Code != http.StatusUnauthorized {
			t.Errorf("token %q got %d, want 401", presented, recorder.Code)
		}
	}
}

func TestAReportFromAnotherBuildIsRefusedWithTheReason(t *testing.T) {
	t.Parallel()

	server := boothServer(t, &fakeRecords{})
	other := strings.Replace(oneReport, `"v": 1`, `"v": 99`, 1)

	recorder := ask(t, server, http.MethodPost, consolePath, other, token)

	if recorder.Code != http.StatusBadRequest {
		t.Fatalf("got %d, want 400", recorder.Code)
	}

	if !strings.Contains(recorder.Body.String(), "different builds") {
		t.Errorf("the reason should say what is actually wrong: %q", recorder.Body.String())
	}
}

func TestABoothWithNoReportYetSaysSoRatherThanDrawingAnEmptyRoster(t *testing.T) {
	t.Parallel()

	view := viewConsole(t, boothServer(t, &fakeRecords{}))

	if view.Report != nil {
		t.Fatalf("nothing has been posted, so there is no report: %+v", view.Report)
	}

	if view.ReportedAt != "" {
		t.Errorf("and nothing to say about when: %q", view.ReportedAt)
	}
}

func TestAnAnswerIsFiledAgainstTheCommandItAnswers(t *testing.T) {
	t.Parallel()

	records := &fakeRecords{}
	server := boothServer(t, records)

	queued := queue(t, server, "round")

	answered := strings.Replace(oneReport, `"answers": []`,
		`"answers": [{"id": `+strconv.FormatInt(queued.ID, 10)+`, "verb": "round", "answer": "a group is 5 and this counted 4."}]`, 1)

	if got := report(t, server, answered, token).StatusCode; got != http.StatusAccepted {
		t.Fatalf("posting a report got %d, want 202", got)
	}

	view := viewConsole(t, server)

	if len(view.Commands) != 1 {
		t.Fatalf("the queue should hold the one command: %+v", view.Commands)
	}

	if view.Commands[0].State != tournament.StateRan {
		t.Errorf("state = %q, want %q", view.Commands[0].State, tournament.StateRan)
	}

	if view.Commands[0].Answer != "a group is 5 and this counted 4." {
		t.Errorf("the sentence travels word for word, got %q", view.Commands[0].Answer)
	}

	if view.Commands[0].RanAt == nil {
		t.Errorf("and a command that ran has a time on it")
	}
}

func TestADatabaseThatHasGoneAwayDoesNotCostTheReport(t *testing.T) {
	t.Parallel()

	server := boothServer(t, &fakeRecords{broken: true})

	answered := strings.Replace(oneReport, `"answers": []`,
		`"answers": [{"id": 1, "verb": "status", "answer": "in the lobby"}]`, 1)

	if got := report(t, server, answered, token).StatusCode; got != http.StatusAccepted {
		t.Fatalf("got %d, want 202: the console's own view is already updated", got)
	}
}

func TestTheVocabularyIsCarriedThroughAsTheModSentIt(t *testing.T) {
	t.Parallel()

	server := boothServer(t, &fakeRecords{})
	report(t, server, oneReport, token)

	view := viewConsole(t, server)

	found := map[string]consoleVerb{}
	for _, verb := range view.Report.Verbs {
		found[verb.Verb] = verb
	}

	if !found["podclear all"].MustBeTyped {
		t.Errorf("`podclear all` has to be typed, not pressed: %+v", found["podclear all"])
	}

	if !found["ghost"].TakesSubject {
		t.Errorf("`ghost` needs a name field beside it: %+v", found["ghost"])
	}

	if found["status"].MustBeTyped || found["status"].TakesSubject {
		t.Errorf("`status` is a plain button: %+v", found["status"])
	}
}

func TestAReportNeverBecomesMatchState(t *testing.T) {
	t.Parallel()

	server := boothServer(t, &fakeRecords{})
	report(t, server, oneReport, token)

	// A console report must not reach the match store that overlays read.
	if _, pushed := server.Store().Latest(); pushed {
		t.Fatalf("a console report must never reach the store the overlays read")
	}

	if server.Store().LastSequence() != 0 {
		t.Errorf("and it must not consume a slot in the event window")
	}
}

func TestPuttingToTheConsolePathIsNotAllowed(t *testing.T) {
	t.Parallel()

	recorder := ask(t, boothServer(t, &fakeRecords{}), http.MethodPut, consolePath, "", operatorToken)

	if recorder.Code != http.StatusMethodNotAllowed {
		t.Fatalf("got %d, want 405", recorder.Code)
	}
}

// reportWithConfig is a report from a server that says what config it is running.
const reportWithConfig = `{
  "v": 1,
  "at": "2026-10-24T12:00:00.000Z",
  "acceptsCommands": true,
  "ran": 0,
  "connected": [{"name": "Ada", "inMatch": true}],
  "verbs": [{"verb": "status", "takesSubject": false, "mustBeTyped": false}],
  "answers": [],
  "config": "match:\n  cycleLength: 10m\n"
}`

func TestTheConsolePrefillsTheConfigTheServerIsRunning(t *testing.T) {
	t.Parallel()

	server := boothServer(t, &fakeRecords{})

	if got := report(t, server, reportWithConfig, token).StatusCode; got != http.StatusAccepted {
		t.Fatalf("posting a report got %d, want 202", got)
	}

	view := viewConsole(t, server)

	if view.Rules == nil {
		t.Fatal("the console has nothing to edit, so an operator must retype the file")
	}

	if view.Rules.Yaml != "match:\n  cycleLength: 10m\n" {
		t.Fatalf("the document is not what the server is running: %q", view.Rules.Yaml)
	}

	// Revision 0 means the file on the game server's own disk.
	if view.Rules.Revision != 0 {
		t.Fatalf("a config nobody revised is revision 0, got %d", view.Rules.Revision)
	}
}

func TestTheRunningFileIsStillOfferedAfterTheFirstRevisionIsWritten(t *testing.T) {
	t.Parallel()

	server := boothServer(t, &fakeRecords{})

	if got := report(t, server, reportWithConfig, token).StatusCode; got != http.StatusAccepted {
		t.Fatalf("posting a report got %d, want 202", got)
	}

	written := writeRules(t, server, someYaml)
	view := viewConsole(t, server)

	if view.Rules == nil || view.Rules.Revision != written.Revision {
		t.Fatalf("rules = %+v, want revision %d", view.Rules, written.Revision)
	}

	if view.Running == nil {
		t.Fatal("the file the game server is running is gone the moment anything is written")
	}

	if view.Running.Yaml != "match:\n  cycleLength: 10m\n" {
		t.Fatalf("running = %q, want what the game server reported", view.Running.Yaml)
	}

	if view.Running.Revision != 0 {
		t.Errorf("the running file is revision 0, got %d", view.Running.Revision)
	}
}

func TestAReportWithoutAConfigIsStillAccepted(t *testing.T) {
	t.Parallel()

	server := boothServer(t, &fakeRecords{})

	if got := report(t, server, oneReport, token).StatusCode; got != http.StatusAccepted {
		t.Fatalf("posting a report with no config got %d, want 202", got)
	}

	if view := viewConsole(t, server); view.Rules != nil {
		t.Fatalf("nothing said what it runs, so there is nothing to prefill: %+v", view.Rules)
	}
}

func TestTheConsoleSaysTheRosterCouldNotBeReadRatherThanThatItIsEmpty(t *testing.T) {
	t.Parallel()

	broken := viewConsole(t, boothServer(t, &fakeRecords{broken: true}))
	if broken.Roster != recordsUnreachable {
		t.Errorf("roster = %q, want %q: an outage must not look like a tournament nobody wrote down",
			broken.Roster, recordsUnreachable)
	}

	if len(broken.People) != 0 {
		t.Errorf("people = %+v, want none: there was nothing to read", broken.People)
	}

	records := &fakeRecords{}
	knownPeople(records, "Ada")

	if got := viewConsole(t, boothServer(t, records)).Roster; got != recordsOK {
		t.Errorf("roster = %q, want %q", got, recordsOK)
	}

	if got := viewConsole(t, boothServer(t, nil)).Roster; got != recordsNone {
		t.Errorf("roster = %q, want %q: there is no database on this service at all",
			got, recordsNone)
	}
}
