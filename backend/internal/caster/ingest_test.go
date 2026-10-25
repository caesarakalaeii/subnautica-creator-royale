package caster

import (
	"encoding/json"
	"io"
	"log/slog"
	"net/http"
	"net/http/httptest"
	"os"
	"path/filepath"
	"strings"
	"testing"

	"github.com/caesarakalaeii/subnautica-creator-royale/backend/internal/config"
)

const token = "a-shared-secret"

// oneBooth is a single-booth configuration: the reserved tournament booth, which the bare paths
// address, holding these tokens.
func oneBooth(ingest, view, operator string) []config.Booth {
	return []config.Booth{{
		ID:            config.TournamentBooth,
		IngestToken:   ingest,
		ViewToken:     view,
		OperatorToken: operator,
	}}
}

// examplePayload is the committed telemetry contract example, the same bytes the mod's encoder is
// tested against, so a wire change fails here too.
func examplePayload(t *testing.T) []byte {
	t.Helper()

	payload, err := os.ReadFile(filepath.Clean("../../../docs/telemetry/example.json"))
	if err != nil {
		t.Fatalf("reading the contract's example payload: %v", err)
	}

	return payload
}

func newTestServer(t *testing.T, ingestToken string) *Server {
	t.Helper()

	cfg := config.Config{Port: 8080, LogLevel: slog.LevelError, LogFormat: config.LogFormatJSON,
		Booths: oneBooth(ingestToken, "", "")}

	return NewServer(cfg, slog.New(slog.NewTextHandler(io.Discard, nil)))
}

func push(t *testing.T, server *Server, body []byte, bearer string) *httptest.ResponseRecorder {
	t.Helper()

	request := httptest.NewRequest(http.MethodPost, ingestPath, strings.NewReader(string(body)))
	if bearer != "" {
		request.Header.Set("Authorization", "Bearer "+bearer)
	}

	recorder := httptest.NewRecorder()
	server.Handler().ServeHTTP(recorder, request)

	return recorder
}

func decode(t *testing.T, recorder *httptest.ResponseRecorder) ingestResponse {
	t.Helper()

	var body ingestResponse
	if err := json.Unmarshal(recorder.Body.Bytes(), &body); err != nil {
		t.Fatalf("reply was not JSON: %v (%q)", err, recorder.Body.String())
	}

	return body
}

func TestAGoodPushIsAcceptedAndStored(t *testing.T) {
	server := newTestServer(t, token)

	recorder := push(t, server, examplePayload(t), token)

	if recorder.Code != http.StatusAccepted {
		t.Fatalf("status = %d, want 202: %s", recorder.Code, recorder.Body.String())
	}

	body := decode(t, recorder)
	if !body.Accepted || body.NewEvents != 3 || body.LastSequence != 43 {
		t.Errorf("reply = %+v, want accepted with 3 new events up to sequence 43", body)
	}

	// Pushed again, the same payload adds nothing and still reports the count.
	repeat := decode(t, push(t, server, examplePayload(t), token))
	if !repeat.Accepted || repeat.NewEvents != 0 || repeat.LastSequence != 43 {
		t.Errorf("second reply = %+v, want accepted with 0 new events still at 43", repeat)
	}

	if !strings.Contains(recorder.Body.String(), `"newEvents"`) {
		t.Error("the reply omitted newEvents; zero is the answer a pump most needs to see")
	}

	snapshot, ok := server.Store().Latest()
	if !ok {
		t.Fatal("the store has no state after a good push")
	}

	if snapshot.Match == nil || snapshot.Match.Group != 2 {
		t.Errorf("stored match = %+v, want group 2", snapshot.Match)
	}
}

func TestAPushWithoutTheTokenIsRefused(t *testing.T) {
	server := newTestServer(t, token)

	for _, bearer := range []string{"", "wrong", token + "x", strings.ToUpper(token)} {
		recorder := push(t, server, examplePayload(t), bearer)
		if recorder.Code != http.StatusUnauthorized {
			t.Errorf("bearer %q: status = %d, want 401", bearer, recorder.Code)
		}
	}

	if _, ok := server.Store().Latest(); ok {
		t.Error("a refused push reached the store")
	}
}

