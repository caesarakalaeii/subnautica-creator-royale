package caster

import (
	"fmt"
	"io"
	"log/slog"
	"net/http"
	"runtime"
	"strings"
	"testing"
	"time"

	"github.com/caesarakalaeii/subnautica-creator-royale/backend/internal/config"
	"github.com/caesarakalaeii/subnautica-creator-royale/backend/internal/telemetry"
)

// boothFootprintCeiling is the most a full booth may cost before the `LMS_MAX_BOOTHS` default is
// wrong: about a third above the measured 0.90 MiB, so heap noise does not fail the test.
const boothFootprintCeiling = 1.2 * 1024 * 1024

// boundedBoothServer is a server that holds `creatable` booths beyond the configured tournament
// booth before refusing; the ceiling counts configured and created booths alike.
func boundedBoothServer(t *testing.T, registry *fakeRegistry, creatable int) *Server {
	t.Helper()

	configured := oneBooth("tournament-push", "tournament-view", "tournament-write")

	cfg := config.Config{Port: 8080, LogLevel: slog.LevelError, LogFormat: config.LogFormatJSON,
		MaxBooths: len(configured) + creatable,
		Booths:    configured}

	return NewServer(cfg, slog.New(slog.NewTextHandler(io.Discard, nil))).WithBooths(registry)
}

func TestABoothPastTheConfiguredMaximumIsRefusedWithAReadableReason(t *testing.T) {
	t.Parallel()

	server := boundedBoothServer(t, newFakeRegistry(), 2)

	create(t, server)
	create(t, server)

	refused := ask(t, server, http.MethodPost, boothsPath, "", "")
	if refused.Code != http.StatusServiceUnavailable {
		t.Fatalf("the third booth: status = %d, want 503", refused.Code)
	}

	said := reason(t, refused)
	for _, word := range []string{"full", "later"} {
		if !strings.Contains(said, word) {
			t.Errorf("the refusal says %q, want it to mention %q", said, word)
		}
	}
}

func TestABoothRefusedAtTheCeilingDoesNotDisturbTheBoothsAlreadyRunning(t *testing.T) {
	t.Parallel()

	server := boundedBoothServer(t, newFakeRegistry(), 1)

	running := create(t, server)

	if code := ask(t, server, http.MethodPost, boothsPath, "", "").Code; code != http.StatusServiceUnavailable {
		t.Fatalf("the second booth: status = %d, want 503", code)
	}

	if code := pushTo(t, server, running.ID, snapshotFor("Ada", 1), running.Ingest).Code; code != http.StatusAccepted {
		t.Errorf("pushing to the booth that was already running: status = %d, want 202", code)
	}

	if code := ask(t, server, http.MethodGet, boothPath(running.ID, bracketPath), "", running.View).Code; code != http.StatusOK {
		t.Errorf("reading the running booth's bracket: status = %d, want 200", code)
	}

	// The reserved tournament booth is unaffected by the ceiling.
	if code := pushTo(t, server, config.TournamentBooth, snapshotFor("Ada", 1), "tournament-push").Code; code != http.StatusAccepted {
		t.Errorf("pushing to the tournament booth at the ceiling: status = %d, want 202", code)
	}
}

func TestAConfiguredBoothCountsAgainstTheCeiling(t *testing.T) {
	t.Parallel()

	cfg := config.Config{Port: 8080, LogLevel: slog.LevelError, LogFormat: config.LogFormatJSON,
		MaxBooths: 2,
		Booths:    twoBooths}

	server := NewServer(cfg, slog.New(slog.NewTextHandler(io.Discard, nil))).WithBooths(newFakeRegistry())

	if code := ask(t, server, http.MethodPost, boothsPath, "", "").Code; code != http.StatusServiceUnavailable {
		t.Errorf("creating a booth with the ceiling already filled by configured ones: "+
			"status = %d, want 503", code)
	}
}

func TestAnExpiredBoothGivesItsSlotBack(t *testing.T) {
	t.Parallel()

	registry := newFakeRegistry()
	server := boundedBoothServer(t, registry, 1)

	at := server.now()
	server.now = func() time.Time { return at }

	create(t, server)

	if code := ask(t, server, http.MethodPost, boothsPath, "", "").Code; code != http.StatusServiceUnavailable {
		t.Fatalf("the second booth while the first is live: status = %d, want 503", code)
	}

	server.now = func() time.Time { return at.Add(boothLife + time.Minute) }

	if code := ask(t, server, http.MethodPost, boothsPath, "", "").Code; code != http.StatusCreated {
		t.Errorf("a booth after the first expired: status = %d, want 201", code)
	}
}

