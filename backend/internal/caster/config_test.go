package caster

import (
	"encoding/json"
	"net/http"
	"net/http/httptest"
	"strconv"
	"strings"
	"testing"

	"github.com/caesarakalaeii/subnautica-creator-royale/backend/internal/tournament"
)

const someYaml = `{"yaml":"match:\n  cycleLength: 45s\n","by":"ops","note":"a shorter cycle"}`

func writeRules(t *testing.T, server *Server, body string) rulesResponse {
	t.Helper()

	recorder := ask(t, server, http.MethodPut, rulesPath, body, operatorToken)
	if recorder.Code != http.StatusAccepted {
		t.Fatalf("writing the rules got %d, want 202: %s", recorder.Code, recorder.Body.String())
	}

	var written rulesResponse
	if err := json.Unmarshal(recorder.Body.Bytes(), &written); err != nil {
		t.Fatalf("reply was not JSON: %v (%q)", err, recorder.Body.String())
	}

	return written
}

func pushRunning(t *testing.T, server *Server, revision int64) ingestResponse {
	t.Helper()

	request := httptest.NewRequest(http.MethodPost, ingestPath,
		strings.NewReader(string(examplePayload(t))))
	request.Header.Set("Authorization", "Bearer "+token)
	request.Header.Set(commandWatermarkHeader, "0")
	request.Header.Set(rulesRevisionHeader, strconv.FormatInt(revision, 10))

	recorder := httptest.NewRecorder()
	server.Handler().ServeHTTP(recorder, request)

	if recorder.Code != http.StatusAccepted {
		t.Fatalf("push got %d, want 202: %s", recorder.Code, recorder.Body.String())
	}

	return decode(t, recorder)
}

func TestTheRulesAreRefusedWithoutTheOperatorToken(t *testing.T) {
	t.Parallel()

	server := boothServer(t, &fakeRecords{})

	for _, presented := range []string{"", token, "watching", "wrong"} {
		recorder := ask(t, server, http.MethodPut, rulesPath, someYaml, presented)

		if recorder.Code != http.StatusUnauthorized {
			t.Errorf("token %q got %d, want 401", presented, recorder.Code)
		}
	}
}

func TestARevisionIsKeptAndRidesBackOnAPush(t *testing.T) {
	t.Parallel()

	records := &fakeRecords{}
	server := boothServer(t, records)

	written := writeRules(t, server, someYaml)

	if !written.Written || written.Revision == 0 {
		t.Fatalf("written = %+v", written)
	}

	reply := pushRunning(t, server, 0)

	if reply.Rules == nil {
		t.Fatalf("the push should carry the revision the server is not running")
	}

	if reply.Rules.Revision != written.Revision {
		t.Errorf("revision = %d, want %d", reply.Rules.Revision, written.Revision)
	}

	if !strings.Contains(reply.Rules.Yaml, "cycleLength: 45s") {
		t.Errorf("the document travels as it was written: %q", reply.Rules.Yaml)
	}

	if reply.Rules.By != "ops" {
		t.Errorf("who wrote it travels to the game server's audit log: %+v", reply.Rules)
	}
}

func TestAServerAlreadyRunningTheNewestIsSentNothing(t *testing.T) {
	t.Parallel()

	server := boothServer(t, &fakeRecords{})
	written := writeRules(t, server, someYaml)

	if reply := pushRunning(t, server, written.Revision); reply.Rules != nil {
		t.Fatalf("rules = %+v, want none", reply.Rules)
	}
}

func TestAServerThatDoesNotSpeakRulesIsSentNone(t *testing.T) {
	t.Parallel()

	server := boothServer(t, &fakeRecords{})
	writeRules(t, server, someYaml)

	reply := decode(t, push(t, server, examplePayload(t), token))

	if reply.Rules != nil {
		t.Fatalf("no header means no rules: %+v", reply.Rules)
	}
}

func TestAReplyWithNoRulesInItDoesNotCarryTheField(t *testing.T) {
	t.Parallel()

	recorder := push(t, boothServer(t, &fakeRecords{}), examplePayload(t), token)

	if strings.Contains(recorder.Body.String(), `"rules"`) {
		t.Fatalf("reply carries a rules field with nothing in it: %q", recorder.Body.String())
	}
}

func TestTheBoothKeepsAConfigItCannotUnderstand(t *testing.T) {
	t.Parallel()

	written := writeRules(t, boothServer(t, &fakeRecords{}),
		`{"yaml":"this is not a tournament config at all","by":"ops"}`)

	if !written.Written {
		t.Fatalf("the game server refuses a config, not the booth: %+v", written)
	}
}

