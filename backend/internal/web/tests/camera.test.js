import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { describe, it } from "node:test";
import { fileURLToPath } from "node:url";

import {
  CAMERA_VERSION,
  CONTROL,
  DEFAULT_CONTROL,
  DRIVER_PROTOCOL,
  cameraDocument,
  cameraFrom,
  cameraSocket,
  controlPin,
  createPublisher,
  createCameraLink,
  handCamera,
  mayDrive,
  onAChannel,
} from "../static/camera.js";
import { DEFAULT_FRAMING, framing } from "../static/scene.js";

const shot = { position: { x: 10, y: 20, z: 30 }, target: { x: 1, y: 2, z: 3 }, cut: 400 };

describe("a camera on the wire", () => {
  it("survives the trip out and back unchanged", () => {
    assert.deepEqual(cameraFrom(cameraDocument(shot)), shot);
  });

  it("carries its own version, which is not the telemetry contract's", () => {
    assert.equal(cameraDocument(shot).v, CAMERA_VERSION);
  });

  it("is nothing at all when the document is from another build", () => {
    assert.equal(cameraFrom({ ...cameraDocument(shot), v: CAMERA_VERSION + 1 }), null);
    assert.equal(cameraFrom(null), null);
    assert.equal(cameraFrom({}), null);
  });

  it("is nothing at all when a coordinate is not a finite number", () => {
    const broken = cameraDocument(shot);
    broken.position.x = Number.NaN;

    assert.equal(cameraFrom(broken), null);

    const infinite = cameraDocument(shot);
    infinite.target.z = Number.POSITIVE_INFINITY;

    assert.equal(cameraFrom(infinite), null);
  });

  it("refuses a cut that is not a depth, because a negative one would lift the plane above the sea", () => {
    const lifted = cameraDocument(shot);
    lifted.cut = -1;

    assert.equal(cameraFrom(lifted), null);
  });
});

describe("a follower that has heard from nobody", () => {
  it("draws the establishing shot rather than nothing", () => {
    const opening = framing(null);

    assert.equal(opening.name, DEFAULT_FRAMING);
    assert.deepEqual(cameraFrom(cameraDocument({ ...opening, cut: 0 })), {
      position: opening.position,
      target: opening.target,
      cut: 0,
    });
  });
});

// Duck-typed three.js objects: a position, a target and a plane's constant.
const fakeVector = (x = 0, y = 0, z = 0) => ({
  x,
  y,
  z,
  set(nx, ny, nz) {
    this.x = nx;
    this.y = ny;
    this.z = nz;
  },
});

const fakeRenderer = () => {
  const state = {
    camera: { position: fakeVector() },
    controls: { target: fakeVector(), updated: 0, update() { state.controls.updated += 1; } },
    depth: 0,
    cut(depth) {
      state.depth = depth;
    },
  };

  return state;
};

describe("the camera on the map's own handle", () => {
  it("gives back what it was given, round-tripped through the handle", () => {
    const renderer = fakeRenderer();
    const hand = handCamera(renderer.camera, renderer.controls, (depth) => renderer.cut(depth));

    hand.aim(shot);

    assert.deepEqual(hand.look(), shot);
  });

  it("moves the renderer's own camera rather than a copy of it", () => {
    const renderer = fakeRenderer();
    const hand = handCamera(renderer.camera, renderer.controls, (depth) => renderer.cut(depth));

    hand.aim(shot);

    assert.deepEqual(
      { x: renderer.camera.position.x, y: renderer.camera.position.y, z: renderer.camera.position.z },
      shot.position,
    );
    assert.deepEqual(
      { x: renderer.controls.target.x, y: renderer.controls.target.y, z: renderer.controls.target.z },
      shot.target,
    );
    assert.equal(renderer.depth, shot.cut, "the cut did not travel with the camera");
  });

  it("reports a cut moved on its own, because the slider is part of the shot", () => {
    const renderer = fakeRenderer();
    const hand = handCamera(renderer.camera, renderer.controls, (depth) => renderer.cut(depth));

    hand.cutTo(600);

    assert.equal(hand.look().cut, 600);
    assert.equal(renderer.depth, 600);
  });

  it("tells the controls the camera moved, or the next mouse drag snaps it back", () => {
    const renderer = fakeRenderer();
    const hand = handCamera(renderer.camera, renderer.controls, (depth) => renderer.cut(depth));

    hand.aim(shot);

    assert.ok(renderer.controls.updated > 0, "the controls were not told the camera moved");
  });
});

