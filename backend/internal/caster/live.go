package caster

import (
	"context"
	"encoding/json"
	"net/http"
	"strconv"
	"sync/atomic"
	"time"

	"github.com/coder/websocket"

	"github.com/caesarakalaeii/subnautica-creator-royale/backend/internal/telemetry"
)

// livePath is where dashboards and overlays connect.
const livePath = "/live"

// writeTimeout bounds one send, so a client whose TCP window has closed cannot hold the writer.
const writeTimeout = 10 * time.Second

// heartbeatInterval is how often an otherwise silent connection is pinged: under the idle timeouts
// of common proxies (nginx-ingress 60s, Cloudflare ~100s). A ping is answered, so it also frees the
// capped slot of a peer that vanished without closing.
const heartbeatInterval = 30 * time.Second

// atCapacityReason is what a client refused for want of a slot is told. The front-end matches the
// close code, not this text.
const atCapacityReason = "the booth is at capacity"

// cursorParam is the query parameter a client resumes from: everything after this audit sequence.
// Absent or zero asks for the whole window.
const cursorParam = "after"

// live streams the match to one dashboard or overlay. The first message is the full state (last
// snapshot plus missed events); after that each push is forwarded as it arrives. Both use the
// telemetry document shape; the client deduplicates events on `seq`.
func (s *Server) live(w http.ResponseWriter, r *http.Request) {
	booth := s.booths.asked(r, s.now())

	// As in ingest: a known booth without a view token is a 503, while an unknown booth falls
	// through to the 401 so the response does not reveal which booths exist.
	if booth != nil && !booth.viewConfigured() {
		reply(w, http.StatusServiceUnavailable,
			ingestResponse{Error: "the live feed is not configured on this service"})
		return
	}

	if !booth.mayView(r) {
		s.logger.Warn("refused a live connection", "reason", "bad token",
			"booth", askedFor(r), "remote", r.RemoteAddr)
		reply(w, http.StatusUnauthorized, ingestResponse{Error: "bad or missing view token"})
		return
	}

	connection, err := websocket.Accept(w, r, &websocket.AcceptOptions{
		// Same-origin check stays on: the front-end is served from this origin and OBS browser
		// sources send no Origin.
		InsecureSkipVerify: false,
	})
	if err != nil {
		// Accept has already written a response by this point.
		s.logger.Warn("live connection could not be upgraded", "reason", err.Error())
		return
	}

	defer connection.CloseNow()

	ctx, cancel := context.WithCancel(r.Context())
	defer cancel()

	// slow is whether this client was disconnected for falling behind rather than for going away.
	var slow atomic.Bool

	messages, unsubscribe, admitted := booth.hub.Subscribe(func() {
		slow.Store(true)
		cancel()
	})
	if !admitted {
		// Refused after the upgrade: browser script cannot read a failed handshake's status, so
		// only a close code can tell the client it was capacity and not a bad token.
		s.logger.Warn("refused a live connection", "reason", "at capacity",
			"booth", booth.ID(), "clients", booth.hub.Count(), "max", s.cfg.MaxLiveClients,
			"refused_total", booth.hub.Refused(), "remote", r.RemoteAddr)

		_ = connection.Close(websocket.StatusTryAgainLater, atCapacityReason)

		return
	}
	defer unsubscribe()

	s.logger.Info("live client connected", "booth", booth.ID(),
		"clients", booth.hub.Count(), "remote", r.RemoteAddr)
	defer func() {
		if slow.Load() {
			// Warn: this client outran the whole SubscriberBuffer.
			s.logger.Warn("live client dropped for falling behind", "booth", booth.ID(),
				"clients", booth.hub.Count()-1, "remote", r.RemoteAddr,
				"buffer", SubscriberBuffer, "slow_drops_total", booth.hub.SlowDrops())

			return
		}

		s.logger.Info("live client gone", "booth", booth.ID(),
			"clients", booth.hub.Count()-1, "remote", r.RemoteAddr)
	}()

	if err := sendReplay(ctx, connection, booth.store, cursor(r)); err != nil {
		return
	}

	// Reading notices a close promptly and keeps the library's ping/pong handling running, which
	// the heartbeat below depends on.
	go drain(ctx, cancel, connection)

	beat := time.NewTicker(s.heartbeat)
	defer beat.Stop()

	for {
		select {
		case payload := <-messages:
			if err := send(ctx, connection, payload); err != nil {
				return
			}

			// A push is proof of life, so the next ping is due a full interval after it.
			beat.Reset(s.heartbeat)
		case <-beat.C:
			if err := ping(ctx, connection, s.heartbeat); err != nil {
				if ctx.Err() == nil {
					s.logger.Warn("live client failed its heartbeat", "booth", booth.ID(),
						"clients", booth.hub.Count()-1, "remote", r.RemoteAddr, "reason", err.Error())
				}

				return
			}
		case <-ctx.Done():
			return
		}
	}
}

// ping sends one heartbeat and waits for the answer, at most until the next beat or writeTimeout,
// whichever is sooner.
func ping(ctx context.Context, connection *websocket.Conn, within time.Duration) error {
	if within > writeTimeout {
		within = writeTimeout
	}

	ctx, cancel := context.WithTimeout(ctx, within)
	defer cancel()

	return connection.Ping(ctx)
}

// sendReplay is the first message: the whole state, and the events this client has missed. The
// store is passed in so a client is only ever replayed its own booth's window.
func sendReplay(ctx context.Context, connection *websocket.Conn, store *Store, after int64) error {
	snapshot, ok := store.Latest()
	if !ok {
		// Nothing pushed yet: send an empty but well-formed document so a dashboard can tell
		// "connected, no match" from "not connected".
		snapshot = telemetry.Snapshot{Version: telemetry.Version, At: time.Now().UTC()}
	}

	snapshot.Events = store.EventsAfter(after)

	payload, err := json.Marshal(telemetry.ForSending(snapshot))
	if err != nil {
		return err
	}

	return send(ctx, connection, payload)
}

// send writes one text frame, bounded so a stuck client cannot hold the writer.
func send(ctx context.Context, connection *websocket.Conn, payload []byte) error {
	ctx, cancel := context.WithTimeout(ctx, writeTimeout)
	defer cancel()

	return connection.Write(ctx, websocket.MessageText, payload)
}

// drain reads and discards. Clients send nothing, but without a reader a close never arrives and
// pongs are never processed.
func drain(ctx context.Context, cancel context.CancelFunc, connection *websocket.Conn) {
	defer cancel()

	for {
		if _, _, err := connection.Read(ctx); err != nil {
			return
		}
	}
}

// cursor is the sequence a reconnecting client resumes from. Anything unreadable means the whole
// window: too much history is harmless, too little leaves an invisible gap.
func cursor(r *http.Request) int64 {
	raw := r.URL.Query().Get(cursorParam)
	if raw == "" {
		return 0
	}

	after, err := strconv.ParseInt(raw, 10, 64)
	if err != nil || after < 0 {
		return 0
	}

	return after
}
