package caster

import (
	"context"
	"encoding/json"
	"errors"
	"io"
	"log/slog"
	"net/http"
	"net/http/httptest"
	"strings"
	"testing"
	"time"

	"github.com/coder/websocket"

	"github.com/caesarakalaeii/subnautica-creator-royale/backend/internal/config"
)

// cameraServer runs the service on a real HTTP server, since an httptest.Recorder cannot be upgraded.
func cameraServer(t *testing.T) (*Server, string) {
	t.Helper()

	cfg := config.Config{Port: 8080, LogLevel: slog.LevelError, LogFormat: config.LogFormatJSON,
		Booths: oneBooth(token, viewToken, operatorToken)}
	server := NewServer(cfg, slog.New(slog.NewTextHandler(io.Discard, nil)))

	httpServer := httptest.NewServer(server.Handler())
	t.Cleanup(httpServer.Close)

	return server, "ws" + strings.TrimPrefix(httpServer.URL, "http")
}

// aShot is a camera message as a page sends one: position, target and cut.
func aShot(x float64) []byte {
	return []byte(`{"v":1,"position":{"x":` + formatFloat(x) +
		`,"y":3600,"z":4600},"target":{"x":0,"y":-500,"z":0},"cut":0}`)
}

func formatFloat(value float64) string {
	payload, err := json.Marshal(value)
	if err != nil {
		panic(err)
	}

	return string(payload)
}

// dialFollower connects as a follower, with the view token in the query: the only place a browser
// can put one on a WebSocket handshake.
func dialFollower(t *testing.T, base, control string) (*websocket.Conn, context.Context) {
	t.Helper()

	return dial(t, base+cameraPath+"?token="+viewToken+control)
}

// dialDriver connects as a driver, with the operator token as a subprotocol so the write
// credential stays out of proxy access logs.
func dialDriver(t *testing.T, base, control string) (*websocket.Conn, context.Context) {
	t.Helper()

	return dialWith(t, base+cameraPath+control, operatorToken)
}

// dialWith is `dial` offering the driver subprotocol with the given token.
func dialWith(t *testing.T, url, protocol string) (*websocket.Conn, context.Context) {
	t.Helper()

	ctx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
	t.Cleanup(cancel)

	connection, _, err := websocket.Dial(ctx, url,
		&websocket.DialOptions{Subprotocols: []string{cameraDriverProtocol, protocol}})
	if err != nil {
		t.Fatalf("dial %s: %v", url, err)
	}

	t.Cleanup(func() { connection.CloseNow() })

	return connection, ctx
}

// readCamera reads the next camera message on a connection as a JSON document.
func readCamera(t *testing.T, ctx context.Context, connection *websocket.Conn) map[string]any {
	t.Helper()

	_, payload, err := connection.Read(ctx)
	if err != nil {
		t.Fatalf("read a camera: %v", err)
	}

	var document map[string]any
	if err := json.Unmarshal(payload, &document); err != nil {
		t.Fatalf("the camera channel sent something that is not a document: %v (%q)", err, payload)
	}

	return document
}

func TestAPageHoldingOnlyTheViewTokenCannotMoveTheCamera(t *testing.T) {
	_, base := cameraServer(t)

	// Connected before anything is published, so it sees everything that reached the channel.
	watching, watchingCtx := dialFollower(t, base, "")

	follower, followerCtx := dialFollower(t, base, "")
	if err := follower.Write(followerCtx, websocket.MessageText, aShot(10)); err != nil {
		t.Fatalf("write: %v", err)
	}

	// Refused by closing the connection rather than silently ignoring the write.
	if _, _, err := follower.Read(followerCtx); err == nil {
		t.Fatal("a view token moved the camera")
	}

	writing, writingCtx := dialDriver(t, base, "")
	if err := writing.Write(writingCtx, websocket.MessageText, aShot(20)); err != nil {
		t.Fatalf("the operator's write: %v", err)
	}

	// The operator's camera arrives, so the refusal was about the credential, not the message.
	document := readCamera(t, watchingCtx, watching)
	if position, _ := document["position"].(map[string]any); position["x"] != float64(20) {
		t.Errorf("the follower drew %v, want the camera the operator published", document["position"])
	}
}

func TestTheOperatorTokenIsWhatDrivesTheCamera(t *testing.T) {
	_, base := cameraServer(t)

	follower, followerCtx := dialFollower(t, base, "")

	publisher, publisherCtx := dialDriver(t, base, "")
	if err := publisher.Write(publisherCtx, websocket.MessageText, aShot(42)); err != nil {
		t.Fatalf("write: %v", err)
	}

	document := readCamera(t, followerCtx, follower)
	if position, _ := document["position"].(map[string]any); position["x"] != float64(42) {
		t.Errorf("follower saw %v, want the published camera", document)
	}
}