describe("the publisher's own trailing message", () => {
  const harness = () => {
    const sent = [];
    const timers = [];
    let clockMs = 0;

    const publisher = createPublisher({
      windowMs: 100,
      send: (camera) => sent.push(camera.position.x),
      now: () => clockMs,
      schedule: (run, ms) => timers.push({ run, ms }),
    });

    return {
      sent,
      timers,
      at(ms) {
        clockMs = ms;
      },
      moved(x) {
        publisher.moved({ ...shot, position: { ...shot.position, x } });
      },
      fire() {
        timers.pop().run();
      },
    };
  };

  it("sends where the gesture ended, not where its last window did", () => {
    const publishing = harness();

    publishing.moved(1);
    publishing.at(20);
    publishing.moved(2);
    publishing.at(40);
    publishing.moved(3);

    assert.deepEqual(publishing.sent, [1], "the burst was not throttled");

    publishing.at(100);
    publishing.fire();

    assert.deepEqual(publishing.sent, [1, 3], "where the caster stopped never went out");
  });

  it("says nothing more when the gesture ended on a message that was sent", () => {
    const publishing = harness();

    publishing.moved(1);
    publishing.at(100);
    publishing.moved(2);

    assert.deepEqual(publishing.sent, [1, 2]);

    for (const timer of publishing.timers.splice(0)) timer.run();

    assert.deepEqual(publishing.sent, [1, 2], "a camera that had already gone out was sent again");
  });
});

describe("the throttle in front of the publisher", () => {
  const harness = () => {
    const sent = [];
    let clockMs = 0;

    const publisher = createPublisher({
      windowMs: 100,
      send: (camera) => sent.push(camera.position.x),
      now: () => clockMs,
      schedule: () => {},
    });

    return {
      sent,
      at(ms) {
        clockMs = ms;
      },
      moved(x) {
        publisher.moved({ ...shot, position: { ...shot.position, x } });
      },
    };
  };

  it("passes the first move at once, so a small nudge is not held back", () => {
    const publishing = harness();

    publishing.moved(1);

    assert.deepEqual(publishing.sent, [1]);
  });

  it("turns a burst inside one window into a single message", () => {
    const publishing = harness();

    for (let at = 0; at < 100; at += 4) {
      publishing.at(at);
      publishing.moved(at);
    }

    assert.equal(publishing.sent.length, 1);
  });

  it("passes the next one once the window has gone by", () => {
    const publishing = harness();

    publishing.moved(1);
    publishing.at(99);
    publishing.moved(2);
    publishing.at(100);
    publishing.moved(3);

    assert.deepEqual(publishing.sent, [1, 3]);
  });
});

describe("whether the dashboard drives at all", () => {
  const operator = { "lms.operatorToken": "an-operator-secret" };

  it("drives when the address asks it to and this machine holds the operator token", () => {
    assert.equal(
      mayDrive(new URL("https://booth/?control=desk-1"), operator),
      "an-operator-secret",
    );
  });

  it("does not drive unless the address asks, so an ordinary dashboard takes no channel", () => {
    assert.equal(mayDrive(new URL("https://booth/"), operator), "");
  });

  it("does not drive on a machine that holds only the view token", () => {
    assert.equal(mayDrive(new URL("https://booth/?control=desk-1"), {}), "");
  });

  it("drives a booth's camera with the booth's own operator token, never the tournament's", () => {
    const both = {
      "lms.operatorToken": "tournament-secret",
      "lms.operatorToken:ada": "ada-secret",
    };

    assert.equal(mayDrive(new URL("https://booth/b/ada/?control=desk-1"), both), "ada-secret");
  });
});

function fakeSocket() {
  return {
    sent: [],
    closed: false,
    protocols: [],
    send(payload) {
      this.sent.push(payload);
    },
    close() {
      this.closed = true;
    },
    opens() {
      this.onopen?.({});
    },
    delivers(document) {
      this.onmessage?.({ data: JSON.stringify(document) });
    },
    dropsWith(code = 1006) {
      this.onclose?.({ code });
    },
  };
}

function linkHarness({ token = "a-secret", driving = false } = {}) {
  const sockets = [];
  const cameras = [];
  const retries = [];

  const link = createCameraLink({
    url: "wss://booth.test/camera?token=a-secret&control=desk",
    driving,
    token,
    connect(url, protocols) {
      const socket = fakeSocket();
      socket.url = url;
      socket.protocols = protocols ?? [];
      sockets.push(socket);

      return socket;
    },
    schedule(run) {
      retries.push(run);
    },
    onCamera(camera) {
      cameras.push(camera);
    },
  });

  link.start();

  return {
    link,
    cameras,
    retries,
    latest: () => sockets[sockets.length - 1],
    sockets,
  };
}

