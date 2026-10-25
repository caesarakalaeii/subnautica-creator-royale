package caster

import (
	"context"
	"encoding/json"
	"io"
	"log/slog"
	"net/http"
	"sort"
	"strings"
	"sync"
	"testing"
	"time"

	"github.com/caesarakalaeii/subnautica-creator-royale/backend/internal/config"
	"github.com/caesarakalaeii/subnautica-creator-royale/backend/internal/tournament"
)

// fakeRegistry is an in-memory booths table. Locked because the reaper runs on its own goroutine
// beside the creation handler.
type fakeRegistry struct {
	mu    sync.Mutex
	rows  map[string]tournament.CreatedBooth
	gone  []string
	fails bool
}

func newFakeRegistry() *fakeRegistry {
	return &fakeRegistry{rows: map[string]tournament.CreatedBooth{}}
}

func (f *fakeRegistry) CreateBooth(_ context.Context, booth tournament.CreatedBooth) error {
	f.mu.Lock()
	defer f.mu.Unlock()

	if f.fails {
		return errBroken
	}

	f.rows[booth.ID] = booth

	return nil
}

func (f *fakeRegistry) CreatedBooths(context.Context) ([]tournament.CreatedBooth, error) {
	f.mu.Lock()
	defer f.mu.Unlock()

	if f.fails {
		return nil, errBroken
	}

	held := make([]tournament.CreatedBooth, 0, len(f.rows))
	for _, row := range f.rows {
		held = append(held, row)
	}

	sort.Slice(held, func(i, j int) bool { return held[i].ID < held[j].ID })

	return held, nil
}

func (f *fakeRegistry) DropBooth(_ context.Context, id string) error {
	f.mu.Lock()
	defer f.mu.Unlock()

	if f.fails {
		return errBroken
	}

	delete(f.rows, id)
	f.gone = append(f.gone, id)

	return nil
}

// dropped returns every id the reaper has asked this registry to remove, in order.
func (f *fakeRegistry) dropped() []string {
	f.mu.Lock()
	defer f.mu.Unlock()

	return append([]string{}, f.gone...)
}

// createdBoothServer is a server holding the reserved tournament booth and accepting creations.
func createdBoothServer(t *testing.T, registry *fakeRegistry) *Server {
	t.Helper()

	cfg := config.Config{Port: 8080, LogLevel: slog.LevelError, LogFormat: config.LogFormatJSON,
		Booths: oneBooth("tournament-push", "tournament-view", "tournament-write")}

	return NewServer(cfg, slog.New(slog.NewTextHandler(io.Discard, nil))).WithBooths(registry)
}

// create creates a booth and returns the reply, failing the test if it was refused.
func create(t *testing.T, server *Server) createdBooth {
	t.Helper()

	recorder := ask(t, server, http.MethodPost, boothsPath, "", "")
	if recorder.Code != http.StatusCreated {
		t.Fatalf("POST %s: status = %d, want 201: %s", boothsPath, recorder.Code, recorder.Body)
	}

	var made createdBooth
	if err := json.Unmarshal(recorder.Body.Bytes(), &made); err != nil {
		t.Fatalf("POST %s: %v in %s", boothsPath, err, recorder.Body)
	}

	return made
}

func TestACreatedBoothsThreeTokensEachWorkOnlyForTheirOwnVerb(t *testing.T) {
	t.Parallel()

	server := createdBoothServer(t, newFakeRegistry())
	made := create(t, server)

	if made.Ingest == made.View || made.View == made.Operator || made.Ingest == made.Operator {
		t.Fatalf("the three tokens are not distinct: %+v", made)
	}

	// Push: the ingest token only.
	if code := pushTo(t, server, made.ID, snapshotFor("Ada", 1), made.Ingest).Code; code != http.StatusAccepted {
		t.Errorf("the ingest token pushing: status = %d, want 202", code)
	}
	for name, wrong := range map[string]string{"view": made.View, "operator": made.Operator} {
		if code := pushTo(t, server, made.ID, snapshotFor("Ada", 2), wrong).Code; code != http.StatusUnauthorized {
			t.Errorf("the %s token pushing: status = %d, want 401", name, code)
		}
	}

	// Read: the view token only. The bracket route works over httptest without a WebSocket upgrade.
	bracket := boothPath(made.ID, bracketPath)
	if code := ask(t, server, http.MethodGet, bracket, "", made.View).Code; code != http.StatusOK {
		t.Errorf("the view token reading the bracket: status = %d, want 200", code)
	}
	for name, wrong := range map[string]string{"ingest": made.Ingest, "operator": made.Operator} {
		if code := ask(t, server, http.MethodGet, bracket, "", wrong).Code; code != http.StatusUnauthorized {
			t.Errorf("the %s token reading the bracket: status = %d, want 401", name, code)
		}
	}

	// Write: the operator token only. With no database an accepted token gets 503, not 401.
	commands := boothPath(made.ID, commandsPath)
	if code := ask(t, server, http.MethodPost, commands, oneCommand, made.Operator).Code; code != http.StatusServiceUnavailable {
		t.Errorf("the operator token writing: status = %d, want 503 for want of a database", code)
	}
	for name, wrong := range map[string]string{"ingest": made.Ingest, "view": made.View} {
		if code := ask(t, server, http.MethodPost, commands, oneCommand, wrong).Code; code != http.StatusUnauthorized {
			t.Errorf("the %s token writing: status = %d, want 401", name, code)
		}
	}
}

