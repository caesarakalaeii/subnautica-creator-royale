package soak_test

import (
	"context"
	"encoding/json"
	"net/http"
	"net/http/httptest"
	"os"
	"path/filepath"
	"strings"
	"sync"
	"testing"
	"time"

	"github.com/coder/websocket"

	"github.com/caesarakalaeii/subnautica-creator-royale/backend/internal/soak"
	"github.com/caesarakalaeii/subnautica-creator-royale/backend/internal/telemetry"
)

// fixture is the canonical telemetry payload.
func fixture(t *testing.T) []byte {
	t.Helper()

	payload, err := os.ReadFile(filepath.Clean("../../../docs/telemetry/example.json"))
	if err != nil {
		t.Fatalf("reading the contract's example payload: %v", err)
	}

	return payload
}

// The whole service under concurrent load for a couple of seconds: no deadlock, no dropped client,
// no goroutine leak.
func TestAShortSoakIsClean(t *testing.T) {
	ctx, cancel := context.WithTimeout(context.Background(), 30*time.Second)
	defer cancel()

	report, err := soak.Run(ctx, soak.Options{
		Duration: 2 * time.Second,
		Rate:     10,
		Clients:  8,
		Report:   time.Second,
		Fixture:  fixture(t),
	})
	if err != nil {
		t.Fatalf("the soak could not run at all: %v", err)
	}

	for _, problem := range report.Problems {
		t.Errorf("soak problem: %s", problem)
	}

	if report.Accepted < 10 {
		t.Errorf("Accepted = %d after 2s at 10/s, want at least 10", report.Accepted)
	}
	if report.Failed != 0 || report.RateLimited != 0 {
		t.Errorf("Failed = %d, RateLimited = %d, want neither", report.Failed, report.RateLimited)
	}

	// Every client gets the replay on connect and then one frame per push. A client may connect
	// between two pushes, so exact equality is not possible, but none may be stuck on the replay.
	if report.Frames < int64(8*report.Accepted) {
		t.Errorf("Frames = %d across 8 clients and %d pushes, want at least %d",
			report.Frames, report.Accepted, 8*report.Accepted)
	}

	if report.SlowDrops != 0 {
		t.Errorf("SlowDrops = %d: a client that was keeping up got dropped", report.SlowDrops)
	}
	if report.RefusedClients != 0 {
		t.Errorf("RefusedClients = %d against the default cap of 64", report.RefusedClients)
	}
}

func TestTheHarnessNoticesAServiceThatRefuses(t *testing.T) {
	ctx, cancel := context.WithTimeout(context.Background(), 30*time.Second)
	defer cancel()

	report, err := soak.Run(ctx, soak.Options{
		Duration: time.Second,
		Rate:     40,
		Clients:  2,
		Report:   time.Second,
		Fixture:  fixture(t),

		// Below the push rate, so the service starts refusing partway through the second.
		IngestRate: 2,
	})
	if err != nil {
		t.Fatalf("the soak could not run at all: %v", err)
	}

	if report.RateLimited == 0 {
		t.Error("pushing at 40/s against a limit of 2/s reported nothing rate limited")
	}

	if len(report.Problems) == 0 {
		t.Error("a run that was rate limited reported no problems, so a real soak would read as clean")
	}
}

func TestTheHarnessNoticesAClientItCouldNotConnect(t *testing.T) {
	ctx, cancel := context.WithTimeout(context.Background(), 30*time.Second)
	defer cancel()

	report, err := soak.Run(ctx, soak.Options{
		Duration:       time.Second,
		Rate:           5,
		Clients:        4,
		Report:         time.Second,
		Fixture:        fixture(t),
		MaxLiveClients: 1,
	})
	if err != nil {
		t.Fatalf("the soak could not run at all: %v", err)
	}

	if report.RefusedClients == 0 {
		t.Error("four clients against a cap of one reported none refused")
	}
	if len(report.Problems) == 0 {
		t.Error("a run that could not connect its clients reported no problems")
	}
}

