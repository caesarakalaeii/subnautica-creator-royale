// camera.js is the shared-camera wire format: the document, its validation, the publish throttle and
// the socket address. Pure; the drawing is in `map3d.js` and the wiring in `dashboard.js` and
// `overlay-page.js`.
//
// Camera state is not telemetry: the booth does not retain or replay it. It uses its own socket so
// 10 Hz camera messages do not eat into the `/live` subscribers' small send buffer.

import { boothBase, storageKey } from "./booth.js";
// Same retry curve as the match feed.
import { backoffMs } from "./feed.js";

/** The camera document version, independent of the telemetry contract version. */
export const CAMERA_VERSION = 1;

/** The channel a page drives or follows when its address names none. */
export const DEFAULT_CONTROL = "desk";

// The operator token's storage key, shared with the console and bracket pages.
const OPERATOR_KEY = "lms.operatorToken";

/**
 * The WebSocket subprotocol a driver offers, with its token as the second entry. The write token
 * goes in this header (the only one a browser can set on a handshake) rather than the query, which
 * proxies log. Must match `cameraDriverProtocol` in internal/caster/camera.go (checked by camera.test.js).
 */
export const DRIVER_PROTOCOL = "lms.camera.operator";

/**
 * Valid channel names, the same shape as a booth id. Must match `validControl` in
 * internal/caster/camera.go (checked by camera.test.js).
 */
export const CONTROL = /^[a-z0-9-]{1,40}$/;

/** Where a camera socket lives, under whichever booth the page is on. */
const CAMERA_ROUTE = "/camera";

/**
 * One camera as it goes on the wire: position, target and depth cut. The cut is part of the shot,
 * so it travels with the camera.
 */
export function cameraDocument({ position, target, cut }) {
  return {
    v: CAMERA_VERSION,
    position: { x: position.x, y: position.y, z: position.z },
    target: { x: target.x, y: target.y, z: target.z },
    cut,
  };
}

/**
 * A camera read from a document, or `null` if invalid. Rejects non-finite values, since a `NaN`
 * would blank the three.js scene; on `null` the last good shot stays up.
 */
export function cameraFrom(document) {
  if (!document || document.v !== CAMERA_VERSION) return null;

  const position = point(document.position);
  const target = point(document.target);

  // A cut is a depth: positive metres down, zero for off.
  if (position === null || target === null || !finite(document.cut) || document.cut < 0) return null;

  return { position, target, cut: document.cut };
}

function point(raw) {
  if (!raw || !finite(raw.x) || !finite(raw.y) || !finite(raw.z)) return null;

  return { x: raw.x, y: raw.y, z: raw.z };
}

function finite(value) {
  return typeof value === "number" && Number.isFinite(value);
}

/**
 * The operator token to drive with, or "" to follow. Driving requires `?control=` in the address,
 * since one driver holds a channel at a time. The token comes from browser storage, never the URL.
 */
export function mayDrive(location, storage) {
  if (!onAChannel(location)) return "";

  return storage[storageKey(OPERATOR_KEY, location)] ?? "";
}

/**
 * Whether the address names a control channel. An invalid name still counts; `controlPin` maps it
 * to the default.
 */
export function onAChannel(location) {
  return Boolean(new URLSearchParams(location.search).get("control"));
}

/**
 * A handle on the map's camera to read and set the shot. `camera` is a `THREE.PerspectiveCamera`,
 * `controls` its `OrbitControls`, and `setCut` moves the terrain's clipping plane. The cut depth is
 * held here because the plane only stores an elevation.
 */
export function handCamera(camera, controls, setCut) {
  let cut = 0;

  // A closure, not a method, so it survives destructuring.
  const cutTo = (depth) => {
    cut = depth;
    setCut(depth);
  };

  return {
    /** Where the camera is, what it is aimed at, and how deep the cut is. */
    look() {
      return {
        position: { x: camera.position.x, y: camera.position.y, z: camera.position.z },
        target: { x: controls.target.x, y: controls.target.y, z: controls.target.z },
        cut,
      };
    },

    /** Moves the camera to a shot. `controls.update()` is required or OrbitControls snaps it back. */
    aim(shot) {
      camera.position.set(shot.position.x, shot.position.y, shot.position.z);
      controls.target.set(shot.target.x, shot.target.y, shot.target.z);
      controls.update();

      cutTo(shot.cut);
    },

    /** Moves only the cut (the dashboard's slider). */
    cutTo,
  };
}