func TestTheTwoThingsTheBoothDoesCheckAreNotAboutYaml(t *testing.T) {
	t.Parallel()

	server := boothServer(t, &fakeRecords{})

	for _, body := range []string{
		// Empty.
		`{"yaml":"","by":"ops"}`,
		`{"yaml":"   ","by":"ops"}`,
		// A tab: the broadcast to clients is tab-delimited.
		`{"yaml":"match:\n\tcycleLength: 45s","by":"ops"}`,
		// No author.
		`{"yaml":"match:","by":""}`,
		`not json`,
	} {
		recorder := ask(t, server, http.MethodPut, rulesPath, body, operatorToken)

		if recorder.Code != http.StatusBadRequest {
			t.Errorf("%s got %d, want 400", body, recorder.Code)
		}
	}
}

func TestTheRulesAreRefusedWithNoDatabaseRatherThanKeptInMemory(t *testing.T) {
	t.Parallel()

	recorder := ask(t, boothServer(t, nil), http.MethodPut, rulesPath, someYaml, operatorToken)

	if recorder.Code != http.StatusServiceUnavailable {
		t.Fatalf("got %d, want 503: %s", recorder.Code, recorder.Body.String())
	}
}

func TestWhatTheGameServerSaysAboutARevisionIsFiledAgainstIt(t *testing.T) {
	t.Parallel()

	records := &fakeRecords{}
	server := boothServer(t, records)
	written := writeRules(t, server, someYaml)

	report(t, server, rulesReport(written.Revision,
		`"adopted":false,"final":false,"message":"written to disk; it will be taken up at the next lobby.","problems":[]`),
		token)

	if state := viewConsole(t, server).Rules.State; state != tournament.ConfigWaiting {
		t.Fatalf("state = %q, want %q: an operator must not read this as refused",
			state, tournament.ConfigWaiting)
	}

	report(t, server, rulesReport(written.Revision,
		`"adopted":true,"final":true,"message":"the rules are revision 1","problems":[]`), token)

	view := viewConsole(t, server)
	if view.Rules.State != tournament.ConfigAdopted {
		t.Fatalf("state = %q, want %q", view.Rules.State, tournament.ConfigAdopted)
	}

	if view.Rules.Answer != "the rules are revision 1" {
		t.Errorf("the game server's own sentence, verbatim: %q", view.Rules.Answer)
	}
}

func TestTheLoadersOwnFieldPathsReachTheConsole(t *testing.T) {
	t.Parallel()

	records := &fakeRecords{}
	server := boothServer(t, records)
	written := writeRules(t, server, `{"yaml":"nonsense","by":"ops"}`)

	report(t, server, rulesReport(written.Revision,
		`"adopted":false,"final":true,"message":"that config has 2 problem(s)","problems":`+
			`[{"problem":"zone.phases[1].radius: must be a number of metres"},`+
			`{"problem":"match.cycleLength: is required"}]`), token)

	view := viewConsole(t, server)

	if view.Rules.State != tournament.ConfigRefused {
		t.Fatalf("state = %q, want %q", view.Rules.State, tournament.ConfigRefused)
	}

	if len(view.Rules.Problems) != 2 ||
		!strings.HasPrefix(view.Rules.Problems[0], "zone.phases[1].radius") {
		t.Fatalf("problems = %v, want the loader's own field paths", view.Rules.Problems)
	}
}

func TestARefusedRevisionIsNotSentAgain(t *testing.T) {
	t.Parallel()

	records := &fakeRecords{}
	server := boothServer(t, records)
	written := writeRules(t, server, someYaml)

	report(t, server, rulesReport(written.Revision,
		`"adopted":false,"final":true,"message":"no","problems":[]`), token)

	if reply := pushRunning(t, server, 0); reply.Rules != nil {
		t.Fatalf("rules = %+v, want none", reply.Rules)
	}
}

func TestADatabaseThatHasGoneAwayDoesNotCostThePushItsRules(t *testing.T) {
	t.Parallel()

	reply := pushRunning(t, boothServer(t, &fakeRecords{broken: true}), 0)

	if !reply.Accepted {
		t.Fatalf("the match still goes on the wire: %+v", reply)
	}

	if reply.Rules != nil {
		t.Fatalf("and there are no rules to run: %+v", reply.Rules)
	}
}

func TestGettingTheRulesPathIsNotAllowed(t *testing.T) {
	t.Parallel()

	recorder := ask(t, boothServer(t, &fakeRecords{}), http.MethodGet, rulesPath, "", operatorToken)

	if recorder.Code != http.StatusMethodNotAllowed {
		t.Fatalf("got %d, want 405", recorder.Code)
	}
}

// rulesReport is a console report saying one thing about one revision.
func rulesReport(revision int64, outcome string) string {
	return `{"v":1,"at":"2026-10-24T12:00:00.000Z","acceptsCommands":true,"ran":0,` +
		`"rulesRevision":0,"connected":[],"verbs":[],"answers":[],` +
		`"rules":[{"revision":` + strconv.FormatInt(revision, 10) + `,` + outcome + `}]}`
}