func TestASoakReportsWhileItIsStillRunning(t *testing.T) {
	ctx, cancel := context.WithTimeout(context.Background(), 30*time.Second)
	defer cancel()

	var mu sync.Mutex
	var seen []time.Duration

	report, err := soak.Run(ctx, soak.Options{
		Duration: 2 * time.Second,
		Rate:     20,
		Clients:  2,
		Report:   400 * time.Millisecond,
		Fixture:  fixture(t),
		Observe: func(r soak.Report) {
			mu.Lock()
			defer mu.Unlock()
			seen = append(seen, r.Elapsed)
		},
	})
	if err != nil {
		t.Fatalf("the soak could not run at all: %v", err)
	}

	mu.Lock()
	defer mu.Unlock()

	// Four ticks in two seconds plus the final report; two would mean the ticker never ran.
	if len(seen) < 3 {
		t.Fatalf("Observe was called %d times over 2s at 400ms, want a line while the run was going", len(seen))
	}

	if first := seen[0]; first > report.Elapsed/2 {
		t.Errorf("the first report came at %v of a %v run, which is not during it", first, report.Elapsed)
	}
}

func TestThePeakHeapIsNeverBelowTheHeapAtTheEnd(t *testing.T) {
	ctx, cancel := context.WithTimeout(context.Background(), 30*time.Second)
	defer cancel()

	report, err := soak.Run(ctx, soak.Options{
		Duration: time.Second,
		Rate:     20,
		Clients:  4,
		Report:   250 * time.Millisecond,
		Fixture:  fixture(t),
	})
	if err != nil {
		t.Fatalf("the soak could not run at all: %v", err)
	}

	if report.PeakHeapBytes < report.HeapBytes {
		t.Errorf("PeakHeapBytes = %d is below HeapBytes = %d, so the peak is not being kept",
			report.PeakHeapBytes, report.HeapBytes)
	}
}

// defaultReadLimit is coder/websocket's default message limit, 32 KiB. Not imported, since the soak
// must not depend on it.
const defaultReadLimit = 32 << 10

// bigReplayFixture is the canonical payload with its event tail repeated until one snapshot exceeds
// the default read limit. Sequences ascend, because Store.Apply drops events at or below its
// watermark.
func bigReplayFixture(t *testing.T) []byte {
	t.Helper()

	frame, err := telemetry.Parse(fixture(t))
	if err != nil {
		t.Fatalf("the contract's example payload does not parse: %v", err)
	}
	if len(frame.Events) == 0 {
		t.Fatal("the contract carries no events to repeat")
	}

	tail := frame.Events
	for {
		payload, err := json.Marshal(telemetry.ForSending(frame))
		if err != nil {
			t.Fatalf("marshalling the grown fixture: %v", err)
		}
		if len(payload) > defaultReadLimit {
			return payload
		}

		grown := make([]telemetry.Event, 0, len(frame.Events)+len(tail))
		grown = append(grown, frame.Events...)
		grown = append(grown, tail...)
		for i := range grown {
			grown[i].Sequence = int64(i) + 1
		}
		frame.Events = grown
	}
}

// `/live`'s first message is the whole event window, which on a long run grows to about 150 KB.
func TestAClientSurvivesAReplayBiggerThanTheDefaultReadLimit(t *testing.T) {
	ctx, cancel := context.WithTimeout(context.Background(), 30*time.Second)
	defer cancel()

	payload := bigReplayFixture(t)
	if len(payload) <= defaultReadLimit {
		t.Fatalf("the fixture is %d bytes, want it past the %d-byte default", len(payload), defaultReadLimit)
	}

	report, err := soak.Run(ctx, soak.Options{
		Duration: 2 * time.Second,
		Rate:     10,
		Clients:  4,
		Report:   time.Second,
		Fixture:  payload,
	})
	if err != nil {
		t.Fatalf("the soak could not run at all: %v", err)
	}

	if report.DroppedClients != 0 {
		t.Errorf("DroppedClients = %d reading a %d-byte replay, want 0 — the harness has a read limit again",
			report.DroppedClients, len(payload))
	}
	if report.Frames == 0 {
		t.Error("Frames = 0: no client read anything at all")
	}
	for _, problem := range report.Problems {
		t.Errorf("soak problem: %s", problem)
	}
}

