package caster

import (
	"context"
	"encoding/json"
	"errors"
	"io"
	"log/slog"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"
	"time"

	"github.com/coder/websocket"

	"github.com/caesarakalaeii/subnautica-creator-royale/backend/internal/config"
	"github.com/caesarakalaeii/subnautica-creator-royale/backend/internal/telemetry"
)

const viewToken = "a-view-secret"

// liveServer is a real HTTP server over the real handler, since an httptest.Recorder cannot be
// upgraded to a WebSocket.
func liveServer(t *testing.T, ingest, view string) (*Server, string) {
	t.Helper()

	cfg := config.Config{Port: 8080, LogLevel: slog.LevelError, LogFormat: config.LogFormatJSON,
		Booths: oneBooth(ingest, view, "")}
	server := NewServer(cfg, slog.New(slog.NewTextHandler(io.Discard, nil)))

	httpServer := httptest.NewServer(server.Handler())
	t.Cleanup(httpServer.Close)

	return server, "ws" + strings.TrimPrefix(httpServer.URL, "http") + livePath
}

func dial(t *testing.T, url string) (*websocket.Conn, context.Context) {
	t.Helper()

	ctx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
	t.Cleanup(cancel)

	connection, _, err := websocket.Dial(ctx, url, nil)
	if err != nil {
		t.Fatalf("dial %s: %v", url, err)
	}

	t.Cleanup(func() { connection.CloseNow() })

	return connection, ctx
}

func readSnapshot(t *testing.T, ctx context.Context, connection *websocket.Conn) telemetry.Snapshot {
	t.Helper()

	_, payload, err := connection.Read(ctx)
	if err != nil {
		t.Fatalf("read: %v", err)
	}

	var snapshot telemetry.Snapshot
	if err := json.Unmarshal(payload, &snapshot); err != nil {
		t.Fatalf("the feed sent something that is not the contract: %v (%q)", err, payload)
	}

	return snapshot
}

func pushExample(t *testing.T, server *Server) {
	t.Helper()

	recorder := push(t, server, examplePayload(t), token)
	if recorder.Code != http.StatusAccepted {
		t.Fatalf("seeding the store failed: %d %s", recorder.Code, recorder.Body.String())
	}
}

func TestAClientJoiningMidMatchIsSentTheStateAtOnce(t *testing.T) {
	server, url := liveServer(t, token, viewToken)
	pushExample(t, server)

	connection, ctx := dial(t, url+"?token="+viewToken)
	snapshot := readSnapshot(t, ctx, connection)

	if snapshot.Match == nil || snapshot.Match.Group != 2 {
		t.Fatalf("first message = %+v, want the match already in progress", snapshot.Match)
	}

	if len(snapshot.Players) != 2 {
		t.Errorf("players = %d, want the two already in the store", len(snapshot.Players))
	}

	if len(snapshot.Events) != 3 {
		t.Errorf("events = %d, want the whole window replayed", len(snapshot.Events))
	}
}

func TestAClientJoiningBeforeAnyPushGetsAnEmptyButValidState(t *testing.T) {
	_, url := liveServer(t, token, viewToken)

	connection, ctx := dial(t, url+"?token="+viewToken)
	snapshot := readSnapshot(t, ctx, connection)

	if snapshot.Version != telemetry.Version {
		t.Errorf("version = %d, want %d", snapshot.Version, telemetry.Version)
	}

	if snapshot.Match != nil || len(snapshot.Events) != 0 {
		t.Errorf("snapshot = %+v, want an empty one", snapshot)
	}
}

func TestAReconnectingClientCanResumeFromItsCursor(t *testing.T) {
	server, url := liveServer(t, token, viewToken)
	pushExample(t, server)

	connection, ctx := dial(t, url+"?token="+viewToken+"&after=42")
	snapshot := readSnapshot(t, ctx, connection)

	if len(snapshot.Events) != 1 || snapshot.Events[0].Sequence != 43 {
		t.Errorf("events = %+v, want only the one after 42", snapshot.Events)
	}

	// A cursor trims the event history, never the state.
	if snapshot.Match == nil || len(snapshot.Players) != 2 {
		t.Error("resuming from a cursor lost the state, which would leave the dashboard blank")
	}
}

