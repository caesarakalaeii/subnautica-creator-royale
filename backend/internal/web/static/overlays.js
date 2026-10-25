// overlays.js is the pure logic for the OBS browser sources: panel selection, the elimination toast
// queue (primed so a page opened mid-match does not replay old eliminations), and the match board,
// whose placement and scoring must match `MatchLedger` and `ScoringRules` in Lms.Core.

import { boothBase, storageKey } from "./booth.js";

// Scoring; must match `ScoringRules.Default` in Lms.Core.
export const POINTS_PER_PLAYER_OUTLASTED = 1;
export const POINTS_PER_KILL = 2;

// How long a toast stays up, and how many may show at once.
export const TOAST_MS = 6500;
export const TOAST_LIMIT = 3;

// The dashboard's view-token storage key, read as a fallback (scoped to the booth).
const TOKEN_KEY = "lms.viewToken";

// The only kinds a board keeps, so a whole match fits in a few records.
export const BOARD_KINDS = new Set(["PlayerEliminated", "KillAttributed", "MatchEnded"]);

/**
 * The panels an overlay page can show, with the browser-source size to configure in OBS. `map` is
 * full frame with a transparent background, for compositing over gameplay.
 */
export const PANELS = {
  status: "480 × 90",
  toasts: "560 × 260",
  board: "360 × 420",
  table: "400 × 520",
  map: "1920 × 1080",
};

/**
 * The panel named by `?panel=`, or "setup" for a missing or unknown one. `Object.hasOwn` so
 * `?panel=toString` does not match an inherited property.
 */
export function overlayPanel(location) {
  const asked = new URLSearchParams(location.search).get("panel");

  return asked !== null && Object.hasOwn(PANELS, asked) ? asked : "setup";
}

/**
 * One overlay's OBS address under this page's booth. Without a token it shows the
 * `YOUR_VIEW_TOKEN` placeholder.
 */
export function overlayURL(panel, location, token) {
  const secret = token ? encodeURIComponent(token) : "YOUR_VIEW_TOKEN";

  return `${location.origin}${boothBase(location)}/overlay.html?panel=${panel}&token=${secret}`;
}

/**
 * Whether a panel has anything to show for this push. Decided only by pushes, so a dropped
 * connection keeps the last frame. The table shows whenever there is a table (it is for between
 * matches); other panels need a match. Unknown panels (the setup card) return false.
 */
export function panelShowing(panel, snapshot) {
  if (panel === "table") return Boolean(snapshot.standings);

  return Object.hasOwn(PANELS, panel) && Boolean(snapshot.match);
}

/**
 * What the map source draws from a push, with absent lists as empty. Kept here because
 * overlay-page.js cannot be imported in tests.
 */
export function mapState(snapshot) {
  return { zone: snapshot.zone ?? null, players: snapshot.players ?? [], pods: snapshot.pods ?? [] };
}

/**
 * Map renderer options from the address. `shot` is not defaulted here; `framing()` in scene.js
 * handles absent and unknown names. Overlays are never driveable, so a stray click in OBS cannot
 * move the shot.
 */
export function mapOptions(location) {
  return {
    shot: new URLSearchParams(location.search).get("camera"),
    driveable: false,
  };
}

/**
 * The token an overlay presents: from the address (left in place, since a browser source has no
 * visible address bar and must survive reloads), else from browser storage.
 */
export function overlayToken(location, storage) {
  const fromLink = new URLSearchParams(location.search).get("token");

  return fromLink || storage[storageKey(TOKEN_KEY, location)] || "";
}

/**
 * Folds a push's events into the toasts to show. `nowMs` is passed in for testability. Call on
 * every push and on a timer, so toasts expire on their own.
 */
export function toastQueue(previous, events, nowMs, life = TOAST_MS) {
  const state = previous ?? { highest: 0, primed: false, toasts: [] };

  let toasts = state.toasts.filter((toast) => toast.until > nowMs);

  const list = events ?? [];

  // A log restarting at 1 is a restarted game server; prime again instead of re-toasting its replay.
  const restarted = list.length > 0 && list[0].seq === 1 && state.highest > 1;

  // The first message on a connection is the booth's whole window, so the first fold only takes
  // the watermark.
  const quiet = !state.primed || restarted;

  let highest = restarted ? 0 : state.highest;

  for (const event of list) {
    if (event.seq <= highest) continue;

    highest = event.seq;

    if (quiet) continue;

    const card = toast(event, nowMs, life);
    if (card) toasts = [card, ...toasts];
  }

  return { highest, primed: true, toasts: toasts.slice(0, TOAST_LIMIT) };
}

/**
 * Accumulates the board's events across pushes (each push carries only new events). Resets when the
 * game server restarts.
 */
export function keepEvents(previous, incoming) {
  const state = previous ?? { highest: 0, events: [] };
  const list = incoming ?? [];

  if (list.length === 0) return state;

  const restarted = list[0].seq === 1 && state.highest > 1;

  let highest = restarted ? 0 : state.highest;
  let events = restarted ? [] : state.events;

  for (const event of list) {
    if (event.seq <= highest) continue;

    highest = event.seq;

    if (!BOARD_KINDS.has(event.kind)) continue;

    events = [...events, event];
  }

  return { highest, events };
}

