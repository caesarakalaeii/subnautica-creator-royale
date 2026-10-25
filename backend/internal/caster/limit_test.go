package caster

import (
	"strconv"
	"sync"
	"testing"
	"time"
)

// clock is a manually advanced clock, so limiter tests do not depend on machine load.
type clock struct {
	mu sync.Mutex
	at time.Time
}

func newClock() *clock {
	// Not the zero time, which would hide a bug in the first call's elapsed-time arithmetic.
	return &clock{at: time.Date(2026, 10, 24, 12, 0, 0, 0, time.UTC)}
}

func (c *clock) now() time.Time {
	c.mu.Lock()
	defer c.mu.Unlock()

	return c.at
}

func (c *clock) advance(d time.Duration) {
	c.mu.Lock()
	defer c.mu.Unlock()

	c.at = c.at.Add(d)
}

func TestABucketAllowsItsBurstAtOnceAndThenRefuses(t *testing.T) {
	t.Parallel()

	tick := newClock()
	limit := newLimiter(10, 3, tick.now)

	for attempt := 1; attempt <= 3; attempt++ {
		if !limit.allow() {
			t.Fatalf("attempt %d was refused, want the whole burst of 3 allowed at once", attempt)
		}
	}

	if limit.allow() {
		t.Error("a fourth attempt in the same instant was allowed, want it refused")
	}
}

func TestABucketRefillsAtItsRate(t *testing.T) {
	t.Parallel()

	tick := newClock()
	limit := newLimiter(10, 2, tick.now)

	limit.allow()
	limit.allow()

	if limit.allow() {
		t.Fatal("the bucket was not empty after its burst")
	}

	// A tenth of a second is exactly one token at ten a second.
	tick.advance(100 * time.Millisecond)

	if !limit.allow() {
		t.Error("no token after 100ms at 10/s, want exactly one")
	}
	if limit.allow() {
		t.Error("two tokens after 100ms at 10/s, want exactly one")
	}
}

func TestAnIdleBucketDoesNotBankMoreThanItsBurst(t *testing.T) {
	t.Parallel()

	tick := newClock()
	limit := newLimiter(10, 3, tick.now)

	tick.advance(time.Hour)

	allowed := 0
	for attempt := 0; attempt < 100; attempt++ {
		if limit.allow() {
			allowed++
		}
	}

	if allowed != 3 {
		t.Errorf("an hour of idling allowed %d in a row, want the burst of 3", allowed)
	}
}

func TestRefusalsAreCountedAndTheFirstOneIsTellable(t *testing.T) {
	t.Parallel()

	tick := newClock()
	limit := newLimiter(10, 1, tick.now)

	limit.allow()

	if limit.allow() {
		t.Fatal("the bucket was not empty")
	}
	if limit.refusals() != 1 {
		t.Errorf("refusals() = %d after the first refusal, want 1 so it can be logged once", limit.refusals())
	}

	limit.allow()
	limit.allow()

	if limit.refusals() != 3 {
		t.Errorf("refusals() = %d, want 3", limit.refusals())
	}

	if drained := limit.drainRefusals(); drained != 3 {
		t.Errorf("drainRefusals() = %d, want 3", drained)
	}
	if limit.refusals() != 0 {
		t.Errorf("refusals() = %d after draining, want 0 so the next episode logs its own first", limit.refusals())
	}
}

func TestARateOfZeroIsNoLimitAtAll(t *testing.T) {
	t.Parallel()

	limit := newLimiter(0, 0, newClock().now)

	for attempt := 0; attempt < 1000; attempt++ {
		if !limit.allow() {
			t.Fatalf("attempt %d was refused by an unconfigured limiter", attempt)
		}
	}

	if limit.refusals() != 0 || limit.drainRefusals() != 0 {
		t.Error("an unconfigured limiter counted a refusal")
	}
}

// TestABucketIsSafeToShareBetweenRequests is only meaningful under -race.
func TestABucketIsSafeToShareBetweenRequests(t *testing.T) {
	t.Parallel()

	tick := newClock()
	limit := newLimiter(1000, 50, tick.now)

	var wg sync.WaitGroup
	for worker := 0; worker < 16; worker++ {
		wg.Add(1)
		go func() {
			defer wg.Done()
			for attempt := 0; attempt < 100; attempt++ {
				if !limit.allow() {
					limit.refusals()
				}
			}
		}()
	}
	wg.Wait()

	// 1600 attempts in one instant against a burst of 50: admitted plus refused must add up.
	if allowed := 1600 - limit.refusals(); allowed != 50 {
		t.Errorf("allowed %d of 1600 against a burst of 50", allowed)
	}
}

func TestARateBelowOneASecondStillLetsSomethingThrough(t *testing.T) {
	t.Parallel()

	tick := newClock()
	limit := newLimiter(0.4, 0.4*burstWindow.Seconds(), tick.now)

	if !limit.allow() {
		t.Fatal("the first push was refused by a full bucket")
	}
	if limit.allow() {
		t.Fatal("a second push in the same instant was allowed")
	}

	// One token at four tenths a second.
	tick.advance(2500 * time.Millisecond)

	if !limit.allow() {
		t.Error("no token after 2.5s at 0.4/s, want one")
	}
}

// TestASetOfBucketsStaysBoundedHoweverManyAddressesAsk asserts the bound and the recovery, not
// when the sweep runs.
func TestASetOfBucketsStaysBoundedHoweverManyAddressesAsk(t *testing.T) {
	t.Parallel()

	tick := newClock()
	buckets := newPerSource(1, 2, tick.now)
	buckets.sources = 100

	// Twenty times the bound, each address asking once.
	for source := range 2000 {
		buckets.allow("198.51.100." + strconv.Itoa(source))
	}

	if held := buckets.tracking(); held > buckets.sources {
		t.Errorf("%d sources are tracked after 2000 addresses asked, want at most %d",
			held, buckets.sources)
	}

	if buckets.tracking() == 0 {
		t.Fatal("nothing is tracked at all, so this test would pass against a limiter that does nothing")
	}

	// Once they have all gone quiet the slots come back.
	tick.advance(time.Hour)

	for source := range buckets.sources {
		if allowed, _ := buckets.allow("203.0.113." + strconv.Itoa(source)); !allowed {
			t.Fatalf("source %d was refused an hour after the flood stopped", source)
		}
	}
}

func TestASetOfBucketsRefusesANewSourceRatherThanGrowWithoutBound(t *testing.T) {
	t.Parallel()

	tick := newClock()
	buckets := newPerSource(1, 1, tick.now)
	buckets.sources = 100

	// Every one of these spends its bucket, so none of them is forgettable.
	for source := range buckets.sources {
		if allowed, _ := buckets.allow("198.51.100." + strconv.Itoa(source)); !allowed {
			t.Fatalf("source %d was refused before the bound was reached", source)
		}
	}

	if admitted, _ := buckets.allow("203.0.113.1"); admitted {
		t.Error("a new source was admitted past the bound, so the map grows with the flood")
	}

	if held := buckets.tracking(); held > buckets.sources {
		t.Errorf("%d sources are tracked, want at most %d", held, buckets.sources)
	}
}

func TestOneSourceEmptyingItsBucketLeavesAnothersFull(t *testing.T) {
	t.Parallel()

	tick := newClock()
	buckets := newPerSource(1, 2, tick.now)

	for range 50 {
		buckets.allow("198.51.100.7")
	}

	if allowed, _ := buckets.allow("203.0.113.9"); !allowed {
		t.Error("a source that had asked for nothing was refused because another had flooded")
	}
}
