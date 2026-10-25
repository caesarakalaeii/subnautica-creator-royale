package caster

import (
	"math"
	"net"
	"net/http"
	"strings"
	"sync"
	"time"
)

// burstWindow sets a bucket's burst as its rate times this, so the mod's catch-up resends after a
// network hiccup are not refused.
const burstWindow = 2 * time.Second

// limiter is a token bucket with an injectable clock. A nil *limiter allows everything, which is
// what an unconfigured rate means.
type limiter struct {
	mu sync.Mutex

	perSecond float64
	burst     float64
	tokens    float64
	last      time.Time
	now       func() time.Time

	// refused is how many have been turned away since the last drain, so a flood is logged once
	// when it starts and once with the count when it ends.
	refused int64
}

// newLimiter is a full bucket, or nil if either bound is off.
func newLimiter(perSecond, burst float64, now func() time.Time) *limiter {
	if perSecond <= 0 || burst <= 0 {
		return nil
	}

	// At least one whole token, or a rate below 0.5/s would refuse everything forever.
	burst = math.Max(1, burst)

	return &limiter{
		perSecond: perSecond,
		burst:     burst,
		tokens:    burst,
		last:      now(),
		now:       now,
	}
}

// allow takes a token if there is one, and counts the refusal if there is not.
func (l *limiter) allow() bool {
	if l == nil {
		return true
	}

	l.mu.Lock()
	defer l.mu.Unlock()

	// Refill only forward, so a wall clock stepped backwards cannot take tokens away.
	if now := l.now(); now.After(l.last) {
		l.tokens = math.Min(l.burst, l.tokens+now.Sub(l.last).Seconds()*l.perSecond)
		l.last = now
	}

	if l.tokens < 1 {
		l.refused++
		return false
	}

	l.tokens--

	return true
}

// refusals is how many have been turned away since the last drain, without draining. One is the
// first refusal of an episode, which is the line worth logging.
func (l *limiter) refusals() int64 {
	if l == nil {
		return 0
	}

	l.mu.Lock()
	defer l.mu.Unlock()

	return l.refused
}

// drainRefusals is the count, and resets it so the next episode reports its own first refusal.
func (l *limiter) drainRefusals() int64 {
	if l == nil {
		return 0
	}

	l.mu.Lock()
	defer l.mu.Unlock()

	refused := l.refused
	l.refused = 0

	return refused
}

// full is whether this bucket is back to its burst. A full bucket behaves like no bucket, so
// perSource may forget it.
func (l *limiter) full() bool {
	if l == nil {
		return true
	}

	l.mu.Lock()
	defer l.mu.Unlock()

	if now := l.now(); now.After(l.last) {
		l.tokens = math.Min(l.burst, l.tokens+now.Sub(l.last).Seconds()*l.perSecond)
		l.last = now
	}

	return l.tokens >= l.burst
}

// perSource is one token bucket per source address, so one caller cannot exhaust booth creation
// for everybody. The map is keyed on caller-controlled input, so it is bounded: full buckets are
// swept when the map reaches its cap, and if nothing frees up, new sources are refused.
type perSource struct {
	mu sync.Mutex

	buckets map[string]*limiter

	perSecond float64
	burst     float64
	sources   int
	now       func() time.Time
}

// maxSources is how many addresses perSource will hold buckets for at once: well under a megabyte,
// above what real callers reach.
const maxSources = 10000

// newPerSource is a set of buckets at this rate, or nil if either bound is off. A nil *perSource
// allows everything.
func newPerSource(perSecond, burst float64, now func() time.Time) *perSource {
	if perSecond <= 0 || burst <= 0 {
		return nil
	}

	return &perSource{
		buckets:   map[string]*limiter{},
		perSecond: perSecond,
		burst:     burst,
		sources:   maxSources,
		now:       now,
	}
}

// allow takes a token from this source's bucket, making one if the source is new. It also says
// whether this refusal is the first of that source's episode, the only one worth logging.
func (p *perSource) allow(source string) (allowed, firstRefusal bool) {
	if p == nil {
		return true, false
	}

	p.mu.Lock()
	defer p.mu.Unlock()

	bucket, held := p.buckets[source]
	if !held {
		// Sweep only when full, so a flood does not pay a full map walk per request.
		if len(p.buckets) >= p.sources {
			p.forgetFullLocked()
		}

		if len(p.buckets) >= p.sources {
			// Nothing freed: refuse rather than grow the map. Not a first refusal, since there is
			// no bucket to count it in and it would log on every request.
			return false, false
		}

		bucket = newLimiter(p.perSecond, p.burst, p.now)
		p.buckets[source] = bucket
	}

	if bucket.allow() {
		return true, false
	}

	return false, bucket.refusals() == 1
}

// forgetFullLocked drops every bucket that has refilled. The caller holds the lock.
func (p *perSource) forgetFullLocked() {
	for source, bucket := range p.buckets {
		if bucket.full() {
			delete(p.buckets, source)
		}
	}
}

// tracking is how many sources have buckets, for the tests that state the map is bounded.
func (p *perSource) tracking() int {
	if p == nil {
		return 0
	}

	p.mu.Lock()
	defer p.mu.Unlock()

	return len(p.buckets)
}

// sourceOf is the address a request is rate-limited against, without the port (which changes per
// connection). With header set, its rightmost entry is used: that is the one the trusted proxy
// appended, while entries to its left are caller-controlled. Otherwise the connection address.
func sourceOf(r *http.Request, header string) string {
	if header != "" {
		if forwarded := r.Header.Get(header); forwarded != "" {
			hops := strings.Split(forwarded, ",")

			if nearest := strings.TrimSpace(hops[len(hops)-1]); nearest != "" {
				return withoutPort(nearest)
			}
		}
		// Absent or empty header: fall back to the connection address, not a shared empty bucket.
	}

	return withoutPort(r.RemoteAddr)
}

// withoutPort is an address with any port taken off, and the address unchanged when it has none.
func withoutPort(address string) string {
	if host, _, err := net.SplitHostPort(address); err == nil {
		return host
	}

	return address
}
