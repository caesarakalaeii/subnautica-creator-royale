// Package soak drives a booth under sustained load: a pump pushing telemetry at the mod's cadence
// and a set of dashboards and overlays reading it back. It looks for memory that grows and clients
// that silently stop receiving. The same code runs both long soaks and short test runs.
package soak

import (
	"bytes"
	"context"
	"encoding/json"
	"fmt"
	"io"
	"log/slog"
	"net/http"
	"net/http/httptest"
	"runtime"
	"strings"
	"sync"
	"sync/atomic"
	"time"

	"github.com/coder/websocket"

	"github.com/caesarakalaeii/subnautica-creator-royale/backend/internal/caster"
	"github.com/caesarakalaeii/subnautica-creator-royale/backend/internal/config"
	"github.com/caesarakalaeii/subnautica-creator-royale/backend/internal/telemetry"
)

// The tokens the in-process booth is built with. Not secrets: that booth is reachable only from this
// process, and authentication stays on so the soak exercises the real service.
const (
	soakIngestToken = "soak-ingest-token"
	soakViewToken   = "soak-view-token"
)

// goroutineSlack is how much drift is allowed between the steady-state goroutine count and the
// count at the end. A real leak is one goroutine per connection or push, which grows far past this.
const goroutineSlack = 8

// stallFactor is how many push intervals a client may go without a frame before it is called
// stalled. Generous, so a loaded machine does not cause false failures.
const stallFactor = 20

// readLimit bounds one message a client will accept. The library default of 32 KiB is too small:
// `/live`'s first message is the whole event window, which grows to about 150 KB on a long run.
// Same value as `cmd/record`.
const readLimit = 8 << 20

// suspendFloor is how much wall-clock drift counts as this host having slept rather than as
// ordinary scheduling jitter.
const suspendFloor = 5 * time.Second

// settleTimeout bounds the wait for the feed to start flowing before the goroutine baseline is
// taken.
const settleTimeout = 10 * time.Second

// Reconnect backoff for a lost feed: half a second first, matching `feed.js` (a restarted booth is
// usually back within a second), then at most five seconds.
const (
	firstRetry   = 500 * time.Millisecond
	longestRetry = 5 * time.Second
	dialTimeout  = 10 * time.Second
)

// ending is why one session with the feed stopped.
type ending int

const (
	// endedWithRun is the soak finishing. Not a loss, and not something to reconnect after.
	endedWithRun ending = iota
	// endedRefused is a close carrying 1013: the booth is at capacity.
	endedRefused
	// endedLost is anything else, such as a transient network blip.
	endedLost
)

// Options is one soak run.
type Options struct {
	// Duration is how long to push for.
	Duration time.Duration

	// Rate is pushes per second. The mod's own cadence is about one.
	Rate float64

	// Clients is how many dashboards and overlays to hold open.
	Clients int

	// Report is how often Observe is called.
	Report time.Duration

	// Fixture is one telemetry document, normally the canonical example. Each push is that document
	// with its events renumbered.
	Fixture []byte

	// URL is an already running booth to soak. Empty starts one in-process.
	URL string

	// IngestToken and ViewToken are needed only when URL is set.
	IngestToken string
	ViewToken   string

	// IngestRate and MaxLiveClients configure the in-process booth. Zero means the service's own
	// default, not unlimited; to soak an unbounded booth, pass a number larger than the load.
	IngestRate     float64
	MaxLiveClients int

	// Observe is called at every reporting interval, and once at the end.
	Observe func(Report)
}

// Report is what a soak saw. Every field is cumulative since the run started.
type Report struct {
	Elapsed time.Duration

	// The pump's view.
	Pushes      int64
	Accepted    int64
	RateLimited int64
	Failed      int64

	// The clients' view. Frames counts every message every client received, replay included.
	Frames         int64
	Connected      int
	RefusedClients int64
	DroppedClients int64
	StalledClients int64

	// Reconnects is how many times a client lost the feed and got it back. Nonzero means the path
	// flapped; the run kept measuring through it.
	Reconnects int64

	// HostSlept is how long this machine was suspended during the run, since a suspend otherwise
	// looks like a network fault.
	HostSlept time.Duration

	// The booth's own numbers, available only for an in-process run. EventsEvicted is how much of the
	// feed has rolled off the store's window; a long run should evict some to exercise that bound.
	SlowDrops     int64
	LastSequence  int64
	EventsEvicted int64

	// This process. For an in-process run that includes the booth; against a remote booth these
	// describe only the load generator.
	Goroutines    int
	HeapBytes     uint64
	PeakHeapBytes uint64

	// Problems is empty on a clean run. Anything in it is a reason not to believe the rest.
	Problems []string
}

