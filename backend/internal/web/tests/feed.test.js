import assert from "node:assert/strict";
import { beforeEach, describe, it } from "node:test";

import { CONTRACT_VERSION, backoffMs, createFeed, feedUrl, holdStandings, liveBase } from "../static/feed.js";

const BASE = "ws://booth.test/live";

function fakeSocket() {
  return {
    closed: false,
    close() {
      this.closed = true;
    },
    opens() {
      this.onopen?.({});
    },
    delivers(document) {
      this.onmessage?.({ data: JSON.stringify(document) });
    },
    deliversRaw(text) {
      this.onmessage?.({ data: text });
    },
    drops() {
      this.onclose?.({});
    },
    dropsWith(code, reason = "") {
      this.onclose?.({ code, reason });
    },
  };
}

function harness({ token = "view-secret" } = {}) {
  const urls = [];
  const sockets = [];
  const retries = [];
  const statuses = [];
  const snapshots = [];

  const feed = createFeed({
    base: BASE,
    token,
    connect(url) {
      urls.push(url);
      const socket = fakeSocket();
      sockets.push(socket);
      return socket;
    },
    schedule(run, ms) {
      retries.push({ run, ms });
    },
    onSnapshot(snapshot) {
      snapshots.push(snapshot);
    },
    onStatus(state, detail) {
      statuses.push({ state, detail });
    },
  });

  return {
    feed,
    urls,
    sockets,
    retries,
    statuses,
    snapshots,
    latest: () => sockets[sockets.length - 1],
    state: () => statuses[statuses.length - 1]?.state,
    retry: () => retries[retries.length - 1].run(),
  };
}

// The version comes off the constant so a bump moves the fixtures with it.
function snapshot(events = []) {
  return {
    v: CONTRACT_VERSION,
    at: "2026-10-24T12:34:56.789Z",
    match: null,
    zone: null,
    standingsRevision: 0,
    standings: null,
    players: [],
    events,
  };
}

describe("the live feed's address", () => {
  it("carries the view token in the query, because a browser cannot set a handshake header", () => {
    assert.equal(feedUrl(BASE, "view-secret", 0), "ws://booth.test/live?token=view-secret");
  });

  it("escapes a token that would otherwise end the query", () => {
    assert.equal(feedUrl(BASE, "a b&c=d", 0), "ws://booth.test/live?token=a%20b%26c%3Dd");
  });

  it("asks for everything after the last sequence it saw, once it has seen one", () => {
    assert.equal(feedUrl(BASE, "t", 43), "ws://booth.test/live?token=t&after=43");
  });

  it("is derived from wherever the page was served, so no host is configured twice", () => {
    assert.equal(liveBase({ protocol: "http:", host: "127.0.0.1:8080" }), "ws://127.0.0.1:8080/live");
    assert.equal(liveBase({ protocol: "https:", host: "sn.caes.ar" }), "wss://sn.caes.ar/live");
  });

  it("is the feed of whichever booth the page is on, not the tournament's", () => {
    assert.equal(
      liveBase({ protocol: "https:", host: "sn.caes.ar", pathname: "/b/mzxw6ytb/" }),
      "wss://sn.caes.ar/b/mzxw6ytb/live",
    );
  });
});

describe("connecting", () => {
  it("says it is connecting before it says anything else", () => {
    const live = harness();
    live.feed.start();

    assert.equal(live.statuses[0].state, "connecting");
    assert.equal(live.urls.length, 1);
  });

  it("refuses to connect at all without a token, and says which is missing", () => {
    const live = harness({ token: "" });
    live.feed.start();

    assert.equal(live.state(), "blocked");
    assert.equal(live.urls.length, 0);
  });

  it("is live once the first document arrives, not merely once the socket opens", () => {
    const live = harness();
    live.feed.start();
    live.latest().opens();

    assert.notEqual(live.state(), "live");

    live.latest().delivers(snapshot());

    assert.equal(live.state(), "live");
    assert.equal(live.snapshots.length, 1);
  });
});