// hangUpBooth is a booth that gives a client one frame and then closes the feed, simulating a
// transient network loss.
func hangUpBooth(t *testing.T, payload []byte) *httptest.Server {
	t.Helper()

	frame, err := telemetry.Parse(payload)
	if err != nil {
		t.Fatalf("the contract's example payload does not parse: %v", err)
	}
	encoded, err := json.Marshal(telemetry.ForSending(frame))
	if err != nil {
		t.Fatalf("marshalling the frame: %v", err)
	}

	mux := http.NewServeMux()
	mux.HandleFunc("/ingest", func(w http.ResponseWriter, _ *http.Request) {
		w.WriteHeader(http.StatusAccepted)
	})
	mux.HandleFunc("/live", func(w http.ResponseWriter, r *http.Request) {
		connection, err := websocket.Accept(w, r, nil)
		if err != nil {
			return
		}

		// One good frame first, so this is distinguishable from a client that could not connect.
		_ = connection.Write(r.Context(), websocket.MessageText, encoded)
		connection.Close(websocket.StatusNormalClosure, "that is all you get")
	})

	server := httptest.NewServer(mux)
	t.Cleanup(server.Close)

	return server
}

func TestAClientReconnectsWhenTheFeedIsCut(t *testing.T) {
	ctx, cancel := context.WithTimeout(context.Background(), 60*time.Second)
	defer cancel()

	payload := fixture(t)
	booth := hangUpBooth(t, payload)

	report, err := soak.Run(ctx, soak.Options{
		Duration:    4 * time.Second,
		Rate:        10,
		Clients:     2,
		Report:      time.Second,
		Fixture:     payload,
		URL:         booth.URL,
		IngestToken: "push",
		ViewToken:   "view",
	})
	if err != nil {
		t.Fatalf("the soak could not run at all: %v", err)
	}

	if report.Reconnects == 0 {
		t.Error("Reconnects = 0: a client that lost the feed never came back, so the rest of the run measured nothing")
	}

	// Each reconnection earns one more frame, so frames past the client count prove the run kept going.
	if report.Frames <= int64(2) {
		t.Errorf("Frames = %d across 2 clients, want the reconnections to have earned more", report.Frames)
	}

	// It still has to report the feed as unstable.
	said := false
	for _, problem := range report.Problems {
		if strings.Contains(problem, "re-established") {
			said = true
		}
	}
	if !said {
		t.Errorf("nothing in Problems mentions the reconnections: %v", report.Problems)
	}

	t.Logf("frames=%d reconnects=%d connected=%d problems=%v",
		report.Frames, report.Reconnects, report.Connected, report.Problems)
}

func TestACleanRunReportsNoReconnections(t *testing.T) {
	ctx, cancel := context.WithTimeout(context.Background(), 30*time.Second)
	defer cancel()

	report, err := soak.Run(ctx, soak.Options{
		Duration: 2 * time.Second,
		Rate:     10,
		Clients:  4,
		Report:   time.Second,
		Fixture:  fixture(t),
	})
	if err != nil {
		t.Fatalf("the soak could not run at all: %v", err)
	}

	if report.Reconnects != 0 {
		t.Errorf("Reconnects = %d on a run nothing interrupted", report.Reconnects)
	}
	if report.Connected != 4 {
		t.Errorf("Connected = %d at the end, want all 4 still up", report.Connected)
	}
}