func TestACameraChannelRefusesATokenItDoesNotKnow(t *testing.T) {
	_, base := cameraServer(t)

	ctx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
	defer cancel()

	for _, presented := range []string{"?token=not-a-token", ""} {
		connection, response, err := websocket.Dial(ctx, base+cameraPath+presented, nil)
		if err == nil {
			connection.CloseNow()
			t.Fatalf("a stranger presenting %q was admitted to the camera channel", presented)
		}

		if response == nil || response.StatusCode != http.StatusUnauthorized {
			t.Errorf("presenting %q got %v, want 401", presented, response)
		}
	}
}

func TestTheOperatorTokenInTheQueryDoesNotDrive(t *testing.T) {
	_, base := cameraServer(t)

	ctx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
	defer cancel()

	// Query strings land in proxy access logs, so the write credential is refused there.
	connection, _, err := websocket.Dial(ctx, base+cameraPath+"?token="+operatorToken, nil)
	if err == nil {
		connection.CloseNow()
		t.Fatal("the operator token in the query was admitted, so it is in every access log")
	}
}

func TestABadOperatorTokenIsRefusedRatherThanDemotedToWatching(t *testing.T) {
	_, base := cameraServer(t)

	ctx, cancel := context.WithTimeout(context.Background(), 5*time.Second)
	defer cancel()

	connection, _, err := websocket.Dial(ctx, base+cameraPath+"?token="+viewToken,
		&websocket.DialOptions{Subprotocols: []string{cameraDriverProtocol, "not-the-operator-token"}})
	if err == nil {
		connection.CloseNow()
		t.Fatal("a bad operator token was served as a follower")
	}
}

func TestABurstOfCamerasEvictsNothingFromALiveSubscribersBuffer(t *testing.T) {
	server, base := cameraServer(t)

	watching, watchingCtx := dial(t, base+livePath+"?token="+viewToken)
	readSnapshot(t, watchingCtx, watching)
	waitForClients(t, server, 1)

	publisher, publisherCtx := dialDriver(t, base, "")
	for sent := 0; sent < SubscriberBuffer*4; sent++ {
		if err := publisher.Write(publisherCtx, websocket.MessageText, aShot(float64(sent))); err != nil {
			t.Fatalf("publishing camera %d: %v", sent, err)
		}
	}

	// The live subscriber has read nothing since its replay, so any camera in its buffer would be
	// read here instead of the push.
	pushExample(t, server)

	if snapshot := readSnapshot(t, watchingCtx, watching); snapshot.Match == nil {
		t.Fatal("a live subscriber got something other than the match after a burst of cameras")
	}

	if drops := server.Hub().SlowDrops(); drops != 0 {
		t.Errorf("slow drops = %d, want none: the cameras spent the live buffer", drops)
	}
}

func TestAFollowerWithNoPublisherIsSentNoCameraAtAll(t *testing.T) {
	_, base := cameraServer(t)

	follower, _ := dialFollower(t, base, "")

	ctx, cancel := context.WithTimeout(context.Background(), 250*time.Millisecond)
	defer cancel()

	if _, payload, err := follower.Read(ctx); err == nil {
		t.Fatalf("a follower with no publisher was sent %q, want its own opening shot left alone", payload)
	}
}

func TestAPublisherThatDisconnectsLeavesTheFollowerWhereItWas(t *testing.T) {
	_, base := cameraServer(t)

	follower, followerCtx := dialFollower(t, base, "")

	publisher, publisherCtx := dialDriver(t, base, "")
	if err := publisher.Write(publisherCtx, websocket.MessageText, aShot(7)); err != nil {
		t.Fatalf("write: %v", err)
	}
	readCamera(t, followerCtx, follower)

	if err := publisher.Close(websocket.StatusNormalClosure, "the caster closed the tab"); err != nil {
		t.Fatalf("close: %v", err)
	}

	quiet, cancel := context.WithTimeout(context.Background(), 250*time.Millisecond)
	defer cancel()

	if _, payload, err := follower.Read(quiet); err == nil {
		t.Fatalf("a publisher going away sent %q, want silence", payload)
	}

	// A follower joining afterwards gets the last camera.
	late, lateCtx := dialFollower(t, base, "")

	document := readCamera(t, lateCtx, late)
	if position, _ := document["position"].(map[string]any); position["x"] != float64(7) {
		t.Errorf("a late follower got %v, want the camera the publisher left behind", document)
	}
}

