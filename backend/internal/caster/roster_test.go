package caster

import (
	"encoding/json"
	"net/http"
	"strings"
	"testing"

	"github.com/caesarakalaeii/subnautica-creator-royale/backend/internal/telemetry"
	"github.com/caesarakalaeii/subnautica-creator-royale/backend/internal/tournament"
)

// playing is a pushed match in the given group, which the derived rule is computed against.
func playing(group int) telemetry.Snapshot {
	return telemetry.Snapshot{
		Version: telemetry.Version,
		Match:   &telemetry.Match{Group: group, Generation: 1, State: "Lobby", Phases: 6},
	}
}

// reportOf is a console report naming who is connected and what the mod believes about each.
func reportOf(accepts bool, connected map[string]bool) string {
	rows := make([]string, 0, len(connected))
	for name, inMatch := range connected {
		rows = append(rows, `{"name":"`+name+`","inMatch":`+boolean(inMatch)+`}`)
	}

	return `{"v":1,"at":"2026-10-24T12:00:00.000Z","acceptsCommands":` + boolean(accepts) +
		`,"ran":0,"connected":[` + strings.Join(rows, ",") + `],"verbs":[],"answers":[]}`
}

func boolean(yes bool) string {
	if yes {
		return "true"
	}

	return "false"
}

// rostered is a booth with a bracket, a roster and a match on the wire.
func rostered(t *testing.T, group int, members ...string) (*Server, *fakeRecords) {
	t.Helper()

	records := &fakeRecords{}
	server := boothServer(t, records)
	server.Store().Apply(playing(group))

	if err := records.SetGroups(t.Context(), []tournament.Group{
		{Number: group, Name: "Group A", Members: members, Seats: seatsFor(members)},
	}, nil); err != nil {
		t.Fatalf("set groups: %v", err)
	}

	return server, records
}

func seatsFor(names []string) []tournament.Seat {
	seats := make([]tournament.Seat, 0, len(names))
	for i, name := range names {
		seats = append(seats, tournament.Seat{Person: int64(i + 1), Name: name, Label: name})
	}

	return seats
}

func knownPeople(records *fakeRecords, names ...string) {
	for i, name := range names {
		records.people = append(records.people,
			tournament.Person{ID: int64(i + 1), Name: name, Label: name})
	}
}

func TestTheBoothAsksTheModToGhostEverybodyOutsideThePlayingGroup(t *testing.T) {
	t.Parallel()

	server, records := rostered(t, 1, "Ada", "Bea")
	knownPeople(records, "Ada", "Bea", "Caster")

	// The mod has all three in the match: nobody has been ghosted yet.
	report(t, server, reportOf(true, map[string]bool{"Ada": true, "Bea": true, "Caster": true}), token)

	queued := []string{}
	for _, row := range records.commands {
		queued = append(queued, row.Verb)
	}

	if len(queued) != 1 || queued[0] != "ghost Caster" {
		t.Fatalf("queued = %v, want just the one who is not in the playing group", queued)
	}

	if records.commands[0].By != reconciler {
		t.Errorf("who asked is the rule, and it says so: %+v", records.commands[0])
	}
}

func TestSomebodyInThePlayingGroupIsPutBackIntoIt(t *testing.T) {
	t.Parallel()

	server, records := rostered(t, 1, "Ada", "Bea")
	knownPeople(records, "Ada", "Bea")

	report(t, server, reportOf(true, map[string]bool{"Ada": true, "Bea": false}), token)

	if len(records.commands) != 1 || records.commands[0].Verb != "unghost Bea" {
		t.Fatalf("commands = %+v, want the one who should be playing", records.commands)
	}
}

func TestAModThatAlreadyAgreesIsAskedForNothing(t *testing.T) {
	t.Parallel()

	server, records := rostered(t, 1, "Ada")
	knownPeople(records, "Ada", "Caster")

	report(t, server, reportOf(true, map[string]bool{"Ada": true, "Caster": false}), token)

	if len(records.commands) != 0 {
		t.Fatalf("commands = %+v, want none", records.commands)
	}
}

func TestAnEmptyBracketNeverGhostsTheWholeTournament(t *testing.T) {
	t.Parallel()

	records := &fakeRecords{}
	server := boothServer(t, records)
	server.Store().Apply(playing(1))
	knownPeople(records, "Ada", "Bea")

	report(t, server, reportOf(true, map[string]bool{"Ada": true, "Bea": true}), token)

	if len(records.commands) != 0 {
		t.Fatalf("commands = %+v, want none: an empty group is a bracket nobody filled in",
			records.commands)
	}

	view := viewConsole(t, server)
	if view.Intent.Reconciling {
		t.Errorf("and it says it is not reconciling: %+v", view.Intent)
	}

	if !strings.Contains(view.Intent.Why, "nobody assigned") {
		t.Errorf("and why not, so a console that does nothing is not mysterious: %q", view.Intent.Why)
	}
}