func TestARefusedRevisionDoesNotFallBackToAnAbandonedOne(t *testing.T) {
	t.Parallel()

	server := boothServer(t, &fakeRecords{})

	writeRules(t, server, `{"yaml":"match:\n  cycleLength: 10s\n","by":"ops","note":"one"}`)
	writeRules(t, server, `{"yaml":"match:\n  cycleLength: 20s\n","by":"ops","note":"two"}`)
	last := writeRules(t, server, `{"yaml":"nonsense","by":"ops","note":"three"}`)

	report(t, server, rulesReport(last.Revision,
		`"adopted":false,"final":true,"message":"2 problems","problems":[{"problem":"zone.opening: is required"}]`),
		token)

	if reply := pushRunning(t, server, 0); reply.Rules != nil {
		t.Fatalf("rules = %+v, want none: the newest revision is the intent, and it was refused",
			reply.Rules)
	}
}

// readRevision fetches one rules revision whole.
func readRevision(t *testing.T, server *Server, revision string) *httptest.ResponseRecorder {
	t.Helper()

	return ask(t, server, http.MethodGet, rulesPath+"/"+revision, "", operatorToken)
}

func TestAPreviousRevisionIsHandedBackWhole(t *testing.T) {
	t.Parallel()

	server := boothServer(t, &fakeRecords{})

	good := writeRules(t, server, `{"yaml":"match:\n  cycleLength: 60s\n","by":"ops","note":"the good one"}`)
	writeRules(t, server, `{"yaml":"match:\n  cycleLength: 5s\n","by":"ops","note":"a mistake"}`)

	recorder := readRevision(t, server, strconv.FormatInt(good.Revision, 10))
	if recorder.Code != http.StatusOK {
		t.Fatalf("got %d, want 200: %s", recorder.Code, recorder.Body.String())
	}

	var back tournament.Config
	if err := json.Unmarshal(recorder.Body.Bytes(), &back); err != nil {
		t.Fatalf("reply is not JSON: %v (%q)", err, recorder.Body.String())
	}

	if back.Revision != good.Revision {
		t.Errorf("revision = %d, want %d", back.Revision, good.Revision)
	}

	if back.Yaml != "match:\n  cycleLength: 60s\n" {
		t.Errorf("the document is not the one that was written: %q", back.Yaml)
	}

	if back.Note != "the good one" {
		t.Errorf("note = %q, want the one it was written with", back.Note)
	}
}

func TestARevisionThisBoothNeverKeptIsNotFound(t *testing.T) {
	t.Parallel()

	server := boothServer(t, &fakeRecords{})
	writeRules(t, server, someYaml)

	recorder := readRevision(t, server, "99")
	if recorder.Code != http.StatusNotFound {
		t.Fatalf("got %d, want 404: %s", recorder.Code, recorder.Body.String())
	}

	if got := reason(t, recorder); !strings.Contains(got, "99") {
		t.Errorf("reason = %q, want the revision in it", got)
	}
}

func TestReadingARevisionNeedsTheOperatorToken(t *testing.T) {
	t.Parallel()

	server := boothServer(t, &fakeRecords{})
	writeRules(t, server, someYaml)

	for _, presented := range []string{"", token, "watching", "wrong"} {
		recorder := ask(t, server, http.MethodGet, rulesPath+"/1", "", presented)

		if recorder.Code != http.StatusUnauthorized {
			t.Errorf("token %q got %d, want 401", presented, recorder.Code)
		}
	}
}

func TestARevisionThatIsNotANumberIsRefusedWithAReason(t *testing.T) {
	t.Parallel()

	server := boothServer(t, &fakeRecords{})
	writeRules(t, server, someYaml)

	for _, asked := range []string{"latest", "-1", "1.5", "one"} {
		if code := readRevision(t, server, asked).Code; code != http.StatusBadRequest {
			t.Errorf("%q got %d, want 400", asked, code)
		}
	}

	// Revision 0 means the game server's own file, which the booth never stored.
	recorder := readRevision(t, server, "0")
	if recorder.Code != http.StatusBadRequest {
		t.Fatalf("revision 0 got %d, want 400: %s", recorder.Code, recorder.Body.String())
	}

	if got := reason(t, recorder); !strings.Contains(got, "the game server") {
		t.Errorf("reason = %q, want it to say what revision 0 is", got)
	}
}

func TestReadingARevisionSaysSoWhenThereIsNoDatabase(t *testing.T) {
	t.Parallel()

	none := readRevision(t, boothServer(t, nil), "1")
	if none.Code != http.StatusServiceUnavailable {
		t.Fatalf("no database got %d, want 503: %s", none.Code, none.Body.String())
	}

	if got := reason(t, none); !strings.Contains(got, "no database") {
		t.Errorf("reason = %q, want the no-database sentence", got)
	}

	broken := readRevision(t, boothServer(t, &fakeRecords{broken: true}), "1")
	if broken.Code != http.StatusServiceUnavailable {
		t.Fatalf("unreachable database got %d, want 503: %s", broken.Code, broken.Body.String())
	}

	if got := reason(t, broken); got == reason(t, none) {
		t.Errorf("both say %q; an operator cannot tell an incomplete deployment from a broken one", got)
	}
}
