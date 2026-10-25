package caster

import (
	"context"
	"encoding/json"
	"math"
	"net/http"
	"regexp"
	"strings"
	"sync"
	"time"

	"github.com/coder/websocket"
)

// cameraPath is where a caster drives the solid map and a browser source follows it. A separate
// socket from /live: 10 Hz camera messages would shrink SubscriberBuffer's grace for every client.
const cameraPath = "/camera"

// cameraVersion is the camera document version, independent of the telemetry contract's. Mirrored
// by `CAMERA_VERSION` in static/camera.js.
const cameraVersion = 1

// cameraBuffer is how many camera messages a follower may fall behind before it is dropped. Only
// the newest matters, and a reconnecting follower is sent the current camera.
const cameraBuffer = 8

// maxCameraBytes bounds one camera message; a real one is under 200 bytes.
const maxCameraBytes = 16 << 10

// busyReason is what a second driver is told. The page matches the close code, not this text.
const busyReason = "somebody else is driving this camera"

// watchingIsNotDriving is what a follower that tried to move the camera is told.
const watchingIsNotDriving = "this token may watch the camera, not move it"

// cameraDriverProtocol is the WebSocket subprotocol a driver offers, followed by its operator token
// as a second protocol. Sec-WebSocket-Protocol is the only header a browser can set on a handshake,
// and unlike the query string it stays out of proxy access logs, which matters for a write token.
const cameraDriverProtocol = "lms.camera.operator"

// validControl is what a control channel may be called: the same shape as a booth id
// (static/camera.js mirrors it).
var validControl = regexp.MustCompile(`^[a-z0-9-]{1,40}$`)

// defaultControl is the channel a page drives or follows when its address names none.
const defaultControl = "desk"

// cameraMessage is a camera as the wire carries it: position, target, and the depth of the terrain
// cut. Pointers distinguish a missing field from a zero one.
type cameraMessage struct {
	Version  int         `json:"v"`
	Position cameraPoint `json:"position"`
	Target   cameraPoint `json:"target"`
	Cut      *float64    `json:"cut"`
}

type cameraPoint struct {
	X *float64 `json:"x"`
	Y *float64 `json:"y"`
	Z *float64 `json:"z"`
}

// drawable is whether a follower could draw this camera. Checked centrally because a NaN in
// three.js's view matrix blanks the scene on every follower.
func (m cameraMessage) drawable() bool {
	// The cut is a depth in metres below sea level, zero for off; negative would clip the islands.
	return m.Version == cameraVersion &&
		m.Position.finite() && m.Target.finite() &&
		finite(m.Cut) && *m.Cut >= 0
}

func (p cameraPoint) finite() bool {
	return finite(p.X) && finite(p.Y) && finite(p.Z)
}

// finite is whether a field arrived at all and is a number a renderer can use.
func finite(value *float64) bool {
	return value != nil && !math.IsNaN(*value) && !math.IsInf(*value, 0)
}

// cameras is one booth's control channels: the camera each is showing, and who is watching it. Not
// match state: it never reaches Store and is not replayed on /live.
type cameras struct {
	mu       sync.Mutex
	channels map[string]*controlChannel
}

// controlChannel is one camera: the last one published, whether somebody is driving, and every
// source following it.
type controlChannel struct {
	// last is the camera in hand, as the bytes that were published. Kept after the driver leaves so
	// followers keep the shot and a new follower starts on it.
	last []byte

	// driving is whether somebody holds this channel. One driver at a time.
	driving bool

	followers map[*cameraFollower]struct{}
}

type cameraFollower struct {
	messages chan []byte

	// dropSlow closes the connection instead of blocking, as in Hub.
	dropSlow func()
	dropped  bool
}

func newCameras() *cameras {
	return &cameras{channels: make(map[string]*controlChannel)}
}

// channel is one control channel, made if this is the first page to ask for it.
func (c *cameras) channel(control string) *controlChannel {
	held, known := c.channels[control]
	if !known {
		held = &controlChannel{followers: make(map[*cameraFollower]struct{})}
		c.channels[control] = held
	}

	return held
}

