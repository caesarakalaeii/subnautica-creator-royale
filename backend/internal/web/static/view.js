// view.js is the dashboard's pure logic (no DOM): formatting, ordering and the absent-versus-zero
// distinctions of the telemetry contract, all functions of one pushed document.

// Health, food and water are out of 100. Oxygen is in seconds, and its maximum depends on the tank.
const THRESHOLDS = {
  health: { critical: 25, low: 50 },
  food: { critical: 20, low: 40 },
  water: { critical: 20, low: 40 },
  oxygen: { critical: 10, low: 30 },
};

// Shown wherever a value is absent (as opposed to zero).
export const ABSENT = "—";

/** The match clock: m:ss, or h:mm:ss past an hour. */
export function clock(elapsedMs) {
  if (elapsedMs === null || elapsedMs === undefined) return ABSENT;

  const total = Math.max(0, Math.floor(elapsedMs / 1000));
  const seconds = total % 60;
  const minutes = Math.floor(total / 60) % 60;
  const hours = Math.floor(total / 3600);

  if (hours > 0) return `${hours}:${pad(minutes)}:${pad(seconds)}`;

  return `${minutes}:${pad(seconds)}`;
}

/**
 * The countdown to the next phase, as the server sent it. Null (lobby, pause, last phase) reads as
 * absent, never zero; the server owns the clock.
 */
export function countdown(untilNextMs) {
  if (untilNextMs === null || untilNextMs === undefined) return ABSENT;

  return clock(Math.max(0, untilNextMs));
}

/** Which of ok, low and critical a vital is in, by its own scale. */
export function vitalLevel(kind, value) {
  const thresholds = THRESHOLDS[kind];
  if (!thresholds || value === null || value === undefined) return "ok";

  if (value <= thresholds.critical) return "critical";
  if (value <= thresholds.low) return "low";

  return "ok";
}

/**
 * The living above the eliminated, each half by name. Never sorted by live values, so rows stay
 * put while casters read them.
 */
export function orderPlayers(players) {
  if (!players) return [];

  return [...players].sort((left, right) => {
    if (left.alive !== right.alive) return left.alive ? -1 : 1;

    return byName(left.name, right.name);
  });
}

/** Metres, rounded to whole metres. */
export function metres(value) {
  if (value === null || value === undefined) return ABSENT;

  return `${Math.round(value)} m`;
}

/** A sphere, written the way `zone:` in the config writes one. */
export function sphereText(sphere) {
  if (!sphere) return ABSENT;

  const centre = [sphere.x, sphere.y, sphere.z].map((axis) => Math.round(axis)).join(", ");

  return `${centre} · r ${Math.round(sphere.r)}`;
}

/** Whether a player is inside the wall, and how far past it if not. */
export function zoneStanding(player) {
  if (player.insideZone) return { inside: true, label: "in" };

  return { inside: false, label: `${metres(player.distanceOutside)} out` };
}

// The equipment slot that gets its own column; the rest goes in the cell's title.
const SUIT_SLOT = "Body";

/**
 * What a player is wearing: the body-slot item for the column, the whole set for the title. Item
 * names are the game's own, unmapped.
 */
export function suitWorn(equipment) {
  const worn = equipment ?? [];

  if (worn.length === 0) return { label: ABSENT, title: "wearing nothing" };

  const suit = worn.find((line) => line.slot === SUIT_SLOT);

  return {
    label: suit ? suit.item : ABSENT,
    title: worn.map((line) => `${line.slot}: ${line.item}`).join(" · "),
  };
}

/**
 * The stasis rifle: not carried, carried ("yes"), or charging (a percentage). A charge wins even
 * if the row says the rifle is not carried.
 */
export function rifleReading(player) {
  const charge = player.rifleCharge ?? 0;

  if (charge > 0) {
    return { label: `${Math.round(charge * 100)}%`, charging: true };
  }

  return { label: player.rifle ? "yes" : ABSENT, charging: false };
}

/** How many items a player is carrying, counting the stacks rather than the lines. */
export function stackCount(inventory) {
  if (!inventory) return 0;

  return inventory.reduce((total, line) => total + line.count, 0);
}

/** The highest audit sequence in a batch (the reconnect cursor); the max, not the last element. */
export function highestSequence(events) {
  if (!events) return 0;

  return events.reduce((highest, event) => Math.max(highest, event.seq), 0);
}

function byName(left, right) {
  return String(left).localeCompare(String(right), undefined, { sensitivity: "base" });
}

function pad(value) {
  return String(value).padStart(2, "0");
}

/**
 * The fraction of the current phase left, for the progress rule. The contract sends no phase
 * length, so the longest countdown seen in a phase stands in; pass the last result as `previous`.
 */
export function phaseProgress(previous, match) {
  const untilNextMs = match?.untilNextMs;

  // Nothing due (lobby, pause, last phase): an empty rule.
  if (untilNextMs === null || untilNextMs === undefined) {
    return { phase: null, lengthMs: 0, fraction: 0 };
  }

  const sameName = previous && previous.phase === match.phase;

  // Grows, never shrinks, so a page opened mid-phase or a longer countdown after a resume never
  // overfills the bar.
  const lengthMs = sameName ? Math.max(previous.lengthMs, untilNextMs) : untilNextMs;

  return {
    phase: match.phase,
    lengthMs,
    fraction: lengthMs > 0 ? Math.min(1, Math.max(0, untilNextMs / lengthMs)) : 0,
  };
}