describe("the cursor", () => {
  let live;

  beforeEach(() => {
    live = harness();
    live.feed.start();
    live.latest().opens();
  });

  it("advances to the highest sequence seen, and a reconnect resumes after it", () => {
    live.latest().delivers(snapshot([{ seq: 41 }, { seq: 43 }]));
    live.latest().drops();
    live.retry();

    assert.equal(live.urls[1], "ws://booth.test/live?token=view-secret&after=43");
  });

  it("never goes backwards over a push that carried no events", () => {
    live.latest().delivers(snapshot([{ seq: 43 }]));
    live.latest().delivers(snapshot([]));
    live.latest().drops();
    live.retry();

    assert.equal(live.urls[1], "ws://booth.test/live?token=view-secret&after=43");
  });

  it("resets when the sequence starts again, because that is a restarted game server", () => {
    live.latest().delivers(snapshot([{ seq: 400 }]));
    live.latest().delivers(snapshot([{ seq: 1 }, { seq: 2 }]));
    live.latest().drops();
    live.retry();

    assert.equal(live.urls[1], "ws://booth.test/live?token=view-secret&after=2");
  });
});

describe("when it goes wrong", () => {
  it("comes straight back after a connection that had been working", () => {
    const live = harness();
    live.feed.start();
    live.latest().opens();
    live.latest().delivers(snapshot());
    live.latest().drops();

    assert.equal(live.state(), "retrying");
    assert.equal(live.retries[0].ms, backoffMs(1));
  });

  it("hands the page nothing to draw when the connection drops, so the last frame stays up", () => {
    const live = harness();
    live.feed.start();
    live.latest().opens();
    live.latest().delivers(snapshot());

    const drawn = live.snapshots.length;
    live.latest().drops();
    live.retry();
    live.latest().drops();

    assert.equal(live.snapshots.length, drawn, "a drop must not reach the page as a frame");
  });

  it("backs off across attempts that keep failing, rather than hammering the booth", () => {
    const live = harness();
    live.feed.start();

    for (const expected of [backoffMs(1), backoffMs(2), backoffMs(3)]) {
      live.latest().opens();
      live.latest().drops();

      assert.equal(live.retries[live.retries.length - 1].ms, expected);
      live.retry();
    }
  });

  it("backs off to a ceiling, so a booth that is down for an hour is polled not flooded", () => {
    assert.equal(backoffMs(1), 500);
    assert.equal(backoffMs(2), 1000);
    assert.equal(backoffMs(3), 2000);
    assert.ok(backoffMs(20) <= 10_000);
    assert.equal(backoffMs(20), backoffMs(50));
  });

  it("blames the token when the socket closes without ever having opened", () => {
    const live = harness();
    live.feed.start();
    live.latest().drops();

    assert.equal(live.state(), "refused");
  });

  it("keeps retrying after a refusal, because the booth may simply not be up yet", () => {
    const live = harness();
    live.feed.start();
    live.latest().drops();

    assert.equal(live.retries.length, 1);
  });

  it("does not ask for the token again once a feed has worked", () => {
    const live = harness();
    live.feed.start();
    live.latest().opens();
    live.latest().delivers(snapshot());
    live.latest().drops();
    live.retry();
    live.sockets[1].drops();

    assert.equal(live.state(), "retrying");
  });

  it("refuses a document from a build it does not speak, rather than drawing half a match", () => {
    const live = harness();
    live.feed.start();
    live.latest().opens();
    live.latest().delivers({ ...snapshot(), v: CONTRACT_VERSION + 1 });

    assert.equal(live.snapshots.length, 0);
    assert.equal(live.state(), "mismatched");
  });

  it("survives a message that is not JSON at all", () => {
    const live = harness();
    live.feed.start();
    live.latest().opens();
    live.latest().deliversRaw("<html>a proxy error page</html>");
    live.latest().delivers(snapshot());

    assert.equal(live.snapshots.length, 1);
    assert.equal(live.state(), "live");
  });

  it("stops when it is told to, and does not schedule another attempt", () => {
    const live = harness();
    live.feed.start();
    live.latest().opens();
    live.feed.stop();

    assert.ok(live.latest().closed);

    live.latest().drops();

    assert.equal(live.retries.length, 0);
  });
});