func TestAServerThatRefusesCommandsIsNotQueuedFor(t *testing.T) {
	t.Parallel()

	server, records := rostered(t, 1, "Ada")
	knownPeople(records, "Ada", "Caster")

	report(t, server, reportOf(false, map[string]bool{"Ada": true, "Caster": true}), token)

	if len(records.commands) != 0 {
		t.Fatalf("commands = %+v, want none", records.commands)
	}

	if why := viewConsole(t, server).Intent.Why; !strings.Contains(why, "acceptsCommands") {
		t.Errorf("and the console says which switch it is: %q", why)
	}
}

func TestTheSameReconcileIsNotQueuedTwice(t *testing.T) {
	t.Parallel()

	server, records := rostered(t, 1, "Ada")
	knownPeople(records, "Ada", "Caster")

	unchanged := reportOf(true, map[string]bool{"Ada": true, "Caster": true})

	report(t, server, unchanged, token)
	report(t, server, unchanged, token)
	report(t, server, unchanged, token)

	if len(records.commands) != 1 {
		t.Fatalf("commands = %+v, want exactly one", records.commands)
	}

	// Still shown as pending until the mod applies it.
	if pending := viewConsole(t, server).Intent.Pending; len(pending) != 1 {
		t.Errorf("pending = %v, want the one still owed", pending)
	}
}

func TestAnUnboundJoinerIsSurfacedAndNeverActedOn(t *testing.T) {
	t.Parallel()

	server, records := rostered(t, 1, "Ada")
	knownPeople(records, "Ada")

	report(t, server, reportOf(true, map[string]bool{"Ada": true, "WhoIsThis": true}), token)

	if len(records.commands) != 0 {
		t.Fatalf("commands = %+v, want none: an unbound joiner is a question for a human",
			records.commands)
	}

	view := viewConsole(t, server)
	if len(view.Intent.Unbound) != 1 || view.Intent.Unbound[0] != "WhoIsThis" {
		t.Fatalf("unbound = %v, want the name nobody wrote down", view.Intent.Unbound)
	}
}

func TestAnOverrideBeatsTheDerivedRuleFromEndToEnd(t *testing.T) {
	t.Parallel()

	server, records := rostered(t, 1, "Ada")
	knownPeople(records, "Ada", "Caster")

	recorder := ask(t, server, http.MethodPost, overridesPath,
		`{"person":2,"role":"contestant","note":"wants to play","by":"ops"}`, operatorToken)
	if recorder.Code != http.StatusCreated {
		t.Fatalf("override got %d: %s", recorder.Code, recorder.Body.String())
	}

	report(t, server, reportOf(true, map[string]bool{"Ada": true, "Caster": false}), token)

	if len(records.commands) != 1 || records.commands[0].Verb != "unghost Caster" {
		t.Fatalf("commands = %+v, want the override honoured", records.commands)
	}
}

func TestABoothRestartRederivesTheSameSetRatherThanADifferentOne(t *testing.T) {
	t.Parallel()

	records := &fakeRecords{}
	knownPeople(records, "Ada", "Caster")

	if err := records.SetGroups(t.Context(), []tournament.Group{
		{Number: 1, Name: "Group A", Members: []string{"Ada"}, Seats: seatsFor([]string{"Ada"})},
	}, nil); err != nil {
		t.Fatalf("set groups: %v", err)
	}

	connected := reportOf(true, map[string]bool{"Ada": true, "Caster": true})

	// A restart is a fresh service over the same database.
	first := boothServer(t, records)
	first.Store().Apply(playing(1))
	report(t, first, connected, token)

	before := viewConsole(t, first).Intent

	second := boothServer(t, records)
	second.Store().Apply(playing(1))
	report(t, second, connected, token)

	after := viewConsole(t, second).Intent

	if strings.Join(before.Ghosts, ",") != strings.Join(after.Ghosts, ",") {
		t.Fatalf("a restart re-derives the same set: %v then %v", before.Ghosts, after.Ghosts)
	}
}