func TestAPushReachesAConnectedClientVerbatim(t *testing.T) {
	server, url := liveServer(t, token, viewToken)

	connection, ctx := dial(t, url+"?token="+viewToken)
	readSnapshot(t, ctx, connection) // the initial empty state

	pushExample(t, server)

	_, payload, err := connection.Read(ctx)
	if err != nil {
		t.Fatalf("read after a push: %v", err)
	}

	// Byte for byte what the mod sent; trimmed only for the file's trailing newline.
	if strings.TrimSpace(string(payload)) != strings.TrimSpace(string(examplePayload(t))) {
		t.Errorf("forwarded payload differs from what was pushed:\n got %s\nwant %s",
			payload, examplePayload(t))
	}
}

func TestEveryConnectedClientGetsThePush(t *testing.T) {
	server, url := liveServer(t, token, viewToken)

	first, firstCtx := dial(t, url+"?token="+viewToken)
	second, secondCtx := dial(t, url+"?token="+viewToken)

	readSnapshot(t, firstCtx, first)
	readSnapshot(t, secondCtx, second)

	waitForClients(t, server, 2)
	pushExample(t, server)

	if got := readSnapshot(t, firstCtx, first); got.Match == nil {
		t.Error("the first client did not get the push")
	}

	if got := readSnapshot(t, secondCtx, second); got.Match == nil {
		t.Error("the second client did not get the push")
	}
}

func TestTheLiveFeedRefusesAClientWithoutTheViewToken(t *testing.T) {
	_, url := liveServer(t, token, viewToken)
	httpURL := "http" + strings.TrimPrefix(url, "ws")

	for _, query := range []string{"", "?token=", "?token=wrong", "?token=" + token} {
		response, err := http.Get(httpURL + query)
		if err != nil {
			t.Fatalf("get: %v", err)
		}
		_ = response.Body.Close()

		if response.StatusCode != http.StatusUnauthorized {
			t.Errorf("query %q: status = %d, want 401", query, response.StatusCode)
		}
	}
}

func TestTheIngestTokenIsNotAViewToken(t *testing.T) {
	_, url := liveServer(t, token, viewToken)

	ctx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
	defer cancel()

	connection, _, err := websocket.Dial(ctx, url+"?token="+token, nil)
	if err == nil {
		connection.CloseNow()
		t.Fatal("the ingest token opened the live feed")
	}
}

func TestTheLiveFeedIsUnavailableWithNoViewTokenConfigured(t *testing.T) {
	_, url := liveServer(t, token, "")
	httpURL := "http" + strings.TrimPrefix(url, "ws")

	response, err := http.Get(httpURL)
	if err != nil {
		t.Fatalf("get: %v", err)
	}
	defer func() { _ = response.Body.Close() }()

	if response.StatusCode != http.StatusServiceUnavailable {
		t.Errorf("status = %d, want 503 when the feed is not configured", response.StatusCode)
	}
}

func TestADisconnectedClientLeavesTheFanOut(t *testing.T) {
	server, url := liveServer(t, token, viewToken)

	connection, ctx := dial(t, url+"?token="+viewToken)
	readSnapshot(t, ctx, connection)
	waitForClients(t, server, 1)

	_ = connection.Close(websocket.StatusNormalClosure, "done")

	waitForClients(t, server, 0)
}

// waitForClients waits for the hub to reach a client count, since connects and disconnects are
// asynchronous to the test.
func waitForClients(t *testing.T, server *Server, want int) {
	t.Helper()

	deadline := time.Now().Add(3 * time.Second)
	for time.Now().Before(deadline) {
		if server.Hub().Count() == want {
			return
		}
		time.Sleep(5 * time.Millisecond)
	}

	t.Fatalf("clients = %d, want %d", server.Hub().Count(), want)
}

// cappedServer is the service with a bound on how many may watch at once.
func cappedServer(t *testing.T, max int) (*Server, string) {
	t.Helper()

	cfg := config.Config{Port: 8080, LogLevel: slog.LevelError, LogFormat: config.LogFormatJSON,
		Booths: oneBooth(token, viewToken, ""), MaxLiveClients: max}
	server := NewServer(cfg, slog.New(slog.NewTextHandler(io.Discard, nil)))

	httpServer := httptest.NewServer(server.Handler())
	t.Cleanup(httpServer.Close)

	return server, "ws" + strings.TrimPrefix(httpServer.URL, "http") + livePath
}

