package caster

import (
	"sync"
	"testing"
	"time"

	"github.com/caesarakalaeii/subnautica-creator-royale/backend/internal/telemetry"
)

func snapshotWith(events ...telemetry.Event) telemetry.Snapshot {
	return telemetry.Snapshot{
		Version: telemetry.Version,
		At:      time.Date(2026, 10, 24, 12, 0, 0, 0, time.UTC),
		Events:  events,
	}
}

func event(sequence int64) telemetry.Event {
	return telemetry.Event{Sequence: sequence, Group: 1, Generation: 1, Kind: "ZoneBite"}
}

func TestAnEmptyStoreHasNothingToShow(t *testing.T) {
	store := NewStore()

	if _, ok := store.Latest(); ok {
		t.Error("a store nobody has pushed to claims to have state")
	}

	if got := len(store.EventsAfter(0)); got != 0 {
		t.Errorf("events = %d, want none", got)
	}
}

func TestResendingEventsDoesNotDuplicateThem(t *testing.T) {
	store := NewStore()

	if added := store.Apply(snapshotWith(event(1), event(2), event(3))); added != 3 {
		t.Fatalf("first push added %d, want 3", added)
	}

	if added := store.Apply(snapshotWith(event(2), event(3), event(4))); added != 1 {
		t.Errorf("overlapping push added %d, want only the new one", added)
	}

	if got := len(store.EventsAfter(0)); got != 4 {
		t.Errorf("events = %d, want 4", got)
	}

	if store.LastSequence() != 4 {
		t.Errorf("last sequence = %d, want 4", store.LastSequence())
	}
}

func TestAPushOfNothingNewIsAccepted(t *testing.T) {
	store := NewStore()
	store.Apply(snapshotWith(event(1)))

	if added := store.Apply(snapshotWith(event(1))); added != 0 {
		t.Errorf("added = %d, want 0", added)
	}
}

func TestALogThatStartsAgainAtOneIsARestartRatherThanADuplicate(t *testing.T) {
	store := NewStore()
	store.Apply(snapshotWith(event(1), event(2), event(3)))

	added := store.Apply(snapshotWith(event(1), event(2)))

	if added != 2 {
		t.Errorf("added = %d, want the restarted log's events to be taken", added)
	}

	if store.LastSequence() != 2 {
		t.Errorf("last sequence = %d, want the new log's 2", store.LastSequence())
	}
}

func TestTheLatestSnapshotReplacesTheOneBefore(t *testing.T) {
	store := NewStore()

	store.Apply(telemetry.Snapshot{Version: telemetry.Version,
		Match: &telemetry.Match{Group: 1, Generation: 1}})
	store.Apply(telemetry.Snapshot{Version: telemetry.Version,
		Match: &telemetry.Match{Group: 2, Generation: 1}})

	snapshot, ok := store.Latest()
	if !ok || snapshot.Match == nil || snapshot.Match.Group != 2 {
		t.Errorf("latest = %+v, want group 2", snapshot.Match)
	}
}

func TestEventsCanBeReadFromACursor(t *testing.T) {
	store := NewStore()
	store.Apply(snapshotWith(event(1), event(2), event(3), event(4)))

	tail := store.EventsAfter(2)

	if len(tail) != 2 || tail[0].Sequence != 3 || tail[1].Sequence != 4 {
		t.Errorf("tail = %+v, want events 3 and 4", tail)
	}
}

func TestTheEventWindowIsBounded(t *testing.T) {
	store := NewStore()

	for sequence := int64(1); sequence <= maxEvents+100; sequence++ {
		store.Apply(snapshotWith(event(sequence)))
	}

	kept := store.EventsAfter(0)
	if len(kept) != maxEvents {
		t.Fatalf("kept %d events, want the window of %d", len(kept), maxEvents)
	}

	if kept[0].Sequence != 101 {
		t.Errorf("oldest kept = %d, want 101 once 100 have fallen off", kept[0].Sequence)
	}

	if store.LastSequence() != maxEvents+100 {
		t.Errorf("last sequence = %d, want the newest", store.LastSequence())
	}
}