func TestASecondPublisherOnOneChannelIsRefused(t *testing.T) {
	_, base := cameraServer(t)

	first, firstCtx := dialDriver(t, base, "")

	// The first driver's camera reaching a follower proves it holds the channel; a completed
	// handshake alone does not, since the claim happens afterwards on the server's goroutine.
	watching, watchingCtx := dialFollower(t, base, "")
	if err := first.Write(firstCtx, websocket.MessageText, aShot(1)); err != nil {
		t.Fatalf("the first publisher's write: %v", err)
	}

	if document := readCamera(t, watchingCtx, watching); document["v"] == nil {
		t.Fatalf("the follower got %v, want the first driver's camera", document)
	}

	second, secondCtx := dialDriver(t, base, "")

	_, _, err := second.Read(secondCtx)
	if err == nil {
		t.Fatal("two publishers were admitted to one control channel")
	}

	if status := websocket.CloseStatus(err); status != websocket.StatusTryAgainLater {
		t.Errorf("close status = %v, want %v (1013), which is what this booth already sends a client "+
			"it has no room for", status, websocket.StatusTryAgainLater)
	}

	var closed websocket.CloseError
	if errors.As(err, &closed) && !strings.Contains(closed.Reason, "driving") {
		t.Errorf("close reason = %q, want it to say somebody else is driving", closed.Reason)
	}

	// The existing driver keeps the camera. Read the current camera before publishing the next,
	// since the follower may not be registered yet and could otherwise receive the newer shot.
	follower, followerCtx := dialFollower(t, base, "")

	document := readCamera(t, followerCtx, follower)
	if position, _ := document["position"].(map[string]any); position["x"] != float64(1) {
		t.Errorf("a joining follower got %v, want the camera in hand when it connected", document)
	}

	// The first driver was not closed along with the second.
	if err := first.Write(firstCtx, websocket.MessageText, aShot(2)); err != nil {
		t.Fatalf("the first publisher stopped being able to drive: %v", err)
	}

	next := readCamera(t, followerCtx, follower)
	if position, _ := next["position"].(map[string]any); position["x"] != float64(2) {
		t.Errorf("the follower got %v, want the camera the first publisher went on driving", next)
	}
}

func TestTheChannelIsFreeAgainOnceTheDriverGoesAway(t *testing.T) {
	_, base := cameraServer(t)

	first, firstCtx := dialDriver(t, base, "")
	if err := first.Write(firstCtx, websocket.MessageText, aShot(1)); err != nil {
		t.Fatalf("write: %v", err)
	}

	if err := first.Close(websocket.StatusNormalClosure, "done"); err != nil {
		t.Fatalf("close: %v", err)
	}

	// Retried because the server notices the close asynchronously. A fresh follower each round,
	// since a read that times out closes the connection in this library.
	deadline := time.Now().Add(3 * time.Second)
	for {
		follower, followerCtx := dialFollower(t, base, "")
		readCamera(t, followerCtx, follower) // the departed driver's last camera

		second, secondCtx := dialDriver(t, base, "")
		if err := second.Write(secondCtx, websocket.MessageText, aShot(3)); err != nil {
			t.Fatalf("the second driver's write: %v", err)
		}

		if document, arrived := nextCamera(t, follower); arrived {
			if position, _ := document["position"].(map[string]any); position["x"] != float64(3) {
				t.Errorf("follower got %v, want the second driver's camera", document)
			}

			return
		}

		second.CloseNow()

		if time.Now().After(deadline) {
			t.Fatal("the control channel was never handed back after its driver went away")
		}

		time.Sleep(5 * time.Millisecond)
	}
}

// nextCamera reads the next camera on a connection, or false if none arrives shortly. A timed-out
// read closes the connection in this library, so the connection is spent either way.
func nextCamera(t *testing.T, connection *websocket.Conn) (map[string]any, bool) {
	t.Helper()

	ctx, cancel := context.WithTimeout(context.Background(), 250*time.Millisecond)
	defer cancel()

	_, payload, err := connection.Read(ctx)
	if err != nil {
		return nil, false
	}

	var document map[string]any
	if err := json.Unmarshal(payload, &document); err != nil {
		t.Fatalf("the camera channel sent something that is not a document: %v (%q)", err, payload)
	}

	return document, true
}

func TestOneControlChannelDoesNotDriveAnother(t *testing.T) {
	_, base := cameraServer(t)

	desk, deskCtx := dialDriver(t, base, "?control=desk-1")
	if err := desk.Write(deskCtx, websocket.MessageText, aShot(11)); err != nil {
		t.Fatalf("write: %v", err)
	}

	other, _ := dialFollower(t, base, "&control=desk-2")

	quiet, cancel := context.WithTimeout(context.Background(), 250*time.Millisecond)
	defer cancel()

	if _, payload, err := other.Read(quiet); err == nil {
		t.Fatalf("desk-2 was sent %q, which desk-1 published", payload)
	}
}