// TestAClientBeyondTheCapIsToldWhyRatherThanRefusedSilently expects a 1013 close after the upgrade,
// since a browser cannot read a failed handshake's status.
func TestAClientBeyondTheCapIsToldWhyRatherThanRefusedSilently(t *testing.T) {
	_, url := cappedServer(t, 1)

	first, firstCtx := dial(t, url+"?token="+viewToken)
	readSnapshot(t, firstCtx, first)

	second, secondCtx := dial(t, url+"?token="+viewToken)

	_, _, err := second.Read(secondCtx)
	if err == nil {
		t.Fatal("a second client was served against a cap of 1")
	}

	if status := websocket.CloseStatus(err); status != websocket.StatusTryAgainLater {
		t.Errorf("close status = %v, want %v (1013) so the page can tell this from a bad token",
			status, websocket.StatusTryAgainLater)
	}

	var closed websocket.CloseError
	if errors.As(err, &closed) && !strings.Contains(closed.Reason, "capacity") {
		t.Errorf("close reason = %q, want it to say what happened", closed.Reason)
	}
}

func TestTheClientsAlreadyWatchingKeepTheMatch(t *testing.T) {
	server, url := cappedServer(t, 1)
	pushExample(t, server)

	first, firstCtx := dial(t, url+"?token="+viewToken)
	readSnapshot(t, firstCtx, first)

	second, secondCtx := dial(t, url+"?token="+viewToken)
	if _, _, err := second.Read(secondCtx); err == nil {
		t.Fatal("a second client was served against a cap of 1")
	}

	// The push after the refusal still reaches the client that was there first.
	pushExample(t, server)

	if snapshot := readSnapshot(t, firstCtx, first); snapshot.Match == nil {
		t.Error("the first client stopped receiving after somebody else was refused")
	}
}

func TestARefusedClientLeavesTheSlotFree(t *testing.T) {
	server, url := cappedServer(t, 1)

	for attempt := 0; attempt < 5; attempt++ {
		refused, ctx := dial(t, url+"?token="+viewToken)
		if _, _, err := refused.Read(ctx); err != nil && websocket.CloseStatus(err) == websocket.StatusTryAgainLater {
			t.Fatalf("attempt %d was refused with nobody connected", attempt)
		}

		refused.CloseNow()
		waitForClients(t, server, 0)
	}
}

// beatingServer is the service with a heartbeat fast enough to observe in a test.
func beatingServer(t *testing.T, beat time.Duration) (*Server, string) {
	t.Helper()

	cfg := config.Config{Port: 8080, LogLevel: slog.LevelError, LogFormat: config.LogFormatJSON,
		Booths: oneBooth(token, viewToken, "")}
	server := NewServer(cfg, slog.New(slog.NewTextHandler(io.Discard, nil)))
	server.heartbeat = beat

	httpServer := httptest.NewServer(server.Handler())
	t.Cleanup(httpServer.Close)

	return server, "ws" + strings.TrimPrefix(httpServer.URL, "http") + livePath
}

func TestAnIdleConnectionSurvivesWithNothingOnTheWire(t *testing.T) {
	server, url := beatingServer(t, 20*time.Millisecond)

	connection, ctx := dial(t, url+"?token="+viewToken)

	// Keep reading: the library answers pings from inside Read, so not reading would model a
	// wedged peer, not an idle one.
	frames := make(chan []byte, 8)
	go func() {
		for {
			_, payload, err := connection.Read(ctx)
			if err != nil {
				return
			}

			select {
			case frames <- payload:
			default:
			}
		}
	}()

	select {
	case <-frames:
	case <-time.After(3 * time.Second):
		t.Fatal("no replay on connect")
	}

	waitForClients(t, server, 1)

	// Twenty-five heartbeats and not one push.
	time.Sleep(500 * time.Millisecond)

	if clients := server.Hub().Count(); clients != 1 {
		t.Fatalf("clients = %d after 500ms of silence, want the idle one still connected", clients)
	}

	// Still a working feed, not merely an open socket.
	pushExample(t, server)

	select {
	case payload := <-frames:
		var snapshot telemetry.Snapshot
		if err := json.Unmarshal(payload, &snapshot); err != nil || snapshot.Match == nil {
			t.Errorf("the idle connection stopped carrying the match: %v", err)
		}
	case <-time.After(3 * time.Second):
		t.Error("nothing arrived on a connection that had been idle")
	}
}

func TestAConnectionThatStopsAnsweringIsDropped(t *testing.T) {
	server, url := beatingServer(t, 20*time.Millisecond)

	// Dialled directly: this client never reads after the replay, so it never answers a ping,
	// which is how a wedged peer behaves.
	ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	defer cancel()

	connection, _, err := websocket.Dial(ctx, url+"?token="+viewToken, nil)
	if err != nil {
		t.Fatalf("dial: %v", err)
	}
	defer connection.CloseNow()

	readSnapshot(t, ctx, connection)
	waitForClients(t, server, 1)

	// The booth must notice the unanswered pings and free the slot.
	waitForClients(t, server, 0)
}