func TestABoothsFootprintIsAboutWhatTheCeilingAssumes(t *testing.T) {
	// No t.Parallel(): HeapAlloc is process-wide. Each booth is measured full: event window at its
	// cap and a fan-out holding a full buffer for one client.
	const measured = 100

	payload := examplePayload(t)

	snapshot, err := telemetry.Parse(payload)
	if err != nil {
		t.Fatalf("parsing the contract's example payload: %v", err)
	}

	// The shipped defaults, since the ceiling is a claim about them.
	deployed, err := config.Load(func(string) string { return "" })
	if err != nil {
		t.Fatalf("loading the default configuration: %v", err)
	}

	var before, after runtime.MemStats

	runtime.GC()
	runtime.ReadMemStats(&before)

	held := make([]*Booth, 0, measured)
	for made := range measured {
		booth := &Booth{
			id:      fmt.Sprintf("measured%03d", made),
			store:   NewStore(),
			hub:     NewHub(deployed.MaxLiveClients),
			console: &console{},
			cameras: newCameras(),
		}

		fillTheWindow(booth.store, snapshot)

		// One client that never reads, so the fan-out holds a full buffer.
		if _, _, admitted := booth.hub.Subscribe(func() {}); !admitted {
			t.Fatal("the fan-out refused its first client")
		}

		for range SubscriberBuffer {
			booth.hub.Broadcast(append([]byte(nil), payload...))
		}

		held = append(held, booth)
	}

	runtime.GC()
	runtime.ReadMemStats(&after)

	perBooth := float64(after.HeapAlloc-before.HeapAlloc) / measured
	t.Logf("a full booth is %.0f bytes (%.2f MiB); %d of them is %.0f MiB",
		perBooth, perBooth/(1<<20), deployed.MaxBooths, perBooth*float64(deployed.MaxBooths)/(1<<20))

	if perBooth > boothFootprintCeiling {
		t.Errorf("a full booth is %.2f MiB, over the %.2f MiB the ceiling of %d assumes; "+
			"remeasure and rewrite both the number and backend/README.md",
			perBooth/(1<<20), boothFootprintCeiling/(1<<20), deployed.MaxBooths)
	}

	// Keep the booths reachable until after the second reading.
	runtime.KeepAlive(held)
}

// fillTheWindow pushes events until the store's window is at its cap, with every optional field
// populated so the measurement reflects a real match.
func fillTheWindow(store *Store, snapshot telemetry.Snapshot) {
	// About what a busy second of a match carries.
	const perPush = 20

	player, actor := "a-player-with-a-plausible-name", "another-players-name"
	detail, biome := "hit by a stasis rifle round", "Grand Reef Deep"
	phase, amount := 3, 42.5
	x, y, z := 100.0, -200.0, 300.0

	for sequence := int64(0); sequence < maxEvents; {
		push := snapshot
		push.Events = make([]telemetry.Event, 0, perPush)

		for range perPush {
			sequence++
			push.Events = append(push.Events, telemetry.Event{
				Sequence: sequence, Group: 2, Generation: 1, At: snapshot.At, ElapsedMs: 60000,
				Kind: "PlayerEliminated", Player: &player, Actor: &actor, Phase: &phase,
				Amount: &amount, Detail: &detail, X: &x, Y: &y, Z: &z, Biome: &biome,
			})
		}

		store.Apply(push)
	}
}

func TestAMaximumOfZeroIsNoCeilingAtAll(t *testing.T) {
	t.Parallel()

	cfg := config.Config{Port: 8080, LogLevel: slog.LevelError, LogFormat: config.LogFormatJSON,
		MaxBooths: 0,
		Booths:    oneBooth("tournament-push", "tournament-view", "tournament-write")}

	server := NewServer(cfg, slog.New(slog.NewTextHandler(io.Discard, nil))).WithBooths(newFakeRegistry())

	for made := 1; made <= 20; made++ {
		if code := ask(t, server, http.MethodPost, boothsPath, "", "").Code; code != http.StatusCreated {
			t.Fatalf("booth %d with no ceiling configured: status = %d, want 201", made, code)
		}
	}
}
