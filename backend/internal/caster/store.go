package caster

import (
	"sync"

	"github.com/caesarakalaeii/subnautica-creator-royale/backend/internal/telemetry"
)

// maxEvents is how much of the feed is kept. It is a bounded window, not storage: the game server's
// audit log is the durable record.
const maxEvents = 5000

// Store is the service's in-memory state: the last snapshot pushed and a window of the events that
// led to it. A restart loses it by design; the audit log is the tournament's evidence.
type Store struct {
	mu sync.RWMutex

	latest    telemetry.Snapshot
	haveState bool

	events []telemetry.Event

	// evicted is how many events have fallen off the end of the window since startup.
	evicted int64

	// lastSeq is the highest audit sequence taken so far. The mod resends anything it is unsure
	// landed, so the sequence is what makes ingest idempotent.
	lastSeq int64

	// standings is the tournament table, held across pushes that omit it. The mod sends it only
	// when it changes and on a slow heartbeat; pushes in between carry nil.
	standings    *telemetry.Standings
	standingsRev int
}

// NewStore is an empty store, which is what the booth looks like before the mod first pushes.
func NewStore() *Store {
	return &Store{}
}

// Apply takes one push and returns how many of its events were new. Fewer than sent means the push
// overlapped an earlier one, which is expected.
func (s *Store) Apply(snapshot telemetry.Snapshot) int {
	s.mu.Lock()
	defer s.mu.Unlock()

	s.holdStandings(&snapshot)

	s.latest = snapshot
	s.haveState = true

	// Sequences are per log file, so a push starting again at one is a restarted game server,
	// not a duplicate; without this reset the feed would freeze.
	if len(snapshot.Events) > 0 && snapshot.Events[0].Sequence == 1 && s.lastSeq > 1 {
		s.lastSeq = 0
	}

	added := 0
	for _, event := range snapshot.Events {
		if event.Sequence <= s.lastSeq {
			continue
		}

		s.events = append(s.events, event)
		s.lastSeq = event.Sequence
		added++
	}

	if overflow := len(s.events) - maxEvents; overflow > 0 {
		// Copy down rather than reslice, which would keep the whole backing array alive.
		s.events = append(s.events[:0], s.events[overflow:]...)
		s.evicted += int64(overflow)
	}

	return added
}

// holdStandings folds the pushed table into the held one and writes the held one back into the
// snapshot. The revision decides what nil means: same revision is silence (keep the table), a
// new revision with nil means the table was cleared. A table on the same revision is a heartbeat.
func (s *Store) holdStandings(snapshot *telemetry.Snapshot) {
	switch {
	case snapshot.StandingsRevision != s.standingsRev:
		s.standingsRev = snapshot.StandingsRevision
		s.standings = snapshot.Standings

	case snapshot.Standings != nil:
		s.standings = snapshot.Standings
	}

	snapshot.Standings = s.standings
	snapshot.StandingsRevision = s.standingsRev
}

// Latest is the last snapshot pushed, and whether there has been one.
func (s *Store) Latest() (telemetry.Snapshot, bool) {
	s.mu.RLock()
	defer s.mu.RUnlock()

	return s.latest, s.haveState
}

// EventsAfter is every kept event with a sequence above after, oldest first. Zero returns the whole
// window.
func (s *Store) EventsAfter(after int64) []telemetry.Event {
	s.mu.RLock()
	defer s.mu.RUnlock()

	// A copy: the caller reads it outside the lock.
	tail := make([]telemetry.Event, 0, len(s.events))
	for _, event := range s.events {
		if event.Sequence > after {
			tail = append(tail, event)
		}
	}

	return tail
}

// Evicted is how many events have fallen off the end of the window since startup.
func (s *Store) Evicted() int64 {
	s.mu.RLock()
	defer s.mu.RUnlock()

	return s.evicted
}

// LastSequence is the highest audit sequence the store holds, which is the cursor a client
// resumes from.
func (s *Store) LastSequence() int64 {
	s.mu.RLock()
	defer s.mu.RUnlock()

	return s.lastSeq
}