// TestTheStoreIsSafeToReadWhileItIsWrittenTo is only meaningful under -race.
func TestTheStoreIsSafeToReadWhileItIsWrittenTo(t *testing.T) {
	store := NewStore()

	var waiting sync.WaitGroup
	waiting.Add(2)

	go func() {
		defer waiting.Done()
		for sequence := int64(1); sequence <= 200; sequence++ {
			store.Apply(snapshotWith(event(sequence)))
		}
	}()

	go func() {
		defer waiting.Done()
		for i := 0; i < 200; i++ {
			store.Latest()
			store.EventsAfter(0)
			store.LastSequence()
		}
	}()

	waiting.Wait()
}

func TestTheStoreCountsWhatItEvicted(t *testing.T) {
	store := NewStore()

	if store.Evicted() != 0 {
		t.Errorf("Evicted() = %d on an empty store, want 0", store.Evicted())
	}

	for sequence := int64(1); sequence <= maxEvents; sequence++ {
		store.Apply(snapshotWith(event(sequence)))
	}

	if store.Evicted() != 0 {
		t.Errorf("Evicted() = %d with the window exactly full, want 0", store.Evicted())
	}

	for sequence := int64(maxEvents + 1); sequence <= maxEvents+100; sequence++ {
		store.Apply(snapshotWith(event(sequence)))
	}

	if store.Evicted() != 100 {
		t.Errorf("Evicted() = %d after 100 fell off the end, want 100", store.Evicted())
	}
}

func TestTheTableIsHeldAcrossThePushesThatDoNotCarryIt(t *testing.T) {
	store := NewStore()

	pushed := snapshotWith(event(1))
	pushed.StandingsRevision = 1
	pushed.Standings = &telemetry.Standings{
		Matches: 1,
		Entries: []telemetry.StandingsLine{{Name: "Vela", Points: 4}},
	}
	store.Apply(pushed)

	// Same revision, no table: what most pushes look like.
	quiet := snapshotWith(event(2))
	quiet.StandingsRevision = 1
	store.Apply(quiet)

	latest, ok := store.Latest()
	if !ok {
		t.Fatal("no state after two pushes")
	}

	if latest.Standings == nil {
		t.Fatal("the table was dropped by a push that did not carry it")
	}

	if latest.Standings.Entries[0].Name != "Vela" || latest.StandingsRevision != 1 {
		t.Errorf("held = %+v at revision %d, want Vela at 1",
			latest.Standings.Entries[0], latest.StandingsRevision)
	}
}

func TestANewRevisionWithNoTableClearsTheOneHeld(t *testing.T) {
	store := NewStore()

	pushed := snapshotWith()
	pushed.StandingsRevision = 3
	pushed.Standings = &telemetry.Standings{Matches: 1}
	store.Apply(pushed)

	cleared := snapshotWith()
	cleared.StandingsRevision = 4
	store.Apply(cleared)

	latest, _ := store.Latest()
	if latest.Standings != nil {
		t.Errorf("standings = %+v, want nil once the revision moved with no table", latest.Standings)
	}

	if latest.StandingsRevision != 4 {
		t.Errorf("revision = %d, want 4", latest.StandingsRevision)
	}
}

func TestARestartedBoothTakesTheTableOffTheHeartbeat(t *testing.T) {
	store := NewStore()

	beat := snapshotWith()
	beat.StandingsRevision = 7
	beat.Standings = &telemetry.Standings{
		Matches: 4,
		Entries: []telemetry.StandingsLine{{Name: "Quill", Points: 11}},
	}
	store.Apply(beat)

	// Same revision again, with the table: a heartbeat.
	store.Apply(beat)

	latest, _ := store.Latest()
	if latest.Standings == nil || latest.Standings.Entries[0].Name != "Quill" {
		t.Errorf("standings = %+v, want Quill's table", latest.Standings)
	}
}

func TestNoTableEverSentIsRevisionZeroAndNothingHeld(t *testing.T) {
	store := NewStore()
	store.Apply(snapshotWith(event(1)))

	latest, _ := store.Latest()
	if latest.Standings != nil || latest.StandingsRevision != 0 {
		t.Errorf("standings = %+v at %d, want nil at 0", latest.Standings, latest.StandingsRevision)
	}
}