func TestIngestWithNoTokenConfiguredIsUnavailableRatherThanUnauthorised(t *testing.T) {
	server := newTestServer(t, "")

	recorder := push(t, server, examplePayload(t), "anything")

	if recorder.Code != http.StatusServiceUnavailable {
		t.Fatalf("status = %d, want 503", recorder.Code)
	}

	if body := decode(t, recorder); body.Accepted || !strings.Contains(body.Error, "not configured") {
		t.Errorf("reply = %+v, want a refusal that says it is not configured", body)
	}
}

func TestAPayloadFromAnotherBuildIsRefused(t *testing.T) {
	server := newTestServer(t, token)

	recorder := push(t, server,
		[]byte(`{"v":999,"at":"2026-10-24T12:00:00.000Z","match":null,"zone":null,"players":[],"events":[]}`),
		token)

	if recorder.Code != http.StatusBadRequest {
		t.Fatalf("status = %d, want 400", recorder.Code)
	}

	if body := decode(t, recorder); !strings.Contains(body.Error, "version") {
		t.Errorf("error = %q, want it to name the version mismatch", body.Error)
	}
}

func TestRubbishIsRefusedRatherThanStored(t *testing.T) {
	server := newTestServer(t, token)

	recorder := push(t, server, []byte("not json"), token)

	if recorder.Code != http.StatusBadRequest {
		t.Errorf("status = %d, want 400", recorder.Code)
	}

	if _, ok := server.Store().Latest(); ok {
		t.Error("unparseable rubbish reached the store")
	}
}

func TestAnOversizedPushIsRefusedRatherThanRead(t *testing.T) {
	server := newTestServer(t, token)

	recorder := push(t, server, []byte(strings.Repeat("x", maxPushBytes+1)), token)

	if recorder.Code != http.StatusRequestEntityTooLarge {
		t.Errorf("status = %d, want 413", recorder.Code)
	}
}

func TestGettingTheIngestPathIsNotAllowed(t *testing.T) {
	server := newTestServer(t, token)

	request := httptest.NewRequest(http.MethodGet, ingestPath, nil)
	recorder := httptest.NewRecorder()
	server.Handler().ServeHTTP(recorder, request)

	if recorder.Code != http.StatusMethodNotAllowed {
		t.Errorf("status = %d, want 405", recorder.Code)
	}
}

func TestHealthStillWorksWithIngestSwitchedOff(t *testing.T) {
	server := newTestServer(t, "")

	request := httptest.NewRequest(http.MethodGet, healthPath, nil)
	recorder := httptest.NewRecorder()
	server.Handler().ServeHTTP(recorder, request)

	if recorder.Code != http.StatusOK {
		t.Errorf("status = %d, want the container to stay probeable with no token", recorder.Code)
	}
}

// limitedServer is the service with a rate limit on. The burst is two seconds' worth, so a rate of
// 1 admits two pushes and refuses the third.
func limitedServer(t *testing.T, rate float64) *Server {
	t.Helper()

	cfg := config.Config{Port: 8080, LogLevel: slog.LevelError, LogFormat: config.LogFormatJSON,
		Booths: oneBooth(token, "", ""), IngestRate: rate}

	return NewServer(cfg, slog.New(slog.NewTextHandler(io.Discard, nil)))
}