func TestOneCreatedBoothsTokensReachNoOtherBooth(t *testing.T) {
	t.Parallel()

	server := createdBoothServer(t, newFakeRegistry())
	mine, theirs := create(t, server), create(t, server)

	if mine.ID == theirs.ID {
		t.Fatalf("two booths were created with one id: %q", mine.ID)
	}

	if code := pushTo(t, server, theirs.ID, snapshotFor("Ada", 1), mine.Ingest).Code; code != http.StatusUnauthorized {
		t.Errorf("my ingest token pushing to their booth: status = %d, want 401", code)
	}

	if code := ask(t, server, http.MethodGet, boothPath(theirs.ID, bracketPath), "", mine.View).Code; code != http.StatusUnauthorized {
		t.Errorf("my view token reading their bracket: status = %d, want 401", code)
	}

	if code := ask(t, server, http.MethodPost, boothPath(theirs.ID, commandsPath), oneCommand, mine.Operator).Code; code != http.StatusUnauthorized {
		t.Errorf("my operator token writing to their booth: status = %d, want 401", code)
	}

	// Nor the reserved tournament booth.
	if code := pushTo(t, server, config.TournamentBooth, snapshotFor("Ada", 1), mine.Ingest).Code; code != http.StatusUnauthorized {
		t.Errorf("my ingest token pushing to the tournament: status = %d, want 401", code)
	}
}

func TestABoothPastItsExpiryIsRefusedTheWayAnUnknownTokenIs(t *testing.T) {
	t.Parallel()

	registry := newFakeRegistry()
	server := createdBoothServer(t, registry)

	at := time.Date(2026, 11, 1, 12, 0, 0, 0, time.UTC)
	server.now = func() time.Time { return at }

	made := create(t, server)

	// One second before expiry: still accepted.
	server.now = func() time.Time { return at.Add(boothLife - time.Second) }
	if code := pushTo(t, server, made.ID, snapshotFor("Ada", 1), made.Ingest).Code; code != http.StatusAccepted {
		t.Fatalf("a second before it expires: status = %d, want 202", code)
	}

	// One second after expiry, before any reap: the per-request check alone must refuse. The
	// answer must match an unknown booth's, or a caller could tell expired ids from never-real ones.
	server.now = func() time.Time { return at.Add(boothLife + time.Second) }

	if dropped := registry.dropped(); len(dropped) != 0 {
		t.Fatalf("the reaper has run: %v, so this test proves nothing about the per-request check", dropped)
	}

	expired := pushTo(t, server, made.ID, snapshotFor("Ada", 2), made.Ingest)
	unknown := pushTo(t, server, "nosuchbooth", snapshotFor("Ada", 2), made.Ingest)

	if expired.Code != http.StatusUnauthorized {
		t.Errorf("pushing to an expired booth: status = %d, want 401", expired.Code)
	}

	if expired.Code != unknown.Code || expired.Body.String() != unknown.Body.String() {
		t.Errorf("an expired booth answers %d %q and an unknown one %d %q; they have to be the same",
			expired.Code, expired.Body, unknown.Code, unknown.Body)
	}

	if code := ask(t, server, http.MethodGet, boothPath(made.ID, bracketPath), "", made.View).Code; code != http.StatusUnauthorized {
		t.Errorf("reading an expired booth's bracket: status = %d, want 401", code)
	}

	if code := ask(t, server, http.MethodPost, boothPath(made.ID, commandsPath), oneCommand, made.Operator).Code; code != http.StatusUnauthorized {
		t.Errorf("writing to an expired booth: status = %d, want 401", code)
	}
}

func TestAConfiguredBoothNeverExpires(t *testing.T) {
	t.Parallel()

	server := createdBoothServer(t, newFakeRegistry())
	server.now = func() time.Time { return time.Date(2027, 1, 1, 0, 0, 0, 0, time.UTC) }

	if code := pushTo(t, server, config.TournamentBooth, snapshotFor("Ada", 1), "tournament-push").Code; code != http.StatusAccepted {
		t.Errorf("pushing to the tournament booth a year later: status = %d, want 202", code)
	}
}