func TestTheRosterIsRefusedWithoutTheOperatorToken(t *testing.T) {
	t.Parallel()

	server := boothServer(t, &fakeRecords{})
	body := `{"people":[{"name":"Ada","label":"Ada"}]}`

	for _, path := range []struct {
		method string
		path   string
		body   string
	}{
		{http.MethodPut, peoplePath, body},
		{http.MethodPost, overridesPath, `{"person":1,"role":"ghost","by":"ops"}`},
	} {
		for _, presented := range []string{"", token, "watching", "wrong"} {
			recorder := ask(t, server, path.method, path.path, path.body, presented)

			if recorder.Code != http.StatusUnauthorized {
				t.Errorf("%s %s with %q got %d, want 401",
					path.method, path.path, presented, recorder.Code)
			}
		}
	}
}

func TestARosterIsWrittenAndReadBack(t *testing.T) {
	t.Parallel()

	records := &fakeRecords{}
	server := boothServer(t, records)

	recorder := ask(t, server, http.MethodPut, peoplePath,
		`{"people":[{"name":"AdaPlays","label":"Ada","notes":"group 2"},{"name":"Bea"}]}`,
		operatorToken)

	if recorder.Code != http.StatusOK {
		t.Fatalf("got %d: %s", recorder.Code, recorder.Body.String())
	}

	view := viewConsole(t, server)
	if len(view.People) != 2 {
		t.Fatalf("people = %+v", view.People)
	}

	if view.People[1].Label != "Bea" {
		t.Errorf("a label left blank falls back to the name, so a roster typed in a hurry draws: %+v",
			view.People[1])
	}
}

func TestARosterWithTheSamePersonTwiceIsRefused(t *testing.T) {
	t.Parallel()

	server := boothServer(t, &fakeRecords{})

	for _, body := range []string{
		`{"people":[{"name":"Ada"},{"name":"ada"}]}`,
		`{"people":[{"name":"  "}]}`,
	} {
		recorder := ask(t, server, http.MethodPut, peoplePath, body, operatorToken)

		if recorder.Code != http.StatusBadRequest {
			t.Errorf("%s got %d, want 400", body, recorder.Code)
		}
	}
}

func TestAnOverrideWithNothingUsefulInItIsRefusedWithASentence(t *testing.T) {
	t.Parallel()

	server := boothServer(t, &fakeRecords{})

	for _, body := range []string{
		`{"person":0,"role":"ghost","by":"ops"}`,
		`{"person":1,"role":"wobble","by":"ops"}`,
		`{"person":1,"role":"ghost","by":""}`,
	} {
		recorder := ask(t, server, http.MethodPost, overridesPath, body, operatorToken)

		if recorder.Code != http.StatusBadRequest {
			t.Errorf("%s got %d, want 400", body, recorder.Code)
		}
	}
}

func TestTheConsoleDrawsWhatTheModBelievesBesideWhatTheBoothIntends(t *testing.T) {
	t.Parallel()

	server, records := rostered(t, 1, "Ada")
	knownPeople(records, "Ada", "Caster")

	report(t, server, reportOf(true, map[string]bool{"Ada": true, "Caster": true}), token)

	view := viewConsole(t, server)

	if !view.Intent.Reconciling || view.Intent.Group != 1 {
		t.Fatalf("intent = %+v", view.Intent)
	}

	if len(view.Intent.Ghosts) != 1 || view.Intent.Ghosts[0] != "Caster" {
		t.Errorf("the booth's own view: %+v", view.Intent)
	}

	// The mod's view still disagrees: the command has not run yet.
	for _, present := range view.Report.Connected {
		if present.Name == "Caster" && !present.InMatch {
			t.Errorf("the mod has not applied it yet, and the page should be able to see that")
		}
	}
}

func TestAConsoleWithNoDatabaseSaysSoRatherThanLookingIdle(t *testing.T) {
	t.Parallel()

	server := boothServer(t, nil)
	server.Store().Apply(playing(1))

	report(t, server, reportOf(true, map[string]bool{"Ada": true}), token)

	view := viewConsole(t, server)

	if view.Intent.Reconciling {
		t.Fatalf("intent = %+v", view.Intent)
	}

	if !strings.Contains(view.Intent.Why, "no database") {
		t.Errorf("why = %q", view.Intent.Why)
	}
}

func TestABoothThatHasNeverBeenPushedToKnowsItCannotReconcile(t *testing.T) {
	t.Parallel()

	records := &fakeRecords{}
	server := boothServer(t, records)

	report(t, server, reportOf(true, map[string]bool{"Ada": true}), token)

	if why := viewConsole(t, server).Intent.Why; !strings.Contains(why, "which group is playing") {
		t.Fatalf("why = %q", why)
	}
}