// follow adds a source to a channel. It returns the messages to draw, an unfollow func, and the
// current camera, which is nil when nobody has driven this channel yet.
func (c *cameras) follow(control string, dropSlow func()) (<-chan []byte, func(), []byte) {
	follower := &cameraFollower{messages: make(chan []byte, cameraBuffer), dropSlow: dropSlow}

	c.mu.Lock()
	defer c.mu.Unlock()

	held := c.channel(control)
	held.followers[follower] = struct{}{}

	return follower.messages, func() {
		c.mu.Lock()
		defer c.mu.Unlock()

		delete(held.followers, follower)
		c.forgetIfEmpty(control, held)
	}, held.last
}

// forgetIfEmpty drops a channel nobody is using and nobody has ever driven, so view-token holders
// cannot grow the map by naming channels. Called under the lock.
func (c *cameras) forgetIfEmpty(control string, held *controlChannel) {
	if held.last == nil && !held.driving && len(held.followers) == 0 {
		delete(c.channels, control)
	}
}

// drive claims a channel, or returns false if somebody else is driving it. The caller must call the
// returned release when the driver goes away.
func (c *cameras) drive(control string) (func(), bool) {
	c.mu.Lock()
	defer c.mu.Unlock()

	held := c.channel(control)
	if held.driving {
		return nil, false
	}
	held.driving = true

	return func() {
		c.mu.Lock()
		defer c.mu.Unlock()

		held.driving = false
		c.forgetIfEmpty(control, held)
	}, true
}

// publish puts one camera on a channel and hands it to everybody following. Never blocks, like
// Hub.Broadcast: slow followers are dropped.
func (c *cameras) publish(control string, payload []byte) {
	c.mu.Lock()
	defer c.mu.Unlock()

	held := c.channel(control)
	held.last = payload

	for follower := range held.followers {
		select {
		case follower.messages <- payload:
		default:
			if !follower.dropped {
				follower.dropped = true
				follower.dropSlow()
			}
		}
	}
}

// camera is one page on a control channel: a caster driving it, or a source following it. The
// credential decides which: the operator token drives, the view token only follows.
func (s *Server) camera(w http.ResponseWriter, r *http.Request) {
	booth := s.booths.asked(r, s.now())

	// As in live: 503 for a known booth without a view token; unknown booths get the 401 below.
	if booth != nil && !booth.viewConfigured() {
		reply(w, http.StatusServiceUnavailable,
			ingestResponse{Error: "the camera channel is not configured on this service"})

		return
	}

	offered := driverToken(r)
	driver := offered != "" && booth.mayDrive(offered)

	// A failed driver offer is refused, not downgraded to a follower.
	if !driver && (offered != "" || !booth.mayView(r)) {
		s.logger.Warn("refused a camera connection", "reason", "bad token",
			"booth", askedFor(r), "remote", r.RemoteAddr)
		reply(w, http.StatusUnauthorized, ingestResponse{Error: "bad or missing token"})

		return
	}

	// The browser closes the connection unless an offered subprotocol is echoed.
	var negotiated []string
	if driver {
		negotiated = []string{cameraDriverProtocol}
	}

	connection, err := websocket.Accept(w, r, &websocket.AcceptOptions{
		InsecureSkipVerify: false,
		Subprotocols:       negotiated,
	})
	if err != nil {
		// Accept has already written a response by this point.
		s.logger.Warn("camera connection could not be upgraded", "reason", err.Error())

		return
	}

	defer connection.CloseNow()

	connection.SetReadLimit(maxCameraBytes)

	ctx, cancel := context.WithCancel(r.Context())
	defer cancel()

	control := controlAsked(r)

	if driver {
		s.drive(ctx, cancel, connection, booth, control, r)

		return
	}

	s.followCamera(ctx, cancel, connection, booth, control, r)
}