func TestNoPlaintextTokenIsKeptAfterTheReply(t *testing.T) {
	t.Parallel()

	registry := newFakeRegistry()
	server := createdBoothServer(t, registry)
	made := create(t, server)

	for name, token := range map[string]string{
		"ingest": made.Ingest, "view": made.View, "operator": made.Operator,
	} {
		booth := server.Booth(made.ID)
		if booth == nil {
			t.Fatalf("the booth that was just created is not held: %q", made.ID)
		}

		// Only digests are kept, so a heap dump or leaked backup yields nothing presentable.
		if !booth.ingest.matches(made.Ingest) {
			t.Fatal("the booth does not recognise the ingest token it just handed out")
		}

		for _, held := range []credential{booth.ingest, booth.view, booth.operator} {
			if strings.Contains(held.stored(), token) {
				t.Errorf("the %s token is in what the booth stored", name)
			}
		}

		rows, err := registry.CreatedBooths(context.Background())
		if err != nil || len(rows) != 1 {
			t.Fatalf("the registry holds %+v, %v", rows, err)
		}

		for _, written := range []string{rows[0].IngestHash, rows[0].ViewHash, rows[0].OperatorHash} {
			if strings.Contains(written, token) {
				t.Errorf("the %s token is in what was written down", name)
			}
		}
	}
}

func TestTheReaperDropsAnExpiredBoothsMemoryAndItsRows(t *testing.T) {
	t.Parallel()

	registry := newFakeRegistry()
	server := createdBoothServer(t, registry)

	at := time.Date(2026, 11, 1, 12, 0, 0, 0, time.UTC)
	server.now = func() time.Time { return at }

	made := create(t, server)

	if code := pushTo(t, server, made.ID, snapshotFor("Ada", 1), made.Ingest).Code; code != http.StatusAccepted {
		t.Fatalf("the push that gives the booth something to hold: status = %d", code)
	}

	server.now = func() time.Time { return at.Add(boothLife + time.Minute) }
	server.reap(context.Background())

	if held := server.Booth(made.ID); held != nil {
		t.Errorf("the booth is still held in memory after the reaper ran: %+v", held)
	}

	if dropped := registry.dropped(); len(dropped) != 1 || dropped[0] != made.ID {
		t.Errorf("the reaper dropped %v, want just %q", dropped, made.ID)
	}

	// A reaped booth answers exactly like an unknown id.
	reaped := pushTo(t, server, made.ID, snapshotFor("Ada", 2), made.Ingest)
	unknown := pushTo(t, server, "nosuchbooth", snapshotFor("Ada", 2), made.Ingest)

	if reaped.Code != http.StatusUnauthorized {
		t.Errorf("pushing to a reaped booth: status = %d, want 401", reaped.Code)
	}

	if reaped.Body.String() != unknown.Body.String() {
		t.Errorf("a reaped booth answers %q and an unknown one %q", reaped.Body, unknown.Body)
	}
}

func TestTheReaperIsIdempotent(t *testing.T) {
	t.Parallel()

	registry := newFakeRegistry()
	server := createdBoothServer(t, registry)

	at := time.Date(2026, 11, 1, 12, 0, 0, 0, time.UTC)
	server.now = func() time.Time { return at }
	create(t, server)

	server.now = func() time.Time { return at.Add(boothLife + time.Minute) }

	server.reap(context.Background())
	first := registry.dropped()

	server.reap(context.Background())

	if second := registry.dropped(); len(second) != len(first) {
		t.Errorf("the second run dropped %v and the first %v; the second has to be no work", second, first)
	}
}

func TestTheReaperLeavesALiveBoothAlone(t *testing.T) {
	t.Parallel()

	registry := newFakeRegistry()
	server := createdBoothServer(t, registry)

	at := time.Date(2026, 11, 1, 12, 0, 0, 0, time.UTC)
	server.now = func() time.Time { return at }
	old := create(t, server)

	server.now = func() time.Time { return at.Add(boothLife - time.Minute) }
	fresh := create(t, server)

	server.now = func() time.Time { return at.Add(boothLife + time.Minute) }
	server.reap(context.Background())

	if server.Booth(fresh.ID) == nil {
		t.Error("the reaper took a booth with hours left on it")
	}

	if code := pushTo(t, server, fresh.ID, snapshotFor("Ada", 1), fresh.Ingest).Code; code != http.StatusAccepted {
		t.Errorf("pushing to the live booth after a reap: status = %d, want 202", code)
	}

	if dropped := registry.dropped(); len(dropped) != 1 || dropped[0] != old.ID {
		t.Errorf("the reaper dropped %v, want just the expired %q", dropped, old.ID)
	}

	// The reserved tournament booth is not a created booth; dropping it would delete the event's rows.
	for _, id := range registry.dropped() {
		if id == config.TournamentBooth {
			t.Fatal("the reaper dropped the reserved tournament booth")
		}
	}
}