func TestACameraThatIsNotADocumentIsNotFannedOut(t *testing.T) {
	_, base := cameraServer(t)

	follower, _ := dialFollower(t, base, "")

	publisher, publisherCtx := dialDriver(t, base, "")
	for _, bad := range [][]byte{
		[]byte(`not json at all`),
		[]byte(`{"v":99,"position":{"x":0,"y":0,"z":0},"target":{"x":0,"y":0,"z":0},"cut":0}`),
		[]byte(`{"v":1,"position":{"x":0,"y":0},"target":{"x":0,"y":0,"z":0},"cut":0}`),
		[]byte(`{"v":1,"position":{"x":0,"y":0,"z":0},"target":{"x":0,"y":0,"z":0},"cut":-5}`),
	} {
		if err := publisher.Write(publisherCtx, websocket.MessageText, bad); err != nil {
			t.Fatalf("writing %q: %v", bad, err)
		}
	}

	quiet, cancel := context.WithTimeout(context.Background(), 250*time.Millisecond)
	defer cancel()

	if _, payload, err := follower.Read(quiet); err == nil {
		t.Fatalf("the follower was sent %q, want nothing it cannot draw", payload)
	}
}

func TestACameraNeverReachesTheMatchStore(t *testing.T) {
	server, base := cameraServer(t)
	pushExample(t, server)

	publisher, publisherCtx := dialDriver(t, base, "")
	if err := publisher.Write(publisherCtx, websocket.MessageText, aShot(5)); err != nil {
		t.Fatalf("write: %v", err)
	}

	// Read back through a follower so the publish has been handled before the store is checked.
	follower, followerCtx := dialFollower(t, base, "")
	readCamera(t, followerCtx, follower)

	watching, watchingCtx := dial(t, base+livePath+"?token="+viewToken)

	snapshot := readSnapshot(t, watchingCtx, watching)
	if len(snapshot.Events) != 3 {
		t.Errorf("events = %d, want the three the match pushed and nothing about a camera",
			len(snapshot.Events))
	}
}

func TestAChannelNobodyIsUsingIsNotKeptForever(t *testing.T) {
	held := newCameras()

	// Channel names are client-chosen, so an unused channel kept forever would let a view-token
	// holder grow this map without bound.
	_, unfollow, camera := held.follow("desk-1", func() {})
	if camera != nil {
		t.Errorf("a first follower was sent %q, want nothing at all", camera)
	}

	unfollow()

	if channels := len(held.channels); channels != 0 {
		t.Errorf("channels = %d after everybody left, want none held", channels)
	}
}

func TestAChannelThatHasBeenDrivenKeepsItsCameraWithNobodyWatching(t *testing.T) {
	held := newCameras()

	release, alone := held.drive("desk-1")
	if !alone {
		t.Fatal("a fresh channel was already claimed")
	}

	held.publish("desk-1", aShot(9))
	release()

	_, _, camera := held.follow("desk-1", func() {})
	if string(camera) != string(aShot(9)) {
		t.Errorf("a follower arriving afterwards got %q, want the camera the driver left", camera)
	}
}

func TestADriverThatStopsAnsweringLosesTheChannel(t *testing.T) {
	server, base := cameraServer(t)
	server.heartbeat = 20 * time.Millisecond

	// Dialled directly and never read from: the library answers pings inside Read, so not reading
	// models a wedged peer.
	ctx, cancel := context.WithTimeout(context.Background(), 10*time.Second)
	defer cancel()

	wedged, _, err := websocket.Dial(ctx, base+cameraPath,
		&websocket.DialOptions{Subprotocols: []string{cameraDriverProtocol, operatorToken}})
	if err != nil {
		t.Fatalf("dial: %v", err)
	}
	defer wedged.CloseNow()

	if err := wedged.Write(ctx, websocket.MessageText, aShot(1)); err != nil {
		t.Fatalf("write: %v", err)
	}

	// A fresh follower each round, since a read that times out closes the connection in this library.
	deadline := time.Now().Add(5 * time.Second)
	for {
		follower, followerCtx := dialFollower(t, base, "")
		readCamera(t, followerCtx, follower) // the wedged driver's camera

		next, nextCtx := dialDriver(t, base, "")
		if err := next.Write(nextCtx, websocket.MessageText, aShot(2)); err != nil {
			t.Fatalf("the next driver's write: %v", err)
		}

		if _, arrived := nextCamera(t, follower); arrived {
			return
		}

		next.CloseNow()

		if time.Now().After(deadline) {
			t.Fatal("a driver that stopped answering held the camera channel")
		}

		time.Sleep(10 * time.Millisecond)
	}
}
