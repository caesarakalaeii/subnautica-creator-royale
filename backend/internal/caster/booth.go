package caster

import (
	"net/http"
	"sync"
	"time"

	"github.com/caesarakalaeii/subnautica-creator-royale/backend/internal/config"
)

// boothPrefix is where a booth's routes live: /b/{booth}/ingest and so on. A path segment rather
// than a host per booth, so no wildcard DNS or per-booth certificate is needed.
const boothPrefix = "/b/"

// boothParam is what the mux calls the wildcard in that prefix.
const boothParam = "booth"

// Booth is one tenant: its own match state, its own fan-out, its own console, and the tokens that
// address it. Nothing in it is shared between booths.
type Booth struct {
	id string

	// The three credentials, held as digests rather than the tokens themselves.
	ingest, view, operator credential

	store *Store
	hub   *Hub

	// console is what the mod reports about itself, including who is connected.
	console *console

	// cameras is the control channels a caster drives the solid map on.
	cameras *cameras

	// expiresAt is when a created booth stops answering, or zero for a configured one. Checked on
	// every request, not only by the reaper, so a stuck reaper cannot extend a booth's life.
	expiresAt time.Time

	// published is whether this booth's bracket answers with no token at all. Set only from
	// configuration, so a created booth is never published.
	published bool
}

// expired is whether this booth's lifetime is over. An expired booth is refused exactly like an
// unknown one, so callers cannot learn which ids ever existed.
func (b *Booth) expired(now time.Time) bool {
	return b != nil && !b.expiresAt.IsZero() && !now.Before(b.expiresAt)
}

// isPublic is whether this booth has published its bracket. Nil-safe: an unknown booth has not.
func (b *Booth) isPublic() bool {
	return b != nil && b.published
}

// ID is what this booth is called, in a URL and in every database row it owns.
func (b *Booth) ID() string {
	return b.id
}

// Store is this booth's match state, for the fan-out and the dashboard to read. Nil-safe.
func (b *Booth) Store() *Store {
	if b == nil {
		return nil
	}

	return b.store
}

// Hub is this booth's fan-out, exposed for reading its drop counts. Nil-safe.
func (b *Booth) Hub() *Hub {
	if b == nil {
		return nil
	}

	return b.hub
}

// booths is every tenant this process serves. Booths are added and dropped at runtime; the lock
// covers only the map and order, since each Booth's parts carry their own locks.
type booths struct {
	mu   sync.RWMutex
	byID map[string]*Booth

	// order is the booths in the order they were added, for stable startup logging.
	order []*Booth
}

func newBooths(cfg config.Config) *booths {
	configured := cfg.Booths
	if len(configured) == 0 {
		// A Config built as a literal rather than by config.Load: serve the reserved booth with no
		// tokens, as an empty environment would, so routes report "not configured".
		configured = []config.Booth{{ID: config.TournamentBooth}}
	}

	held := &booths{byID: make(map[string]*Booth, len(configured))}

	for _, booth := range configured {
		held.add(&Booth{
			id:        booth.ID,
			ingest:    hashToken(booth.IngestToken),
			view:      hashToken(booth.ViewToken),
			operator:  hashToken(booth.OperatorToken),
			store:     NewStore(),
			hub:       NewHub(cfg.MaxLiveClients),
			console:   &console{},
			cameras:   newCameras(),
			published: booth.Published,
		})
	}

	return held
}

func (b *booths) add(booth *Booth) {
	b.mu.Lock()
	defer b.mu.Unlock()

	b.byID[booth.ID()] = booth
	b.order = append(b.order, booth)
}

// addIfRoom takes a booth only if the process is holding fewer than maximum unexpired ones, and
// says whether it did. Zero is no ceiling. Count and insert share one lock so concurrent creates
// cannot overshoot the ceiling.
func (b *booths) addIfRoom(booth *Booth, maximum int, now time.Time) bool {
	b.mu.Lock()
	defer b.mu.Unlock()

	if maximum > 0 && b.countLiveHeld(now) >= maximum {
		return false
	}

	b.byID[booth.ID()] = booth
	b.order = append(b.order, booth)

	return true
}