// String is one line, for a log or a terminal.
func (r Report) String() string {
	// Only shown when nonzero, so it stands out.
	slept := ""
	if r.HostSlept > 0 {
		slept = fmt.Sprintf("  SLEPT=%s", r.HostSlept)
	}

	return fmt.Sprintf(
		"%s  pushes=%d accepted=%d limited=%d failed=%d  frames=%d clients=%d refused=%d down=%d stalled=%d  reconnects=%d slow_drops=%d last_seq=%d evicted=%d  goroutines=%d heap=%.1fMiB peak=%.1fMiB",
		r.Elapsed.Round(time.Second), r.Pushes, r.Accepted, r.RateLimited, r.Failed,
		r.Frames, r.Connected, r.RefusedClients, r.DroppedClients, r.StalledClients, r.Reconnects,
		r.SlowDrops, r.LastSequence, r.EventsEvicted,
		r.Goroutines, float64(r.HeapBytes)/(1<<20), float64(r.PeakHeapBytes)/(1<<20)) + slept
}

// slept is how long the host was suspended. Go's monotonic clock stops during suspend while the
// wall clock does not, so their difference is the time this process was frozen.
func slept(wall, monotonic time.Duration) time.Duration {
	gap := wall - monotonic
	if gap < suspendFloor {
		return 0
	}

	return gap.Round(time.Second)
}

// client is one reader: an overlay or a dashboard.
type client struct {
	frames atomic.Int64

	// lastFrame is when the most recent message arrived, in Unix nanoseconds.
	lastFrame atomic.Int64

	// refused and down are current state, not history. sessions counts how many times this client has
	// connected; anything past the first is a reconnection.
	refused atomic.Bool
	down    atomic.Bool

	sessions atomic.Int64
}

// run is the mutable state of one soak.
type run struct {
	opts Options

	startedWall time.Time

	pushes      atomic.Int64
	accepted    atomic.Int64
	rateLimited atomic.Int64
	failed      atomic.Int64
	reconnects  atomic.Int64

	peakHeap atomic.Uint64

	// steadyGoroutines is the count at the most recent reporting tick, with everything up. The leak
	// check compares against it, because a count at teardown includes goroutines still unwinding.
	steadyGoroutines atomic.Int64

	clients []*client

	// booth is the in-process service, or nil when soaking a URL.
	booth *caster.Server

	// firstFailure is the first error seen; later ones are only counted.
	firstFailure atomic.Value
}

// Run drives the booth for opts.Duration and returns what it saw. The error is for a run that could
// not happen at all; a run that went badly returns a Report with Problems in it.
func Run(ctx context.Context, opts Options) (Report, error) {
	if opts.Rate <= 0 || opts.Clients < 0 || opts.Duration <= 0 {
		return Report{}, fmt.Errorf("soak: a run needs a positive duration and rate, got %v at %v/s",
			opts.Duration, opts.Rate)
	}

	if opts.Report <= 0 {
		opts.Report = opts.Duration
	}

	frame, err := telemetry.Parse(opts.Fixture)
	if err != nil {
		return Report{}, fmt.Errorf("soak: the fixture is not the contract: %w", err)
	}

	state := &run{opts: opts, clients: make([]*client, 0, opts.Clients)}

	base, ingestToken, viewToken, stop, err := state.target(opts)
	if err != nil {
		return Report{}, err
	}
	defer stop()

	// One deadline for the whole run, so pump, readers and reporter stop together.
	ctx, cancel := context.WithTimeout(ctx, opts.Duration)
	defer cancel()

	var readers sync.WaitGroup
	state.connect(ctx, &readers, base, viewToken)

	started := time.Now()
	// The same instant without its monotonic reading, so the two clocks can be compared later.
	state.startedWall = started.Round(0)

	var pump sync.WaitGroup
	pump.Add(1)
	go func() {
		defer pump.Done()
		state.push(ctx, base, ingestToken, frame)
	}()

	// Take the baseline once the feed is flowing: an accepted socket does not yet have all its
	// goroutines, and counting earlier makes startup look like a leak.
	baselineGoroutines := state.settle(ctx)

	// Report while pumping, so a long run can be watched and stopped early.
	var reporter sync.WaitGroup
	reporter.Add(1)
	go func() {
		defer reporter.Done()
		state.report(ctx, started, baselineGoroutines)
	}()

	pump.Wait()

	// The last steady-state count, or this one if the run was too short to have had a tick. Taken
	// before teardown, which would otherwise skew it.
	final := int(state.steadyGoroutines.Load())
	if final == 0 {
		final = runtime.NumGoroutine()
	}

	cancel()
	reporter.Wait()
	readers.Wait()

	report := state.snapshot(time.Since(started), baselineGoroutines, final)
	if opts.Observe != nil {
		opts.Observe(report)
	}

	return report, nil
}

