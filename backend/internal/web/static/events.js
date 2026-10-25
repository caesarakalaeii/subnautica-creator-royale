// events.js turns audit-log records into a readable caster timeline. Pure (no DOM): it tracks which
// events were taken, which collapse into one line, and what each line says. Kinds use the audit
// log's own names.

// Never drawn: position samples dominate the log and belong in the player table.
export const HIDDEN_KINDS = new Set(["PlayerPosition"]);

// Lines kept in the browser.
export const FEED_LIMIT = 400;

// How long a run of bites stays open. Bites tick every two seconds while outside the zone.
export const BITE_WINDOW_MS = 30_000;

// The kind whose lines are counted rather than listed.
const BITE = "ZoneBite";

const TONES = {
  PlayerEliminated: "out",
  KillAttributed: "out",
  MatchEnded: "out",

  ZoneBite: "zone",
  ZoneWarned: "zone",
  ZoneClosed: "zone",

  // Zone damage charged on return to a player who was away; one line per absence.
  ZoneArrears: "zone",

  PhaseStarted: "phase",
  PhaseWarned: "phase",

  RingFired: "ring",
  PlayerCaged: "ring",
  PlayerReleased: "ring",

  // Player-on-player damage, any weapon.
  PlayerHurt: "fight",

  PlayerJoined: "quiet",
  PlayerLeft: "quiet",
};

// Verb per `PlayerHurt` weapon (`detail` is the mod's StrikeWeapons.Word). A Map, not an object
// literal, so a wire value like "toString" cannot resolve to an inherited property.
const HURT_VERBS = new Map([
  ["claw", "clawed"],
  ["drill", "drilled"],
  ["knife", "knifed"],
  ["heatblade", "burned"],
]);

// Verb for a weapon this page does not know (e.g. a newer mod build).
const HURT_FALLBACK = "hit";

// Line text per kind. Each takes the entry, so a coalesced run can say how long it lasted.
const LINES = {
  MatchStarted: () => "match started",
  MatchPaused: () => "paused",
  MatchResumed: () => "resumed",
  MatchSkipped: () => "a phase was brought forward",
  MatchReset: () => "match reset",
  GroupAdvanced: (entry) => `group ${entry.group} is up`,
  PhaseWarned: (entry) => `phase ${entry.phase} is coming`,
  PhaseStarted: (entry) => `phase ${entry.phase} — the wall is closing`,
  ZoneClosed: () => "the zone has closed",
  MatchEnded: (entry) => (entry.player ? `${entry.player} wins` : "match over, nobody left standing"),

  PlayerJoined: (entry) => `${entry.player} joined`,
  PlayerLeft: (entry) => `${entry.player} left`,
  ZoneWarned: (entry) => `${entry.player} is outside where the wall is going`,
  // `detail` is the mod's "away Ns".
  ZoneArrears: (entry) =>
    `${entry.player}${entry.detail ? ` ${entry.detail}` : " was away"} — the wall took ${round(entry.amount)}`,

  [BITE]: (entry) =>
    `${entry.player} in the wall — ${round(entry.amount)} over ${entry.ticks} ` +
    `${entry.ticks === 1 ? "tick" : "ticks"}`,

  PlayerEliminated: (entry) =>
    `${entry.player} eliminated${entry.actor ? ` by ${entry.actor}` : ""}` +
    `${entry.detail ? ` — ${entry.detail}` : ""}`,

  // Written by an operator after the fact, e.g. crediting a player who caged someone in the wall.
  KillAttributed: (entry) =>
    entry.actor
      ? `${entry.actor} credited with ${entry.player}`
      : `${entry.player}'s death credited to nobody`,

  // `amount` here is the trigger charge (0..1), not damage.
  RingFired: (entry) => `${entry.player} fired a ring${charge(entry)}`,
  PlayerCaged: (entry) => `${entry.player} caged${entry.actor ? ` by ${entry.actor}` : ""}`,
  PlayerReleased: (entry) => `${entry.player} is out of the ring`,

  // The server always sets the actor on this kind.
  PlayerHurt: (entry) =>
    `${entry.actor} ${HURT_VERBS.get(entry.detail) ?? HURT_FALLBACK} ` +
    `${entry.player} — ${round(entry.amount)}`,
};

