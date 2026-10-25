package caster

import (
	"io"
	"log/slog"
	"net/http"
	"net/http/httptest"
	"strconv"
	"strings"
	"testing"
	"time"

	"github.com/caesarakalaeii/subnautica-creator-royale/backend/internal/config"
)

// creationLimitedServer is a server that allows booth creations from one source at `perSecond`,
// against a hand-advanced clock so the test does not depend on machine load.
func creationLimitedServer(t *testing.T, tick *clock, perSecond float64) *Server {
	t.Helper()

	cfg := config.Config{Port: 8080, LogLevel: slog.LevelError, LogFormat: config.LogFormatJSON,
		BoothRate: perSecond,
		Booths:    oneBooth("tournament-push", "tournament-view", "tournament-write")}

	server := NewServer(cfg, slog.New(slog.NewTextHandler(io.Discard, nil))).WithBooths(newFakeRegistry())
	server.now = tick.now

	return server
}

// askFrom sends one request from the given source address, which keys the creation bucket.
func askFrom(t *testing.T, server *Server, source, method, path string) *httptest.ResponseRecorder {
	t.Helper()

	request := httptest.NewRequest(method, path, nil)
	request.RemoteAddr = source

	recorder := httptest.NewRecorder()
	server.Handler().ServeHTTP(recorder, request)

	return recorder
}

func TestAFloodOfCreationsFromOneSourceIsRefused(t *testing.T) {
	t.Parallel()

	tick := newClock()
	server := creationLimitedServer(t, tick, 1)

	allowed, refused := 0, 0

	for range 100 {
		if askFrom(t, server, "198.51.100.7:41000", http.MethodPost, boothsPath).Code == http.StatusCreated {
			allowed++
		} else {
			refused++
		}
	}

	if refused == 0 {
		t.Fatalf("all %d creations from one source in one instant were allowed", allowed)
	}

	if allowed == 0 {
		t.Error("no creation from a source that had asked for none was allowed")
	}
}

func TestAFloodedCreationIsToldItIsBeingRateLimited(t *testing.T) {
	t.Parallel()

	tick := newClock()
	server := creationLimitedServer(t, tick, 1)

	var refused *httptest.ResponseRecorder

	for range 100 {
		if got := askFrom(t, server, "198.51.100.7:41000", http.MethodPost, boothsPath); got.Code != http.StatusCreated {
			refused = got
			break
		}
	}

	if refused == nil {
		t.Fatal("nothing was refused")
	}

	if refused.Code != http.StatusTooManyRequests {
		t.Errorf("status = %d, want 429", refused.Code)
	}

	if after := refused.Header().Get("Retry-After"); after == "" {
		t.Error("no Retry-After on a rate-limited creation, so a client is not told when to come back")
	}

	if said := reason(t, refused); !strings.Contains(said, "too many") {
		t.Errorf("the refusal says %q, want it to say the caller is asking too often", said)
	}
}

func TestAFloodOfCreationsDoesNotDisturbARunningBoothsIngestOrItsClients(t *testing.T) {
	t.Parallel()

	tick := newClock()
	server := creationLimitedServer(t, tick, 1)

	made := create(t, server)

	for range 200 {
		askFrom(t, server, "198.51.100.7:41000", http.MethodPost, boothsPath)
	}

	for sequence := 1; sequence <= 5; sequence++ {
		if code := pushTo(t, server, made.ID, snapshotFor("Ada", sequence), made.Ingest).Code; code != http.StatusAccepted {
			t.Fatalf("push %d during a creation flood: status = %d, want 202", sequence, code)
		}
	}

	// A live client, via a route that works over httptest without a WebSocket upgrade.
	if code := ask(t, server, http.MethodGet, boothPath(made.ID, bracketPath), "", made.View).Code; code != http.StatusOK {
		t.Errorf("reading a running booth during a creation flood: status = %d, want 200", code)
	}
}

func TestOneSourcesFloodDoesNotRefuseAnother(t *testing.T) {
	t.Parallel()

	tick := newClock()
	server := creationLimitedServer(t, tick, 1)

	for range 200 {
		askFrom(t, server, "198.51.100.7:41000", http.MethodPost, boothsPath)
	}

	if code := askFrom(t, server, "203.0.113.9:52000", http.MethodPost, boothsPath).Code; code != http.StatusCreated {
		t.Errorf("a first request from a second source during another's flood: status = %d, want 201", code)
	}
}

func TestASourceThatWaitsMayCreateAgain(t *testing.T) {
	t.Parallel()

	tick := newClock()
	server := creationLimitedServer(t, tick, 1)

	const source = "198.51.100.7:41000"

	for range 200 {
		askFrom(t, server, source, http.MethodPost, boothsPath)
	}

	if code := askFrom(t, server, source, http.MethodPost, boothsPath).Code; code != http.StatusTooManyRequests {
		t.Fatalf("still flooding: status = %d, want 429", code)
	}

	tick.advance(time.Minute)

	if code := askFrom(t, server, source, http.MethodPost, boothsPath).Code; code != http.StatusCreated {
		t.Errorf("a minute after a flood at one a second: status = %d, want 201", code)
	}
}