func TestTheReaperIsSafeWhileABoothIsBeingCreated(t *testing.T) {
	t.Parallel()

	server := createdBoothServer(t, newFakeRegistry())

	at := time.Date(2026, 11, 1, 12, 0, 0, 0, time.UTC)
	server.now = func() time.Time { return at }

	// Meaningful under -race.
	made := make(chan string, 20)

	var running sync.WaitGroup
	running.Add(2)

	go func() {
		defer running.Done()

		for range 20 {
			made <- create(t, server).ID
		}

		close(made)
	}()

	go func() {
		defer running.Done()

		for range 50 {
			server.reap(context.Background())
		}
	}()

	running.Wait()

	for id := range made {
		if server.Booth(id) == nil {
			t.Errorf("booth %q was created during a sweep and reaped by it", id)
		}
	}
}

func TestABoothCreatedBeforeARestartStillAnswers(t *testing.T) {
	t.Parallel()

	registry := newFakeRegistry()
	before := createdBoothServer(t, registry)

	at := time.Date(2026, 11, 1, 12, 0, 0, 0, time.UTC)
	before.now = func() time.Time { return at }

	made := create(t, before)

	// The same registry, a new server.
	after := createdBoothServer(t, registry)
	after.now = func() time.Time { return at.Add(time.Hour) }

	if err := after.LoadBooths(context.Background()); err != nil {
		t.Fatalf("reading the booths back: %v", err)
	}

	if code := pushTo(t, after, made.ID, snapshotFor("Ada", 1), made.Ingest).Code; code != http.StatusAccepted {
		t.Errorf("pushing to a booth that outlived its process: status = %d, want 202", code)
	}

	// All three tokens: a hash written to the wrong column would only show up after a reload.
	bracket := boothPath(made.ID, bracketPath)
	if code := ask(t, after, http.MethodGet, bracket, "", made.View).Code; code != http.StatusOK {
		t.Errorf("the view token reading after a restart: status = %d, want 200", code)
	}

	// With no database an accepted operator token gets 503, not 401.
	commands := boothPath(made.ID, commandsPath)
	if code := ask(t, after, http.MethodPost, commands, oneCommand, made.Operator).Code; code != http.StatusServiceUnavailable {
		t.Errorf("the operator token writing after a restart: status = %d, want 503 for want of a database", code)
	}

	for name, wrong := range map[string]string{"ingest": made.Ingest, "view": made.View} {
		if code := ask(t, after, http.MethodPost, commands, oneCommand, wrong).Code; code != http.StatusUnauthorized {
			t.Errorf("the %s token writing after a restart: status = %d, want 401", name, code)
		}
	}

	// Expiry is from creation time, not from the restart.
	after.now = func() time.Time { return at.Add(boothLife + time.Minute) }

	if code := pushTo(t, after, made.ID, snapshotFor("Ada", 2), made.Ingest).Code; code != http.StatusUnauthorized {
		t.Errorf("a restart moved the expiry: status = %d, want 401", code)
	}
}

func TestABoothsOwnDashboardUrlServesTheDashboard(t *testing.T) {
	t.Parallel()

	server := createdBoothServer(t, newFakeRegistry())
	made := create(t, server)

	for _, page := range []string{made.Dashboard, made.Dashboard + "bracket.html", made.Dashboard + "console.html"} {
		recorder := ask(t, server, http.MethodGet, page, "", "")

		if recorder.Code != http.StatusOK {
			t.Errorf("GET %s: status = %d, want the page", page, recorder.Code)
		}
	}

	// Identical to the bare paths: the page reads its booth from the address.
	underBooth := ask(t, server, http.MethodGet, made.Dashboard, "", "")
	bare := ask(t, server, http.MethodGet, "/", "", "")

	if underBooth.Body.String() != bare.Body.String() {
		t.Error("a booth's dashboard is not the same page the bare path serves")
	}
}

func TestAPageSaysNothingAboutWhetherItsBoothExists(t *testing.T) {
	t.Parallel()

	server := createdBoothServer(t, newFakeRegistry())
	made := create(t, server)

	// Pages take no token, so differing answers would let anyone enumerate running booth ids.
	real := ask(t, server, http.MethodGet, made.Dashboard, "", "")
	unknown := ask(t, server, http.MethodGet, "/b/nosuchbooth/", "", "")

	if real.Code != unknown.Code {
		t.Errorf("a real booth's page answers %d and an unknown booth's %d", real.Code, unknown.Code)
	}
}
