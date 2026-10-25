// feed.js is the front-end's connection to the booth: `GET /live`, the WebSocket fan-out behind the
// view token. It owns the token, the event cursor and reconnecting, and hands out one telemetry
// document at a time. `connect` and `schedule` are injectable for tests.

import { boothBase } from "./booth.js";

// The telemetry contract version this page accepts; documents from other versions are refused.
// Must match the C# encoder, the Go parser and `docs/telemetry/example.json` (checked in feed.test.js).
export const CONTRACT_VERSION = 6;

/**
 * Carries the tournament table across pushes that do not include one. Most pushes carry a null
 * table with an unchanged revision (keep the held table); a new revision with a null table means
 * the table was cleared. Pushes are forwarded verbatim, so the client must hold it too.
 * Mirrors `Store.holdStandings` in Go; feed.test.js and store_test.go assert the same cases.
 */
export function holdStandings(held, document) {
  const revision = document.standingsRevision ?? 0;

  if (!held || revision !== held.revision) {
    return { revision, standings: document.standings ?? null };
  }

  return document.standings ? { revision, standings: document.standings } : held;
}

// Reconnect backoff: first retry and ceiling.
const FIRST_RETRY_MS = 500;
const RETRY_CEILING_MS = 10_000;

// Close code a booth at capacity sends (RFC 6455 1013, "try again later"). The booth accepts the
// upgrade and then closes, because script cannot read the status of a failed handshake.
const AT_CAPACITY = 1013;

/** The live feed's address for the booth this page was served under. */
export function liveBase(location) {
  const scheme = location.protocol === "https:" ? "wss:" : "ws:";

  return `${scheme}//${location.host}${boothBase(location)}/live`;
}

/**
 * The address for one attempt. The token goes in the query because browsers cannot set headers on
 * a WebSocket handshake; the booth logs paths without the query.
 */
export function feedUrl(base, token, after) {
  const url = `${base}?token=${encodeURIComponent(token)}`;

  return after > 0 ? `${url}&after=${after}` : url;
}

/** How long before attempt n, doubling to a ceiling. */
export function backoffMs(attempt) {
  return Math.min(RETRY_CEILING_MS, FIRST_RETRY_MS * 2 ** Math.max(0, attempt - 1));
}

/**
 * The feed. Statuses: `blocked` (no token), `connecting`, `live` (a document arrived), `retrying`,
 * `full` (booth at capacity), `refused` (never connected: bad token or host), `mismatched`
 * (another contract version), `stopped`.
 */
export function createFeed({
  base,
  token,
  connect,
  schedule = (run, ms) => setTimeout(run, ms),
  onSnapshot,
  onStatus = () => {},
}) {
  let socket = null;
  let cursor = 0;
  let attempt = 0;
  let stopped = false;

  // Whether this attempt opened. A refused upgrade is invisible to script, so a close without an
  // open is the only sign of a bad token.
  let opened = false;

  // Whether any attempt ever worked. Once a token was accepted, later closes are restarts, not a
  // bad token.
  let everLive = false;

  // The held tournament table. Kept across reconnects so the panel does not blank.
  let held = null;

  function open() {
    if (stopped) return;

    if (!token) {
      onStatus("blocked", "paste the view token to watch the match");
      return;
    }

    attempt += 1;
    opened = false;
    onStatus("connecting", attempt > 1 ? `attempt ${attempt}` : "");

    socket = connect(feedUrl(base, token, cursor));

    socket.onopen = () => {
      opened = true;
    };

    socket.onmessage = (message) => receive(message.data);

    socket.onclose = (event) => {
      if (stopped) return;

      if (event?.code === AT_CAPACITY) {
        onStatus("full", "the booth is full — waiting for a free slot");
      } else if (opened || everLive) {
        onStatus("retrying", `reconnecting in ${Math.round(backoffMs(attempt) / 100) / 10}s`);
      } else {
        onStatus("refused", "no connection — check the view token, and that the booth is up");
      }

      // Always retry: a refusal may just be a booth that is still starting.
      schedule(open, backoffMs(attempt));
    };
  }

  function receive(data) {
    let document;

    try {
      document = JSON.parse(data);
    } catch {
      // Unparseable frame; the next push carries the whole state anyway.
      return;
    }

    if (document.v !== CONTRACT_VERSION) {
      onStatus(
        "mismatched",
        `the booth speaks contract v${document.v}, this page speaks v${CONTRACT_VERSION}`,
      );
      return;
    }

    advanceCursor(document.events);

    // Held here so every page interprets a null table the same way.
    held = holdStandings(held, document);

    // Reset backoff on the first document, not on open, so a booth that opens but never sends
    // still backs off.
    attempt = 0;
    everLive = true;
    onStatus("live", "");
    onSnapshot({ ...document, standings: held.standings, standingsRevision: held.revision });
  }

  function advanceCursor(events) {
    if (!events || events.length === 0) return;

    const highest = events.reduce((seen, event) => Math.max(seen, event.seq), 0);

    // Sequences restarting at 1 mean a restarted game server; reset the cursor or the next match
    // would be skipped.
    const restarted = events[0].seq === 1 && cursor > 1;

    cursor = restarted ? highest : Math.max(cursor, highest);
  }

  return {
    start: open,

    stop() {
      stopped = true;
      socket?.close();
      onStatus("stopped", "");
    },

    cursor: () => cursor,
  };
}