/**
 * A leading-edge gate that opens at most once per window. Takes the current time in milliseconds
 * so tests can drive it.
 */
function createThrottle(windowMs) {
  let last = null;

  return (nowMs) => {
    if (last !== null && nowMs - last < windowMs) return false;

    last = nowMs;

    return true;
  };
}

/**
 * One page's end of a control channel, driving or following. `connect` and `schedule` are
 * injectable for tests. A follower keeps its last camera across reconnects; a driver ignores
 * incoming messages.
 */
export function createCameraLink({
  url,
  driving,
  token,
  connect,
  schedule = (run, ms) => setTimeout(run, ms),
  onCamera = () => {},
}) {
  let socket = null;
  let open = false;
  let attempt = 0;
  let stopped = false;

  function dial() {
    if (stopped) return;

    attempt += 1;
    open = false;

    // A driver presents its token via the subprotocol; a follower uses the view token in the URL.
    socket = connect(url, driving ? [DRIVER_PROTOCOL, token] : undefined);

    socket.onopen = () => {
      open = true;
      attempt = 0;
    };

    socket.onmessage = (message) => {
      if (driving) return;

      let camera = null;
      try {
        camera = cameraFrom(JSON.parse(message.data));
      } catch {
        // Unparseable frame: keep the current shot.
        return;
      }

      if (camera !== null) onCamera(camera);
    };

    socket.onclose = () => {
      if (stopped) return;

      open = false;
      schedule(dial, backoffMs(attempt));
    };
  }

  return {
    start: dial,

    /** Put one camera on the channel. Does nothing on a follower, and nothing before the socket is up. */
    publish(shot) {
      if (!driving || !open) return;

      socket.send(JSON.stringify(cameraDocument(shot)));
    },

    stop() {
      stopped = true;
      socket?.close();
    },
  };
}

/** How often a driving dashboard publishes its camera (10 Hz). */
export const PUBLISH_MS = 100;

/**
 * Throttled publisher: at most one message per window, and the last move of a gesture is always
 * sent (a held move arms a trailing send of the camera as it is then). `now` and `schedule` are
 * injectable for tests.
 */
export function createPublisher({ windowMs, send, now = () => Date.now(), schedule = setTimeout }) {
  const ready = createThrottle(windowMs);

  // The camera owed a trailing send, or null.
  let held = null;
  let armed = false;

  function flush() {
    armed = false;
    if (held === null) return;

    const camera = held;
    held = null;

    // Pass through the gate so the trailing send starts the next window.
    ready(now());
    send(camera);
  }

  return {
    /** The camera moved. */
    moved(camera) {
      if (ready(now())) {
        held = null;
        send(camera);

        return;
      }

      held = camera;

      if (!armed) {
        armed = true;
        schedule(flush, windowMs);
      }
    },
  };
}

/**
 * The control channel named by `?control=`, or the default for a missing or invalid name (the
 * service would refuse it forever).
 */
export function controlPin(location) {
  const asked = new URLSearchParams(location.search).get("control") ?? "";

  return CONTROL.test(asked) ? asked : DEFAULT_CONTROL;
}

/**
 * The camera socket's address for this page's booth, with the view token (browsers cannot set
 * handshake headers) and the channel in the query.
 */
export function cameraSocket(location, token) {
  const scheme = location.protocol === "https:" ? "wss:" : "ws:";
  const base = `${scheme}//${location.host}${boothBase(location)}${CAMERA_ROUTE}`;

  return `${base}?token=${encodeURIComponent(token)}&control=${encodeURIComponent(controlPin(location))}`;
}