describe("a booth that is full", () => {
  // The client cap accepts the socket then closes it with 1013, so the close code is the only way to tell "no room" from "wrong token".
  it("says the booth is full rather than blaming the token", () => {
    const feed = harness();
    feed.feed.start();

    feed.latest().opens();
    feed.latest().dropsWith(1013, "the booth is at capacity");

    assert.equal(feed.state(), "full");
  });

  it("says so even on an attempt that never opened", () => {
    const feed = harness();
    feed.feed.start();

    feed.latest().dropsWith(1013, "the booth is at capacity");

    assert.equal(feed.state(), "full");
  });

  it("keeps retrying, because a slot frees up when somebody closes a tab", () => {
    const feed = harness();
    feed.feed.start();

    feed.latest().opens();
    feed.latest().dropsWith(1013, "the booth is at capacity");

    assert.equal(feed.retries.length, 1, "a full booth must be retried, not given up on");

    feed.retry();
    feed.latest().opens();
    feed.latest().delivers(snapshot());

    assert.equal(feed.state(), "live");
  });

  it("still blames the token when the close carries no code at all", () => {
    const feed = harness();
    feed.feed.start();

    feed.latest().drops();

    assert.equal(feed.state(), "refused");
  });

  it("does not mistake an ordinary close for a full booth", () => {
    const feed = harness();
    feed.feed.start();

    feed.latest().opens();
    feed.latest().dropsWith(1006, "");

    assert.equal(feed.state(), "retrying");
  });
});

describe("the version this page speaks", () => {
  it("is the version the committed contract carries", async () => {
    const url = new URL("../../../../docs/telemetry/example.json", import.meta.url);
    const { readFile } = await import("node:fs/promises");
    const example = JSON.parse(await readFile(url, "utf8"));

    assert.equal(
      CONTRACT_VERSION,
      example.v,
      "docs/telemetry/example.json is the contract, and this page is one of its three ends",
    );
  });
});

describe("holding the tournament table", () => {
  const table = (matches) => ({ matches, entries: [{ place: 1, name: "Vela", points: 4 }] });

  it("takes the first table it is given", () => {
    const held = holdStandings(null, { standingsRevision: 1, standings: table(1) });

    assert.equal(held.revision, 1);
    assert.equal(held.standings.matches, 1);
  });

  it("keeps the table through a push that does not carry one", () => {
    const first = holdStandings(null, { standingsRevision: 1, standings: table(1) });
    const quiet = holdStandings(first, { standingsRevision: 1, standings: null });

    assert.equal(quiet.standings.matches, 1);
  });

  it("clears the table when a new revision carries none", () => {
    const first = holdStandings(null, { standingsRevision: 1, standings: table(1) });
    const cleared = holdStandings(first, { standingsRevision: 2, standings: null });

    assert.equal(cleared.standings, null);
    assert.equal(cleared.revision, 2);
  });

  it("takes a table that arrives on an unchanged revision, which is the heartbeat", () => {
    const started = holdStandings(null, { standingsRevision: 7, standings: null });
    const beat = holdStandings(started, { standingsRevision: 7, standings: table(4) });

    assert.equal(beat.standings.matches, 4);
  });

  it("has nothing to hold before the first match finishes", () => {
    const held = holdStandings(null, { standingsRevision: 0, standings: null });

    assert.equal(held.standings, null);
    assert.equal(held.revision, 0);
  });

  it("survives a document from a build that has no table in it at all", () => {
    const held = holdStandings({ revision: 0, standings: null }, { v: 2 });

    assert.equal(held.revision, 0);
    assert.equal(held.standings, null);
  });
});

describe("what a page is handed", () => {
  it("carries the held table, not the null block the push actually had", () => {
    const live = harness();
    live.feed.start();
    live.latest().opens();
    live.latest().delivers({
      ...snapshot(),
      standingsRevision: 1,
      standings: { matches: 1, entries: [{ place: 1, name: "Vela", points: 4 }] },
    });
    live.latest().delivers({ ...snapshot(), standingsRevision: 1, standings: null });

    assert.equal(live.snapshots.length, 2);
    assert.equal(live.snapshots[1].standings.entries[0].name, "Vela",
      "a push that carried no table must not blank the panel");
  });
});