// countLive is how many booths this process is holding that have not expired.
func (b *booths) countLive(now time.Time) int {
	b.mu.RLock()
	defer b.mu.RUnlock()

	return b.countLiveHeld(now)
}

// countLiveHeld is countLive with the lock already held by the caller. Expired booths are not
// counted: they already refuse everything and the reaper frees them shortly.
func (b *booths) countLiveHeld(now time.Time) int {
	living := 0

	for _, booth := range b.byID {
		if !booth.expired(now) {
			living++
		}
	}

	return living
}

// drop forgets one booth and everything it was holding in memory, so memory is bounded by
// concurrent booths rather than every booth ever created.
func (b *booths) drop(id string) {
	b.mu.Lock()
	defer b.mu.Unlock()

	if _, held := b.byID[id]; !held {
		return
	}

	delete(b.byID, id)

	kept := b.order[:0]
	for _, booth := range b.order {
		if booth.ID() != id {
			kept = append(kept, booth)
		}
	}
	b.order = kept
}

// find is the booth an id names, or nil.
func (b *booths) find(id string) *Booth {
	b.mu.RLock()
	defer b.mu.RUnlock()

	return b.byID[id]
}

// all is a copy of every booth this process is holding, in the order they were added. A copy
// because drop reuses `order` in place.
func (b *booths) all() []*Booth {
	b.mu.RLock()
	defer b.mu.RUnlock()

	return append([]*Booth{}, b.order...)
}

// asked is the booth a request's path names, or nil when it names none or an expired one. The path
// only selects the booth; each handler's token check authorises it. Expired and unknown booths
// both return nil and so get the same refusal.
func (b *booths) asked(r *http.Request, now time.Time) *Booth {
	id := r.PathValue(boothParam)
	if id == "" {
		// A bare path (/ingest rather than /b/ada/ingest) addresses the reserved tournament booth.
		id = config.TournamentBooth
	}

	booth := b.find(id)
	if booth.expired(now) {
		return nil
	}

	return booth
}

// mayPush is whether this request presented the booth's ingest token. Nil-safe, so an unknown
// booth is refused exactly like a bad token and booth ids cannot be enumerated.
func (b *Booth) mayPush(r *http.Request) bool {
	return b != nil && b.ingest.matches(bearerToken(r))
}

// mayView is whether this request presented the booth's view token, in a header or the query. The
// query is needed because browsers cannot set headers on a WebSocket handshake; logRequests omits
// it, but proxies may log it, which is why the view token is separate from the push token.
func (b *Booth) mayView(r *http.Request) bool {
	if b == nil {
		return false
	}

	return b.view.matches(bearerToken(r)) || b.view.matches(r.URL.Query().Get("token"))
}

// mayWrite is whether this request presented the booth's operator token.
func (b *Booth) mayWrite(r *http.Request) bool {
	return b != nil && b.operator.matches(bearerToken(r))
}

// mayDrive is whether a token offered on a WebSocket handshake is the booth's operator token. The
// camera channel carries it as a subprotocol (see driverToken): a handshake has no bearer header.
func (b *Booth) mayDrive(offered string) bool {
	return b != nil && b.operator.matches(offered)
}

// ingestConfigured, viewConfigured and writeConfigured are whether the booth has the token at all.
// Handlers return 503 only for a known booth without the token; an unknown booth gets the 401, so
// the 503 never reveals which ids exist.
func (b *Booth) ingestConfigured() bool {
	return b != nil && b.ingest.set
}

func (b *Booth) viewConfigured() bool {
	return b != nil && b.view.set
}

func (b *Booth) writeConfigured() bool {
	return b != nil && b.operator.set
}
