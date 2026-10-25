package caster

import "sync"

// SubscriberBuffer is how many pushes a client may fall behind before it is dropped. At about one
// push a second this is roughly sixteen seconds of grace for a backgrounded tab.
const SubscriberBuffer = 16

// Hub fans pushes out to every connected dashboard and overlay. Broadcast never blocks, because
// ingest runs on the path the mod waits on: a client that cannot keep up is disconnected and
// replays from its cursor on reconnect. At capacity the newcomer is refused; nobody is evicted.
type Hub struct {
	mu          sync.Mutex
	subscribers map[*subscriber]struct{}

	// maxSubscribers bounds how many may be connected at once. Zero is no bound.
	maxSubscribers int

	// slowDrops counts clients disconnected for falling behind.
	slowDrops int64

	// refused counts clients turned away at capacity.
	refused int64
}

// subscriber is one connected client.
type subscriber struct {
	messages chan []byte

	// dropSlow is called instead of blocking when messages is full. It closes the connection,
	// which unwinds the reader and the writer and takes the subscriber out of the hub.
	dropSlow func()

	// dropped is whether dropSlow has already run. The close is not instant, so later pushes find
	// the same full channel; this keeps one wedged client from being counted many times.
	dropped bool
}

// NewHub is an empty fan-out that will hold at most maxSubscribers clients. Zero is unlimited.
func NewHub(maxSubscribers int) *Hub {
	return &Hub{
		subscribers:    make(map[*subscriber]struct{}),
		maxSubscribers: maxSubscribers,
	}
}

// Subscribe adds a client and returns its channel, an unsubscribe func the caller must call when
// done, and whether there was room. On false there is nothing to clean up.
func (h *Hub) Subscribe(dropSlow func()) (<-chan []byte, func(), bool) {
	sub := &subscriber{
		messages: make(chan []byte, SubscriberBuffer),
		dropSlow: dropSlow,
	}

	h.mu.Lock()
	if h.maxSubscribers > 0 && len(h.subscribers) >= h.maxSubscribers {
		h.refused++
		h.mu.Unlock()

		return nil, nil, false
	}
	h.subscribers[sub] = struct{}{}
	h.mu.Unlock()

	return sub.messages, func() {
		h.mu.Lock()
		delete(h.subscribers, sub)
		h.mu.Unlock()
	}, true
}

// Broadcast sends one payload to every client, without blocking on any of them.
func (h *Hub) Broadcast(payload []byte) {
	h.mu.Lock()
	defer h.mu.Unlock()

	for sub := range h.subscribers {
		select {
		case sub.messages <- payload:
		default:
			// Full: drop the client; it reconnects and replays from its last sequence.
			// dropSlow is safe under this lock: it only cancels a context, and the
			// unsubscribe that follows waits for this mutex.
			if !sub.dropped {
				sub.dropped = true
				h.slowDrops++
				sub.dropSlow()
			}
		}
	}
}

// Count is how many clients are connected, for the log and for the tests.
func (h *Hub) Count() int {
	h.mu.Lock()
	defer h.mu.Unlock()

	return len(h.subscribers)
}

// SlowDrops is how many clients have been disconnected for falling behind since startup.
func (h *Hub) SlowDrops() int64 {
	h.mu.Lock()
	defer h.mu.Unlock()

	return h.slowDrops
}

// Refused is how many clients have been turned away because the hub was at capacity.
func (h *Hub) Refused() int64 {
	h.mu.Lock()
	defer h.mu.Unlock()

	return h.refused
}