func TestTheConsoleNeverHandsThePageANull(t *testing.T) {
	t.Parallel()

	recorder := ask(t, boothServer(t, &fakeRecords{}), http.MethodGet, consolePath, "", operatorToken)

	var raw map[string]json.RawMessage
	if err := json.Unmarshal(recorder.Body.Bytes(), &raw); err != nil {
		t.Fatalf("console is not JSON: %v", err)
	}

	for _, field := range []string{"commands", "people", "groups", "overrides"} {
		if string(raw[field]) == "null" {
			t.Errorf("%s is null; a page doing .map on it breaks on the first load", field)
		}
	}

	var drawn intent
	if err := json.Unmarshal(raw["intent"], &drawn); err != nil {
		t.Fatalf("intent is not JSON: %v", err)
	}

	if drawn.Ghosts == nil || drawn.Contestants == nil || drawn.Unbound == nil || drawn.Pending == nil {
		t.Errorf("intent = %+v, want empty lists rather than nulls", drawn)
	}
}

func TestAReconcileTheRuleNoLongerWantsIsWithdrawnBeforeItFires(t *testing.T) {
	t.Parallel()

	server, records := rostered(t, 1, "Ada")
	knownPeople(records, "Ada", "Caster")

	// Caster is not in group 1, so the booth queues a ghost that nothing collects.
	report(t, server, reportOf(true, map[string]bool{"Ada": true, "Caster": true}), token)

	if len(records.commands) != 1 || records.commands[0].Verb != "ghost Caster" {
		t.Fatalf("commands = %+v", records.commands)
	}

	if err := records.SetGroups(t.Context(), []tournament.Group{
		{Number: 1, Name: "Group A", Members: []string{"Ada", "Caster"},
			Seats: seatsFor([]string{"Ada", "Caster"})},
	}, nil); err != nil {
		t.Fatalf("set groups: %v", err)
	}

	report(t, server, reportOf(true, map[string]bool{"Ada": true, "Caster": true}), token)

	if records.commands[0].State != tournament.StateExpired {
		t.Fatalf("the stale ghost must not fire: %+v", records.commands[0])
	}
}

func TestAReconcileTheGameServerAlreadyHasIsNotWithdrawn(t *testing.T) {
	t.Parallel()

	server, records := rostered(t, 1, "Ada")
	knownPeople(records, "Ada", "Caster")

	report(t, server, reportOf(true, map[string]bool{"Ada": true, "Caster": true}), token)

	pushWithWatermark(t, server, 0)

	if records.commands[0].State != tournament.StateSent {
		t.Fatalf("commands = %+v", records.commands)
	}

	if err := records.SetGroups(t.Context(), []tournament.Group{
		{Number: 1, Name: "Group A", Members: []string{"Ada", "Caster"},
			Seats: seatsFor([]string{"Ada", "Caster"})},
	}, nil); err != nil {
		t.Fatalf("set groups: %v", err)
	}

	report(t, server, reportOf(true, map[string]bool{"Ada": true, "Caster": true}), token)

	if records.commands[0].State == tournament.StateExpired {
		t.Fatalf("a command the mod may be running must not be marked as given up on: %+v",
			records.commands[0])
	}
}

func TestTheRuleOnlyWithdrawsItsOwnCommands(t *testing.T) {
	t.Parallel()

	server, records := rostered(t, 1, "Ada")
	knownPeople(records, "Ada")

	queue(t, server, "start")

	report(t, server, reportOf(true, map[string]bool{"Ada": true}), token)

	if records.commands[0].State != tournament.StateQueued {
		t.Fatalf("an operator's own command is theirs: %+v", records.commands[0])
	}
}

// An unreadable database renders as `people: []` on the console, so the editor may submit an empty
// or id-less roster back; the tests below cover what the booth does with that.

func TestARosterWriteThatWouldTakeEverybodyOffIsRefused(t *testing.T) {
	t.Parallel()

	records := &fakeRecords{}
	server := boothServer(t, records)
	knownPeople(records, "Ada", "Bea", "Caster")

	recorder := ask(t, server, http.MethodPut, peoplePath, `{"people":[]}`, operatorToken)

	if recorder.Code != http.StatusConflict {
		t.Fatalf("got %d, want 409: an empty roster through this route is a redraw during an outage",
			recorder.Code)
	}

	if len(records.people) != 3 {
		t.Errorf("people = %+v, want all three still there", records.people)
	}

	if got := reason(t, recorder); !strings.Contains(got, "all 3") {
		t.Errorf("the refusal counts them so the operator knows what was about to go: %q", got)
	}
}