describe("a page following the camera", () => {
  it("hands over a camera it was sent", () => {
    const following = linkHarness();

    following.latest().opens();
    following.latest().delivers(cameraDocument(shot));

    assert.deepEqual(following.cameras, [shot]);
  });

  it("holds its last camera when the publisher goes away", () => {
    const following = linkHarness();

    following.latest().opens();
    following.latest().delivers(cameraDocument(shot));
    following.latest().dropsWith();

    assert.deepEqual(following.cameras, [shot], "a drop moved the camera");
  });

  it("comes back after a drop, because a booth rolls mid-match", () => {
    const following = linkHarness();

    following.latest().opens();
    following.latest().dropsWith();

    assert.equal(following.retries.length, 1);
    following.retries[0]();

    assert.equal(following.sockets.length, 2);
  });

  it("draws nothing from a document it cannot read", () => {
    const following = linkHarness();

    following.latest().opens();
    following.latest().delivers({ v: CAMERA_VERSION + 1, position: {}, target: {}, cut: 0 });

    assert.deepEqual(following.cameras, []);
  });

  it("offers no subprotocol, so it presents no write credential at all", () => {
    const following = linkHarness();

    assert.deepEqual(following.latest().protocols, []);
  });
});

describe("a page driving the camera", () => {
  it("offers the operator token as the negotiated subprotocol, never in the address", () => {
    // A write credential goes in the WebSocket subprotocol, not the query string, which lands in proxy access logs.
    const driving = linkHarness({ driving: true, token: "an-operator-secret" });

    assert.deepEqual(driving.latest().protocols, ["lms.camera.operator", "an-operator-secret"]);
    assert.ok(
      !driving.latest().url.includes("an-operator-secret"),
      "the operator token is in the address, where every proxy logs it",
    );
  });

  it("sends what it is given, once the socket is open", () => {
    const driving = linkHarness({ driving: true });

    driving.link.publish(shot);
    assert.deepEqual(driving.latest().sent, [], "a camera went out before the socket opened");

    driving.latest().opens();
    driving.link.publish(shot);

    assert.deepEqual(driving.latest().sent.map((text) => JSON.parse(text)), [cameraDocument(shot)]);
  });

  it("draws nothing itself, because the driver is what everybody else is following", () => {
    const driving = linkHarness({ driving: true });

    driving.latest().opens();
    driving.latest().delivers(cameraDocument(shot));

    assert.deepEqual(driving.cameras, []);
  });
});

describe("whether a page is on a control channel at all", () => {
  it("is on one only when its address pins it to one", () => {
    assert.equal(onAChannel(new URL("https://booth/overlay.html?panel=map&control=desk-1")), true);
    assert.equal(onAChannel(new URL("https://booth/overlay.html?panel=map")), false);
  });

  it("is on one even when the name is junk, because a typo is not a reason to draw nothing", () => {
    assert.equal(onAChannel(new URL("https://booth/overlay.html?control=A%20desk")), true);
  });
});

describe("which control channel a page is on", () => {
  it("is what the address pins it to, because a source has no other configuration", () => {
    assert.equal(controlPin(new URL("https://booth/overlay.html?control=desk-1")), "desk-1");
  });

  it("is the documented default when the address names none", () => {
    assert.equal(controlPin(new URL("https://booth/overlay.html")), DEFAULT_CONTROL);
    assert.equal(controlPin(new URL("https://booth/overlay.html?control=")), DEFAULT_CONTROL);
  });

  it("is the default rather than junk, because the pin is a path-safe name", () => {
    assert.equal(controlPin(new URL("https://booth/o.html?control=A%20desk")), DEFAULT_CONTROL);
    assert.equal(controlPin(new URL(`https://booth/o.html?control=${"x".repeat(41)}`)), DEFAULT_CONTROL);
  });

  it("addresses the booth the page is on, and never the tournament's own", () => {
    const url = cameraSocket(
      new URL("https://booth.example/b/ada/overlay.html?control=desk-1"),
      "a-token",
    );

    assert.match(url, /^wss:\/\/booth\.example\/b\/ada\/camera\?/);
    assert.match(url, /token=a-token/);
    assert.match(url, /control=desk-1/);
  });

  it("speaks ws where the page is not https, so a local booth works with no certificate", () => {
    assert.match(cameraSocket(new URL("http://127.0.0.1:8099/"), "t"), /^ws:\/\/127\.0\.0\.1:8099\//);
  });
});

describe("the two ends of the camera channel", () => {
  // The Go source is read as text: these constants exist in both languages with no shared file.
  const service = readFileSync(
    fileURLToPath(new URL("../../caster/camera.go", import.meta.url)),
    "utf8",
  );

  const constant = (name) => service.match(new RegExp(`${name}\\s*=\\s*(\\S+)`))?.[1];

  it("speak the same document version", () => {
    assert.equal(String(CAMERA_VERSION), constant("cameraVersion"));
  });

  it("name the driver's subprotocol the same thing", () => {
    assert.equal(`"${DRIVER_PROTOCOL}"`, constant("cameraDriverProtocol"));
  });

  it("agree on what a control channel may be called", () => {
    const inService = service.match(/validControl = regexp\.MustCompile\(`([^`]+)`\)/)?.[1];

    assert.equal(CONTROL.source, inService);
  });
});