func TestPushesBeyondTheRateAreRefusedWithSomethingUseful(t *testing.T) {
	server := limitedServer(t, 1)
	payload := examplePayload(t)

	for push := 1; push <= 2; push++ {
		if recorder := pushAuthorised(t, server, payload); recorder.Code != http.StatusAccepted {
			t.Fatalf("push %d = %d, want the burst of 2 accepted", push, recorder.Code)
		}
	}

	recorder := pushAuthorised(t, server, payload)
	if recorder.Code != http.StatusTooManyRequests {
		t.Fatalf("the third push = %d, want 429", recorder.Code)
	}

	if retry := recorder.Header().Get("Retry-After"); retry != "1" {
		t.Errorf("Retry-After = %q, want %q so a pump backs off rather than guessing", retry, "1")
	}

	body := decode(t, recorder)
	if body.Accepted {
		t.Error("a refused push said it was accepted")
	}
	if body.Error == "" {
		t.Error("a refused push said nothing about why")
	}

	// The cursor still comes back, so the pump resends from the right place.
	if body.LastSequence != server.Store().LastSequence() {
		t.Errorf("LastSequence = %d, want the store's %d", body.LastSequence, server.Store().LastSequence())
	}
}

func TestARefusedPushChangesNothing(t *testing.T) {
	server := limitedServer(t, 1)
	payload := examplePayload(t)

	pushAuthorised(t, server, payload)
	pushAuthorised(t, server, payload)
	before := server.Store().LastSequence()

	if recorder := pushAuthorised(t, server, payload); recorder.Code != http.StatusTooManyRequests {
		t.Fatalf("the third push = %d, want 429", recorder.Code)
	}

	if after := server.Store().LastSequence(); after != before {
		t.Errorf("LastSequence moved from %d to %d on a refused push", before, after)
	}
}

func TestAFloodOfBadTokensDoesNotStarveTheRealPump(t *testing.T) {
	server := limitedServer(t, 1)

	for attempt := 0; attempt < 200; attempt++ {
		recorder := push(t, server, examplePayload(t), "not-the-token")
		if recorder.Code != http.StatusUnauthorized {
			t.Fatalf("bad-token attempt %d = %d, want 401 every time", attempt, recorder.Code)
		}
	}

	if recorder := pushAuthorised(t, server, examplePayload(t)); recorder.Code != http.StatusAccepted {
		t.Errorf("the real pump got %d after a flood of bad tokens, want 202", recorder.Code)
	}
}

// TestEveryWrongTokenGetsTheSameAnswer: a 429 on some wrong tokens would tell a guesser something;
// only the log line is rationed.
func TestEveryWrongTokenGetsTheSameAnswer(t *testing.T) {
	server := limitedServer(t, 1)

	for attempt := 0; attempt < 50; attempt++ {
		recorder := push(t, server, examplePayload(t), "not-the-token")

		if recorder.Code != http.StatusUnauthorized {
			t.Fatalf("attempt %d = %d, want 401", attempt, recorder.Code)
		}
		if recorder.Header().Get("Retry-After") != "" {
			t.Fatalf("attempt %d carried a Retry-After, which says the guess was throttled", attempt)
		}
	}
}

func TestTheDefaultRateIsFarAboveWhatTheModDoes(t *testing.T) {
	cfg, err := config.Load(func(string) string { return "" })
	if err != nil {
		t.Fatalf("loading defaults: %v", err)
	}

	server := limitedServer(t, cfg.IngestRate)
	payload := examplePayload(t)

	// Twice the burst: forty pushes, far more than the mod's one a second.
	for push := 0; push < 40; push++ {
		if recorder := pushAuthorised(t, server, payload); recorder.Code != http.StatusAccepted {
			t.Fatalf("push %d = %d at the default rate, want it accepted", push, recorder.Code)
		}
	}
}

// pushAuthorised is push with the token the server was built with.
func pushAuthorised(t *testing.T, server *Server, body []byte) *httptest.ResponseRecorder {
	t.Helper()

	return push(t, server, body, token)
}

func TestRetryAfterIsHowLongATokenActuallyTakes(t *testing.T) {
	t.Parallel()

	for rate, want := range map[float64]string{
		20:  "1",
		1:   "1",
		0.5: "2",
		0.4: "3",
		0:   "1",
		-1:  "1",
	} {
		if got := retryAfter(rate); got != want {
			t.Errorf("retryAfter(%v) = %q, want %q", rate, got, want)
		}
	}
}