func TestAPasteWithTheIdsStrippedOutIsRefusedRatherThanReplacingEverybody(t *testing.T) {
	t.Parallel()

	records := &fakeRecords{}
	server := boothServer(t, records)
	knownPeople(records, "Ada", "Bea")

	// Same names without ids: the store would insert two and delete two, cascading their groups
	// and overrides.
	recorder := ask(t, server, http.MethodPut, peoplePath,
		`{"people":[{"name":"Ada"},{"name":"Bea"}]}`, operatorToken)

	if recorder.Code != http.StatusConflict {
		t.Fatalf("got %d, want 409", recorder.Code)
	}

	if len(records.people) != 2 || records.people[0].ID != 1 || records.people[1].ID != 2 {
		t.Errorf("people = %+v, want the surrogate keys the bracket hangs off untouched",
			records.people)
	}
}

func TestARosterWriteThatDropsSomebodyIsRefusedUntilTheCountIsTyped(t *testing.T) {
	t.Parallel()

	records := &fakeRecords{}
	server := boothServer(t, records)
	knownPeople(records, "Ada", "Bea", "Caster")

	recorder := ask(t, server, http.MethodPut, peoplePath,
		`{"people":[{"id":1,"name":"Ada"},{"id":2,"name":"Bea"}]}`, operatorToken)

	if recorder.Code != http.StatusConflict {
		t.Fatalf("got %d, want 409", recorder.Code)
	}

	if len(records.people) != 3 {
		t.Errorf("people = %+v, want nobody removed", records.people)
	}

	got := reason(t, recorder)
	if !strings.Contains(got, "removes 1") || !strings.Contains(got, "Caster") {
		t.Errorf("the refusal says how many and who, so it can be checked before it is confirmed: %q",
			got)
	}
}

func TestARosterWriteThatConfirmsTheWrongCountIsStillRefused(t *testing.T) {
	t.Parallel()

	records := &fakeRecords{}
	server := boothServer(t, records)
	knownPeople(records, "Ada", "Bea", "Caster")

	// Confirms two removals when the write removes one: the operator's view is stale.
	recorder := ask(t, server, http.MethodPut, peoplePath,
		`{"people":[{"id":1,"name":"Ada"},{"id":2,"name":"Bea"}],"removing":2}`, operatorToken)

	if recorder.Code != http.StatusConflict {
		t.Fatalf("got %d, want 409", recorder.Code)
	}

	if len(records.people) != 3 {
		t.Errorf("people = %+v, want nobody removed", records.people)
	}
}

func TestARosterWriteThatNamesHowManyItRemovesGoesThrough(t *testing.T) {
	t.Parallel()

	records := &fakeRecords{}
	server := boothServer(t, records)
	knownPeople(records, "Ada", "Bea", "Caster")

	recorder := ask(t, server, http.MethodPut, peoplePath,
		`{"people":[{"id":1,"name":"Ada"},{"id":2,"name":"Bea"}],"removing":1}`, operatorToken)

	if recorder.Code != http.StatusOK {
		t.Fatalf("got %d: %s", recorder.Code, recorder.Body.String())
	}

	if len(records.people) != 2 {
		t.Errorf("people = %+v, want the one they confirmed gone", records.people)
	}
}

func TestARosterWriteThatOnlyAddsSomebodyNeedsNoConfirmation(t *testing.T) {
	t.Parallel()

	records := &fakeRecords{}
	server := boothServer(t, records)
	knownPeople(records, "Ada")

	// Adding someone removes nobody and needs no confirmation.
	recorder := ask(t, server, http.MethodPut, peoplePath,
		`{"people":[{"id":1,"name":"Ada"},{"name":"Bea"}]}`, operatorToken)

	if recorder.Code != http.StatusOK {
		t.Fatalf("got %d: %s", recorder.Code, recorder.Body.String())
	}

	if len(records.people) != 2 {
		t.Errorf("people = %+v, want both", records.people)
	}
}

func TestARosterIsNotWrittenOverADatabaseThatCouldNotBeRead(t *testing.T) {
	t.Parallel()

	server := boothServer(t, &fakeRecords{broken: true})

	recorder := ask(t, server, http.MethodPut, peoplePath,
		`{"people":[{"id":1,"name":"Ada"}]}`, operatorToken)

	if recorder.Code != http.StatusServiceUnavailable {
		t.Fatalf("got %d, want 503", recorder.Code)
	}

	if got := reason(t, recorder); !strings.Contains(got, "could not be read") {
		t.Errorf("a write that cannot see what it replaces says so rather than attempting it: %q", got)
	}
}