// target returns the base URL to drive, the tokens to present, and how to shut it down.
func (s *run) target(opts Options) (base, ingest, view string, stop func(), err error) {
	if opts.URL != "" {
		if opts.IngestToken == "" || opts.ViewToken == "" {
			return "", "", "", nil, fmt.Errorf("soak: driving %s needs both tokens", opts.URL)
		}

		return strings.TrimSuffix(opts.URL, "/"), opts.IngestToken, opts.ViewToken, func() {}, nil
	}

	// The deployed defaults, then the caller's overrides, so the soak tests a realistic config.
	cfg, err := config.Load(func(string) string { return "" })
	if err != nil {
		return "", "", "", nil, fmt.Errorf("soak: the service's own defaults did not load: %w", err)
	}

	// The booth's tokens must be in the configuration; the service never reads fields set elsewhere.
	cfg.Booths = []config.Booth{{
		ID: config.TournamentBooth, IngestToken: soakIngestToken, ViewToken: soakViewToken,
	}}
	if opts.IngestRate > 0 {
		cfg.IngestRate = opts.IngestRate
	}
	if opts.MaxLiveClients > 0 {
		cfg.MaxLiveClients = opts.MaxLiveClients
	}

	// Errors only, so request logs do not bury the report.
	logger := slog.New(slog.NewTextHandler(io.Discard, &slog.HandlerOptions{Level: slog.LevelError}))

	s.booth = caster.NewServer(cfg, logger)
	server := httptest.NewServer(s.booth.Handler())

	return server.URL, soakIngestToken, soakViewToken, server.Close, nil
}

// connect starts one holder per reader, each of which keeps its own feed up for the whole run.
func (s *run) connect(ctx context.Context, readers *sync.WaitGroup, base, view string) {
	feed := "ws" + strings.TrimPrefix(base, "http") + "/live?token=" + view

	for range s.opts.Clients {
		reader := &client{}
		reader.lastFrame.Store(time.Now().UnixNano())
		reader.down.Store(true)
		s.clients = append(s.clients, reader)

		readers.Add(1)
		go func() {
			defer readers.Done()

			s.hold(ctx, feed, reader)
		}()
	}
}

// hold keeps one client's feed up until the run ends, dialling again whenever it goes away, as a
// real overlay does. Every reconnection is counted and reported as a problem.
func (s *run) hold(ctx context.Context, feed string, reader *client) {
	backoff := firstRetry

	for ctx.Err() == nil {
		// A separate dial context, so a dial at the end of a short run is refused, not cancelled.
		dialCtx, cancelDial := context.WithTimeout(ctx, dialTimeout)
		connection, _, err := websocket.Dial(dialCtx, feed, nil)
		cancelDial()

		if err != nil {
			if ctx.Err() != nil {
				return
			}

			// A failed dial means the booth is unreachable. A capacity refusal arrives as a close instead.
			reader.down.Store(true)
			s.note(fmt.Errorf("dialling the feed: %w", err))

			if !wait(ctx, backoff) {
				return
			}
			backoff = min(backoff*2, longestRetry)

			continue
		}

		connection.SetReadLimit(readLimit)
		if reader.sessions.Add(1) > 1 {
			s.reconnects.Add(1)
		}
		reader.down.Store(false)
		reader.refused.Store(false)
		backoff = firstRetry

		outcome := s.drain(ctx, connection, reader)
		connection.CloseNow()

		switch outcome {
		case endedWithRun:
			return
		case endedRefused:
			reader.refused.Store(true)
		case endedLost:
			reader.down.Store(true)
		}

		if !wait(ctx, backoff) {
			return
		}
		backoff = min(backoff*2, longestRetry)
	}
}

// wait sleeps unless the run ends first, and says which happened.
func wait(ctx context.Context, delay time.Duration) bool {
	timer := time.NewTimer(delay)
	defer timer.Stop()

	select {
	case <-ctx.Done():
		return false
	case <-timer.C:
		return true
	}
}