/**
 * Folds one push's events into the feed. `previous` is the last result, or null to start. Returns
 * lines newest first, the highest sequence taken (the reconnect cursor), and an era that changes
 * when the game server restarts.
 */
export function mergeEvents(previous, incoming, limit = FEED_LIMIT) {
  const state = previous ?? { era: 0, highest: 0, entries: [] };

  if (!incoming || incoming.length === 0) return state;

  // Sequences are per log file, so a log starting again at 1 is a restarted game server, not a
  // duplicate push.
  const restarted = incoming[0].seq === 1 && state.highest > 1;
  const era = restarted ? state.era + 1 : state.era;

  let highest = restarted ? 0 : state.highest;
  let entries = state.entries;

  for (const event of incoming) {
    if (event.seq <= highest) continue;

    // Advance even for hidden kinds so they are not re-read on every push.
    highest = event.seq;

    if (HIDDEN_KINDS.has(event.kind)) continue;

    const run = event.kind === BITE ? openBiteRun(entries, event, era) : -1;

    if (run >= 0) {
      // Copy rather than mutate: the caller still holds the previous state.
      entries = [...entries];
      entries[run] = absorb(entries[run], event);
      continue;
    }

    entries = [entry(event, era), ...entries];
  }

  return { era, highest, entries: entries.length > limit ? entries.slice(0, limit) : entries };
}

/** What one line says. */
export function eventLine(entry) {
  const write = LINES[entry.kind];
  if (write) return write(entry);

  // Unknown kind (e.g. a newer mod build): show the raw kind name.
  return entry.player ? `${entry.kind} — ${entry.player}` : entry.kind;
}

/** How loud a line is, as a class for the stylesheet. */
export function eventTone(kind) {
  return TONES[kind] ?? "match";
}

/** Which match a line belongs to, taken from the event itself. */
export function matchLabel(entry) {
  return `group ${entry.group} · run ${entry.generation}`;
}

/**
 * The label above `entry` given the newer line above it, or null for no break. A label goes on the
 * newest line, where the group or run changes, and where the game server restarted.
 */
export function divider(entry, newer) {
  if (!newer) return matchLabel(entry);

  if (newer.era !== entry.era) return `${matchLabel(entry)} · before the restart`;

  return matchLabel(newer) === matchLabel(entry) ? null : matchLabel(entry);
}

/**
 * Index of an open bite run for this player, or -1. Stops at the first line outside the time
 * window or from another match, since a new match restarts the elapsed clock.
 */
function openBiteRun(entries, event, era) {
  for (let i = 0; i < entries.length; i += 1) {
    const candidate = entries[i];

    if (candidate.era !== era) return -1;
    if (candidate.group !== event.group || candidate.generation !== event.generation) return -1;
    if (event.elapsedMs - candidate.elapsedMs > BITE_WINDOW_MS) return -1;
    if (candidate.kind === BITE && candidate.player === event.player) return i;
  }

  return -1;
}

function entry(event, era) {
  return {
    // A run's first sequence; stable so the rendered line keeps its DOM node as the run grows.
    key: `${era}:${event.seq}`,
    era,
    seq: event.seq,

    // Display time. `elapsedMs` advances to a run's latest tick for the window check.
    startedMs: event.elapsedMs,
    group: event.group,
    generation: event.generation,
    kind: event.kind,
    at: event.at,
    elapsedMs: event.elapsedMs,
    player: event.player,
    actor: event.actor,
    phase: event.phase,
    detail: event.detail,
    biome: event.biome,
    amount: event.amount ?? 0,
    ticks: 1,
  };
}

function absorb(run, event) {
  return {
    ...run,
    seq: event.seq,
    elapsedMs: event.elapsedMs,
    at: event.at,
    amount: run.amount + (event.amount ?? 0),
    ticks: run.ticks + 1,
  };
}

function round(value) {
  return Math.round(value * 10) / 10;
}

// Ring charge as words: under 5% is a tap, 100% is a full charge, otherwise a percentage.
function charge(entry) {
  if (!entry.amount) return "";
  if (entry.amount >= 1) return " — full charge";
  if (entry.amount < 0.05) return " — a tap";

  return ` — ${Math.round(entry.amount * 100)}%`;
}
