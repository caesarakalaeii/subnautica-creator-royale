// tournament.js is the pure logic of the tournament overview: the fixed timetable, what is on at a
// given second, and the bracket's stages filled from the booth's groups. tournament-page.js draws it.
// The timetable is static; the players come from `GET /bracket`.

/** One slot: unix seconds, `end` null for the slot nothing follows, `group` on a game. */
const slot = ([start, end, title, group]) => ({ start, end, title, group });

/**
 * The published event schedule. A game's `group` is its number in the booth's bracket and on the
 * game server, where `/lms next` counts through them in this order.
 */
export const SCHEDULE = [
  {
    day: 1,
    slots: [
      [1792843200, 1792844100, "Introductions"],
      [1792844100, 1792846200, "Group 1 · game 1", 1],
      [1792846200, 1792846800, "Intermission"],
      [1792846800, 1792848900, "Group 1 · game 2", 2],
      [1792848900, 1792849500, "Intermission"],
      [1792849500, 1792851600, "Group 1 · game 3", 3],
      [1792851600, 1792852200, "Intermission"],
      [1792852200, 1792854300, "Group 2 · game 1", 4],
      [1792854300, 1792854900, "Intermission"],
      [1792854900, 1792857000, "Group 2 · game 2", 5],
      [1792857000, 1792857600, "Intermission"],
      [1792857600, 1792859700, "Group 2 · game 3", 6],
      [1792859700, 1792860300, "Intermission"],
      [1792860300, 1792862400, "Group 3 · game 1", 7],
      [1792862400, 1792863000, "Intermission"],
      [1792863000, 1792865100, "Group 3 · game 2", 8],
      [1792865100, 1792865700, "Intermission"],
      [1792865700, 1792867800, "Group 3 · game 3", 9],
      [1792867800, 1792868400, "Intermission"],
      [1792868400, 1792870500, "Group 4 · game 1", 10],
      [1792870500, 1792871100, "Intermission"],
      [1792871100, 1792873200, "Group 4 · game 2", 11],
      [1792873200, 1792873800, "Intermission"],
      [1792873800, 1792875900, "Group 4 · game 3", 12],
    ].map(slot),
  },
  {
    day: 2,
    slots: [
      [1792933200, 1792934100, "Introductions"],
      [1792934100, 1792936200, "Losers bracket · game 1", 13],
      [1792936200, 1792936800, "Intermission"],
      [1792936800, 1792938900, "Winners semi 1 · game 1", 14],
      [1792938900, 1792939500, "Intermission"],
      [1792939500, 1792941600, "Winners semi 1 · game 2", 15],
      [1792941600, 1792942200, "Intermission"],
      [1792942200, 1792944300, "Winners semi 1 · game 3", 16],
      [1792944300, 1792944900, "Intermission"],
      [1792944900, 1792947000, "Losers bracket · game 2", 17],
      [1792947000, 1792947600, "Intermission"],
      [1792947600, 1792949700, "Winners semi 2 · game 1", 18],
      [1792949700, 1792950300, "Intermission"],
      [1792950300, 1792952400, "Winners semi 2 · game 2", 19],
      [1792952400, 1792953000, "Intermission"],
      [1792953000, 1792955100, "Winners semi 2 · game 3", 20],
      [1792955100, 1792955700, "Intermission"],
      [1792955700, 1792957800, "Losers bracket · game 3", 21],
      [1792957800, 1792958400, "Intermission"],
      [1792958400, 1792960500, "Losers bracket · game 4", 22],
      [1792960500, 1792961100, "Intermission"],
      [1792961100, 1792963200, "Losers bracket · game 5", 23],
      [1792963200, 1792965600, "Break"],
      [1792965600, 1792967700, "Grand final · game 1", 24],
      [1792967700, 1792968300, "Intermission"],
      [1792968300, 1792970400, "Grand final · game 2", 25],
      [1792970400, 1792971000, "Intermission"],
      [1792971000, 1792973100, "Grand final · game 3", 26],
      [1792973100, null, "Closing speeches"],
    ].map(slot),
  },
];

const SLOTS = SCHEDULE.flatMap((day) => day.slots);

/** "upcoming", "live" or "done" at unix second `at`. */
export function slotState(slot, at) {
  if (at < slot.start) return "upcoming";
  if (slot.end === null || at < slot.end) return "live";

  return "done";
}

/** "game", "pause" for an intermission or the break, or "other" for the talking either side. */
export function kindOf(slot) {
  if (slot.group !== undefined) return "game";

  return slot.title === "Intermission" || slot.title === "Break" ? "pause" : "other";
}

/** The slot running at `at` and the next non-pause slot; either may be null. */
export function current(at) {
  return {
    live: SLOTS.find((slot) => slotState(slot, at) === "live") ?? null,
    next: SLOTS.find((slot) => slot.start > at && kindOf(slot) !== "pause") ?? null,
  };
}

/** A clock time in the viewer's zone and locale; the options exist for tests. */
export function timeOf(seconds, { locale, timeZone } = {}) {
  return new Intl.DateTimeFormat(locale, { hour: "2-digit", minute: "2-digit", timeZone }).format(seconds * 1000);
}

/** The date in the viewer's zone and locale. */
export function dateOf(seconds, { locale, timeZone } = {}) {
  return new Intl.DateTimeFormat(locale, { weekday: "long", day: "numeric", month: "long", timeZone }).format(
    seconds * 1000,
  );
}

/** "in 45 min", "in 2 h 5 min", "in 2 d 3 h" — rounded up, so it never says "in 0 min". */
export function until(at, then) {
  const minutes = Math.max(1, Math.ceil((then - at) / 60));
  const days = Math.floor(minutes / 1440);
  const hours = Math.floor((minutes % 1440) / 60);

  if (days > 0) return `in ${days} d ${hours} h`;
  if (hours > 0) return `in ${hours} h ${minutes % 60} min`;

  return `in ${minutes} min`;
}

/**
 * The bracket's stages in play order (a stage is the title before " · "), each with its distinct
 * players. Once the final is seated, its players are those seats, each noted with its source series.
 */
export function stages(bracket) {
  const members = new Map((bracket?.groups ?? []).map((group) => [group.number, group.members ?? []]));
  const found = new Map();

  for (const day of SCHEDULE) {
    for (const each of day.slots) {
      if (each.group === undefined) continue;

      const name = each.title.split(" · ")[0];
      if (!found.has(name)) found.set(name, { name, day: day.day, members: [] });

      const stage = found.get(name);
      for (const player of members.get(each.group) ?? []) {
        if (!stage.members.includes(player)) stage.members.push(player);
      }
    }
  }

  const seated = bracket?.final?.from ?? [];
  const final = found.get("Grand final");
  if (seated.length > 0) {
    final.members = seated.map((seat) => seat.name);
    final.notes = seated.map((seat) => seat.series);
  }

  return [...found.values()];
}