// drain reads one client's feed until the session ends, counting frames and returning why it
// stopped. Whether to reconnect is `hold`'s decision.
func (s *run) drain(ctx context.Context, connection *websocket.Conn, reader *client) ending {
	for {
		_, payload, err := connection.Read(ctx)
		if err != nil {
			if ctx.Err() != nil {
				return endedWithRun
			}

			if websocket.CloseStatus(err) == websocket.StatusTryAgainLater {
				return endedRefused
			}

			// Recorded, so a lost feed reports its reason.
			s.note(fmt.Errorf("reading the feed: %w", err))

			return endedLost
		}

		// Parsed as a client would, so an unparseable frame counts as a failure.
		var snapshot telemetry.Snapshot
		if err := json.Unmarshal(payload, &snapshot); err != nil || snapshot.Version != telemetry.Version {
			s.note(fmt.Errorf("the feed sent something a client could not read: %v", err))

			return endedLost
		}

		reader.frames.Add(1)
		reader.lastFrame.Store(time.Now().UnixNano())
	}
}

// push is the pump: the fixture, renumbered, at the configured cadence.
func (s *run) push(ctx context.Context, base, token string, frame telemetry.Snapshot) {
	interval := time.Duration(float64(time.Second) / s.opts.Rate)
	ticker := time.NewTicker(interval)
	defer ticker.Stop()

	client := &http.Client{Timeout: 30 * time.Second}
	url := base + "/ingest"

	for round := int64(0); ; round++ {
		select {
		case <-ctx.Done():
			return
		case <-ticker.C:
		}

		s.pushOnce(ctx, client, url, token, renumber(frame, round))
	}
}

// renumber advances the fixture's events past everything sent so far. Without it the store would
// treat every push after the first as a duplicate.
func renumber(frame telemetry.Snapshot, round int64) telemetry.Snapshot {
	events := make([]telemetry.Event, len(frame.Events))
	copy(events, frame.Events)

	offset := round * int64(len(events))
	for i := range events {
		events[i].Sequence = offset + int64(i) + 1
	}

	frame.Events = events
	frame.At = time.Now().UTC()

	return frame
}

// pushOnce is one POST to /ingest, counted by what came back.
func (s *run) pushOnce(ctx context.Context, client *http.Client, url, token string, frame telemetry.Snapshot) {
	payload, err := json.Marshal(telemetry.ForSending(frame))
	if err != nil {
		s.failed.Add(1)
		s.note(fmt.Errorf("encoding a push: %w", err))

		return
	}

	request, err := http.NewRequestWithContext(ctx, http.MethodPost, url, bytes.NewReader(payload))
	if err != nil {
		s.failed.Add(1)
		s.note(fmt.Errorf("building a push: %w", err))

		return
	}

	request.Header.Set("Authorization", "Bearer "+token)
	request.Header.Set("Content-Type", "application/json")

	s.pushes.Add(1)

	response, err := client.Do(request)
	if err != nil {
		if ctx.Err() != nil {
			// The run ended mid-push. Not a failure of the service.
			s.pushes.Add(-1)

			return
		}

		s.failed.Add(1)
		s.note(fmt.Errorf("pushing: %w", err))

		return
	}

	defer func() {
		// Drain and close the body so the transport can reuse the connection.
		_, _ = io.Copy(io.Discard, response.Body)
		_ = response.Body.Close()
	}()

	switch response.StatusCode {
	case http.StatusAccepted:
		s.accepted.Add(1)
	case http.StatusTooManyRequests:
		s.rateLimited.Add(1)
	default:
		s.failed.Add(1)
		s.note(fmt.Errorf("the booth answered a push with %s", response.Status))
	}
}

// settle waits until every connected client has had a frame and returns the goroutine count there,
// the baseline a leak is measured against. It gives up after settleTimeout and counts anyway.
func (s *run) settle(ctx context.Context) int {
	deadline := time.Now().Add(settleTimeout)

	for time.Now().Before(deadline) {
		if ctx.Err() != nil {
			break
		}

		if s.flowing() {
			break
		}

		select {
		case <-ctx.Done():
		case <-time.After(5 * time.Millisecond):
		}
	}

	s.sample()

	return runtime.NumGoroutine()
}

// flowing is whether every client that connected has received something.
func (s *run) flowing() bool {
	if s.accepted.Load() == 0 {
		return false
	}

	// At least one client has to be up with a frame. Clients dial in their own goroutines, so early on
	// all of them are legitimately down, which must not count as settled.
	flowing := 0
	for _, reader := range s.clients {
		if reader.refused.Load() {
			continue
		}
		if reader.down.Load() || reader.frames.Load() == 0 {
			return false
		}
		flowing++
	}

	return flowing > 0
}

// report prints on a tick until the run ends.
func (s *run) report(ctx context.Context, started time.Time, baseline int) {
	ticker := time.NewTicker(s.opts.Report)
	defer ticker.Stop()

	for {
		select {
		case <-ctx.Done():
			return
		case <-ticker.C:
			goroutines := runtime.NumGoroutine()
			s.steadyGoroutines.Store(int64(goroutines))

			if s.opts.Observe != nil {
				s.opts.Observe(s.snapshot(time.Since(started), baseline, goroutines))
			}
		}
	}
}