// drive is one caster's socket: everything they send moves the camera every source on this channel
// is drawing.
func (s *Server) drive(ctx context.Context, cancel context.CancelFunc, connection *websocket.Conn,
	booth *Booth, control string, r *http.Request) {
	release, alone := booth.cameras.drive(control)
	if !alone {
		// Refused after the upgrade so the page gets a close code, as with the live cap.
		s.logger.Warn("refused a camera driver", "reason", "already driving", "booth", booth.ID(),
			"control", control, "remote", r.RemoteAddr)

		_ = connection.Close(websocket.StatusTryAgainLater, busyReason)

		return
	}
	defer release()

	s.logger.Info("camera driver connected", "booth", booth.ID(), "control", control,
		"remote", r.RemoteAddr)
	defer s.logger.Info("camera driver gone", "booth", booth.ID(), "control", control,
		"remote", r.RemoteAddr)

	// Read in its own goroutine so heartbeats can go out between gestures.
	cameras := make(chan []byte, cameraBuffer)
	go readCameras(ctx, cancel, connection, cameras)

	beat := time.NewTicker(s.heartbeat)
	defer beat.Stop()

	for {
		select {
		case payload := <-cameras:
			var message cameraMessage
			if err := json.Unmarshal(payload, &message); err != nil || !message.drawable() {
				// Dropped, not closed: closing would start a reconnect loop holding the channel.
				s.logger.Warn("a camera was refused", "booth", booth.ID(), "control", control,
					"reason", "not a camera this service can draw")

				continue
			}

			booth.cameras.publish(control, payload)

			// A gesture is proof of life, as a push is on /live.
			beat.Reset(s.heartbeat)
		case <-beat.C:
			if err := s.beat(ctx, connection, booth, control, r); err != nil {
				return
			}
		case <-ctx.Done():
			return
		}
	}
}

// readCameras hands one socket's messages to the drive loop, and cancels when it is done.
func readCameras(ctx context.Context, cancel context.CancelFunc, connection *websocket.Conn,
	cameras chan<- []byte) {
	defer cancel()

	for {
		_, payload, err := connection.Read(ctx)
		if err != nil {
			return
		}

		select {
		case cameras <- payload:
		case <-ctx.Done():
			return
		}
	}
}

// followCamera is one source's socket: the camera in hand, and every one after it.
func (s *Server) followCamera(ctx context.Context, cancel context.CancelFunc,
	connection *websocket.Conn, booth *Booth, control string, r *http.Request) {
	messages, unfollow, held := booth.cameras.follow(control, cancel)
	defer unfollow()

	s.logger.Info("camera follower connected", "booth", booth.ID(), "control", control,
		"remote", r.RemoteAddr)

	// Nothing to send if nobody has driven yet; the source keeps its own opening framing.
	if held != nil {
		if err := send(ctx, connection, held); err != nil {
			return
		}
	}

	go refuseToBeDriven(ctx, cancel, connection)

	beat := time.NewTicker(s.heartbeat)
	defer beat.Stop()

	for {
		select {
		case payload := <-messages:
			if err := send(ctx, connection, payload); err != nil {
				return
			}

			beat.Reset(s.heartbeat)
		case <-beat.C:
			if err := s.beat(ctx, connection, booth, control, r); err != nil {
				return
			}
		case <-ctx.Done():
			return
		}
	}
}

// beat is one heartbeat on a camera socket, as on /live: it keeps proxies from closing an idle
// connection and frees a vanished driver's slot.
func (s *Server) beat(ctx context.Context, connection *websocket.Conn, booth *Booth,
	control string, r *http.Request) error {
	err := ping(ctx, connection, s.heartbeat)
	if err != nil && ctx.Err() == nil {
		s.logger.Warn("camera client failed its heartbeat", "booth", booth.ID(),
			"control", control, "remote", r.RemoteAddr, "reason", err.Error())
	}

	return err
}

// refuseToBeDriven reads a follower's socket (needed for close and pong handling) and closes it
// with 1008 if anything arrives, distinct from the 1013 a second driver gets.
func refuseToBeDriven(ctx context.Context, cancel context.CancelFunc, connection *websocket.Conn) {
	defer cancel()

	// Control frames are handled inside Read, so this blocks for a well-behaved follower's lifetime.
	if _, _, err := connection.Read(ctx); err != nil {
		return
	}

	_ = connection.Close(websocket.StatusPolicyViolation, watchingIsNotDriving)
}

// driverToken is the operator token from `Sec-WebSocket-Protocol: lms.camera.operator, <token>`, or
// empty when the handshake is not a driver offer.
func driverToken(r *http.Request) string {
	offered := strings.Split(r.Header.Get("Sec-WebSocket-Protocol"), ",")
	if len(offered) != 2 || strings.TrimSpace(offered[0]) != cameraDriverProtocol {
		return ""
	}

	return strings.TrimSpace(offered[1])
}

// controlAsked is the channel a request named, or the default when the name is invalid: a browser
// source cannot fix a typo, so falling back beats refusing.
func controlAsked(r *http.Request) string {
	asked := r.URL.Query().Get("control")
	if !validControl.MatchString(asked) {
		return defaultControl
	}

	return asked
}