/**
 * Placements and points for the match in progress, mirroring `MatchLedger.Of` and
 * `ScoringRules.Default`: the named winner, then other survivors sharing a placement, then the
 * eliminated latest-out first. A point per player outlasted, two per attributed kill.
 * `match`, if given, filters events to that group and generation. Event order does not matter.
 */
export function matchBoard(players, events, match) {
  const roster = players ?? [];
  if (roster.length === 0) return [];

  const list = (events ?? []).filter((event) => belongsTo(event, match));
  const exits = new Map();
  const killedBy = new Map();
  let named = null;
  let namedAt = -1;

  for (const event of list) {
    if (event.kind === "PlayerEliminated" && event.player) {
      if (newer(event, exits.get(event.player))) exits.set(event.player, event);
    }

    // The newest attribution wins; corrections are appended, not edited.
    if (event.kind === "KillAttributed" && event.player) {
      if (newer(event, killedBy.get(event.player))) killedBy.set(event.player, event);
    }

    // The newest ending, so a replayed match does not leave an old winner at the top.
    if (event.kind === "MatchEnded" && event.seq > namedAt) {
      named = event.player ?? null;
      namedAt = event.seq;
    }
  }

  const kills = tally(roster, exits, killedBy);
  const alive = roster.filter((player) => player.alive);
  const dead = roster.filter((player) => !player.alive);

  const winner = alive.find((player) => player.name === named) ?? null;
  const survivors = alive.filter((player) => player !== winner).sort(byNameOrdinal);

  // Latest out ranks highest. A death with no event in the window sorts last.
  dead.sort((left, right) => {
    const leftSeq = exits.get(left.name)?.seq;
    const rightSeq = exits.get(right.name)?.seq;

    if (leftSeq === undefined && rightSeq === undefined) return byNameOrdinal(left, right);
    if (leftSeq === undefined) return 1;
    if (rightSeq === undefined) return -1;
    if (leftSeq !== rightSeq) return rightSeq - leftSeq;

    return byNameOrdinal(left, right);
  });

  // A block shares one placement, so everyone in it outlasted the same players.
  const blocks = [];
  if (winner) blocks.push([winner]);
  if (survivors.length > 0) blocks.push(survivors);
  for (const player of dead) blocks.push([player]);

  const rows = [];
  let placement = 1;

  for (const block of blocks) {
    const outlasted = roster.length - (placement - 1) - block.length;

    for (const player of block) {
      const scored = kills.get(player.name) ?? 0;
      const exit = exits.get(player.name);

      rows.push({
        name: player.name,
        alive: player.alive,
        placement,
        outlasted,
        kills: scored,
        points: outlasted * POINTS_PER_PLAYER_OUTLASTED + scored * POINTS_PER_KILL,
        by: exit?.actor ?? null,
        cause: exit?.detail ?? null,
      });
    }

    placement += block.length;
  }

  return rows;
}

/**
 * How a board row went out: "by <killer>", else the cause, else null (also null for the living).
 */
export function exitNote(row) {
  if (!row || row.alive) return null;

  if (row.by) return `by ${row.by}`;

  return row.cause ?? null;
}

/** What a toast says, or null for an event that gets none. */
function toast(event, nowMs, life) {
  const card = { key: `${event.seq}`, until: nowMs + life };

  if (event.kind === "PlayerEliminated") {
    return { ...card, kind: "out", text: `${event.player} — eliminated by ${blame(event)}` };
  }

  if (event.kind === "MatchEnded") {
    // Shown even when nobody won.
    return event.player
      ? { ...card, kind: "won", text: `${event.player} wins` }
      : { ...card, kind: "won", text: "nobody left standing" };
  }

  return null;
}

function blame(event) {
  if (event.actor) return event.actor;

  // `detail` is the log's cause, e.g. "zone".
  return event.detail === "zone" ? "the zone" : (event.detail ?? "nobody");
}

/**
 * Kills per player, derived from attributions so corrections move the points. A death's own
 * `actor` counts unless an explicit attribution overrides it.
 */
function tally(roster, exits, killedBy) {
  const kills = new Map();

  for (const player of roster) {
    // An explicit attribution wins, including an attribution to nobody.
    const killer = killedBy.has(player.name)
      ? (killedBy.get(player.name).actor ?? null)
      : (exits.get(player.name)?.actor ?? null);

    // Self-kills score nothing.
    if (!killer || killer === player.name) continue;

    kills.set(killer, (kills.get(killer) ?? 0) + 1);
  }

  return kills;
}

function belongsTo(event, match) {
  if (!match) return true;

  return event.group === match.group && event.generation === match.generation;
}

function newer(event, held) {
  return !held || event.seq > held.seq;
}

/** Ordinal tie-break, matching `MatchLedger`'s `StringComparer.Ordinal`. */
function byNameOrdinal(left, right) {
  if (left.name === right.name) return 0;

  return left.name < right.name ? -1 : 1;
}

/**
 * The tournament table ready to draw, or null when empty. Rows arrive already ordered and placed by
 * the mod, so they are not re-sorted here; this adds only the caption and the sole leader.
 */
export function tournamentTable(standings) {
  const entries = standings?.entries ?? [];
  if (entries.length === 0) return null;

  return {
    // Matches folded so far; the contract carries no total.
    caption: standings.matches === 1 ? "after 1 match" : `after ${standings.matches} matches`,

    // Only a sole leader is highlighted; a shared first place has none.
    lead: entries.length > 1 && entries[0].place === entries[1].place ? null : entries[0].name,

    rows: entries,
  };
}