// sample reads the heap, updates the high-water mark, and returns what it read. Every reading goes
// through here so the peak is never below a reported value. ReadMemStats stops the world, so this
// runs on the reporting tick, not per push.
func (s *run) sample() uint64 {
	var stats runtime.MemStats
	runtime.ReadMemStats(&stats)

	for {
		peak := s.peakHeap.Load()
		if stats.HeapAlloc <= peak {
			break
		}

		if s.peakHeap.CompareAndSwap(peak, stats.HeapAlloc) {
			break
		}
	}

	return stats.HeapAlloc
}

// note keeps the first thing that went wrong.
func (s *run) note(err error) {
	s.firstFailure.CompareAndSwap(nil, err.Error())
}

// snapshot builds a Report and judges the run.
func (s *run) snapshot(elapsed time.Duration, baseline, goroutines int) Report {
	heap := s.sample()

	report := Report{
		Elapsed:       elapsed,
		Pushes:        s.pushes.Load(),
		Accepted:      s.accepted.Load(),
		RateLimited:   s.rateLimited.Load(),
		Failed:        s.failed.Load(),
		Goroutines:    goroutines,
		Reconnects:    s.reconnects.Load(),
		HostSlept:     slept(time.Now().Round(0).Sub(s.startedWall), elapsed),
		HeapBytes:     heap,
		PeakHeapBytes: s.peakHeap.Load(),
	}

	stallAfter := time.Duration(float64(time.Second)/s.opts.Rate) * stallFactor
	oldest := time.Now().Add(-stallAfter).UnixNano()

	for _, reader := range s.clients {
		report.Frames += reader.frames.Load()

		switch {
		case reader.refused.Load():
			report.RefusedClients++
		case reader.down.Load():
			report.DroppedClients++
		default:
			report.Connected++

			if reader.lastFrame.Load() < oldest && report.Accepted > 0 {
				report.StalledClients++
			}
		}
	}

	if s.booth != nil {
		report.LastSequence = s.booth.Store().LastSequence()
		report.SlowDrops = s.booth.Hub().SlowDrops()
		report.EventsEvicted = s.booth.Store().Evicted()
	}

	report.Problems = problems(report, baseline, s.firstFailure.Load())

	return report
}

// problems judges a run, in one place so long soaks and tests agree on what a clean run is.
func problems(report Report, baseline int, firstFailure any) []string {
	var found []string

	if report.Failed > 0 {
		found = append(found, fmt.Sprintf("%d pushes failed", report.Failed))
	}

	if report.RateLimited > 0 {
		found = append(found, fmt.Sprintf(
			"%d pushes were rate limited: the load is above the booth's LMS_INGEST_RATE, so this run is not soaking what it thinks it is",
			report.RateLimited))
	}

	if report.RefusedClients > 0 {
		found = append(found, fmt.Sprintf(
			"%d clients were refused at capacity, so the load is lighter than it was asked to be",
			report.RefusedClients))
	}

	if report.DroppedClients > 0 {
		found = append(found, fmt.Sprintf(
			"%d clients are not connected and are still trying", report.DroppedClients))
	}

	if report.HostSlept > 0 {
		// Listed first: it explains the problems most likely printed beside it.
		found = append(found, fmt.Sprintf(
			"this host slept for %s during the run, so reconnects and stalls around it are this machine and not the booth",
			report.HostSlept))
	}

	if report.Reconnects > 0 {
		// Reconnecting keeps the run measuring but does not make an unstable feed a clean result.
		found = append(found, fmt.Sprintf(
			"the feed was lost and re-established %d times, so something in the path is not steady",
			report.Reconnects))
	}

	if report.StalledClients > 0 {
		found = append(found, fmt.Sprintf(
			"%d clients are still connected but have received nothing for a long time, which is the wedged-overlay failure",
			report.StalledClients))
	}

	if report.SlowDrops > 0 {
		found = append(found, fmt.Sprintf(
			"the booth dropped %d clients for falling behind, so its %d-message buffer was not enough",
			report.SlowDrops, caster.SubscriberBuffer))
	}

	if grown := report.Goroutines - baseline; grown > goroutineSlack {
		found = append(found, fmt.Sprintf(
			"goroutines grew from %d to %d, which over a long run is a leak per connection or per push",
			baseline, report.Goroutines))
	}

	if first, ok := firstFailure.(string); ok && first != "" {
		found = append(found, "first failure: "+first)
	}

	return found
}