func TestTheSourceIsTheAddressWithoutThePort(t *testing.T) {
	t.Parallel()

	tick := newClock()
	server := creationLimitedServer(t, tick, 1)

	refused := false

	// Keying on `host:port` would give every request from one machine a fresh bucket.
	for port := range 100 {
		source := "198.51.100.7:" + strconv.Itoa(40000+port)
		if askFrom(t, server, source, http.MethodPost, boothsPath).Code == http.StatusTooManyRequests {
			refused = true
			break
		}
	}

	if !refused {
		t.Error("a hundred requests from one address on a hundred ports were all allowed")
	}
}

func TestTheSourceComesFromTheConfiguredForwardedHeaderWhenThereIsOne(t *testing.T) {
	t.Parallel()

	tick := newClock()

	cfg := config.Config{Port: 8080, LogLevel: slog.LevelError, LogFormat: config.LogFormatJSON,
		BoothRate: 1, ClientIPHeader: "X-Forwarded-For",
		Booths: oneBooth("tournament-push", "tournament-view", "tournament-write")}

	server := NewServer(cfg, slog.New(slog.NewTextHandler(io.Discard, nil))).WithBooths(newFakeRegistry())
	server.now = tick.now

	flood := func(forwarded string) int {
		request := httptest.NewRequest(http.MethodPost, boothsPath, nil)
		request.RemoteAddr = "10.0.0.1:9999"
		request.Header.Set("X-Forwarded-For", forwarded)

		recorder := httptest.NewRecorder()
		server.Handler().ServeHTTP(recorder, request)

		return recorder.Code
	}

	for range 200 {
		flood("198.51.100.7")
	}

	// Same RemoteAddr, so a limiter keyed on it would refuse this too.
	if code := flood("203.0.113.9"); code != http.StatusCreated {
		t.Errorf("a second player behind the same proxy: status = %d, want 201", code)
	}
}

func TestAForwardedHeaderIsReadFromItsRightmostEntry(t *testing.T) {
	t.Parallel()

	tick := newClock()

	cfg := config.Config{Port: 8080, LogLevel: slog.LevelError, LogFormat: config.LogFormatJSON,
		BoothRate: 1, ClientIPHeader: "X-Forwarded-For",
		Booths: oneBooth("tournament-push", "tournament-view", "tournament-write")}

	server := NewServer(cfg, slog.New(slog.NewTextHandler(io.Discard, nil))).WithBooths(newFakeRegistry())
	server.now = tick.now

	claim := func(forwarded string) int {
		request := httptest.NewRequest(http.MethodPost, boothsPath, nil)
		request.RemoteAddr = "10.0.0.1:9999"
		request.Header.Set("X-Forwarded-For", forwarded)

		recorder := httptest.NewRecorder()
		server.Handler().ServeHTTP(recorder, request)

		return recorder.Code
	}

	// One flooder, varying the caller-controlled leftmost entry on every request; only the
	// rightmost entry was written by the trusted proxy.
	refused := false

	for attempt := range 200 {
		if claim("10.9.9."+strconv.Itoa(attempt%250+1)+", 198.51.100.7") == http.StatusTooManyRequests {
			refused = true
			break
		}
	}

	if !refused {
		t.Error("a flood that rewrote the leftmost forwarded entry was never refused")
	}
}

func TestABoothIdIsDrawnFromASpaceTooLargeToEnumerate(t *testing.T) {
	t.Parallel()

	// Lower-case base32, which `config.validBoothID` accepts.
	const alphabet = "abcdefghijklmnopqrstuvwxyz234567"

	// Five bits per character; 2^48 ids is far too many to enumerate.
	const wantBits = 48

	id, err := newBoothID()
	if err != nil {
		t.Fatalf("minting an id: %v", err)
	}

	if bits := len(id) * 5; bits < wantBits {
		t.Errorf("an id is %d characters, which is %d bits of space; want at least %d",
			len(id), bits, wantBits)
	}

	for _, character := range id {
		if !strings.ContainsRune(alphabet, character) {
			t.Errorf("id %q has %q in it, which is not in the alphabet %q", id, character, alphabet)
		}
	}

	const readableLength = 16

	if len(id) > readableLength {
		t.Errorf("an id is %d characters, which is more than the %d somebody can read out loud",
			len(id), readableLength)
	}
}

func TestTwoBoothIdsAreDifferent(t *testing.T) {
	t.Parallel()

	// Enough to catch a generator with only a few thousand distinct outputs.
	const drawn = 2000

	seen := make(map[string]bool, drawn)

	for range drawn {
		id, err := newBoothID()
		if err != nil {
			t.Fatalf("minting an id: %v", err)
		}

		if seen[id] {
			t.Fatalf("id %q was drawn twice in %d", id, drawn)
		}

		seen[id] = true
	}
}

func TestARateOfZeroIsNoCreationLimitAtAll(t *testing.T) {
	t.Parallel()

	tick := newClock()
	server := creationLimitedServer(t, tick, 0)

	for made := 1; made <= 50; made++ {
		if code := askFrom(t, server, "198.51.100.7:41000", http.MethodPost, boothsPath).Code; code != http.StatusCreated {
			t.Fatalf("creation %d with no rate configured: status = %d, want 201", made, code)
		}
	}
}
