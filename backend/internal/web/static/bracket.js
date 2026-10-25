// bracket.js is the bracket page's pure logic (no DOM). The page shows standings and pairings only,
// never live positions or who is still in; `GET /bracket` does not carry them.

import { boothBase } from "./booth.js";
import { ABSENT } from "./view.js";

// The reply's store-of-record states.
export const RECORDS_OK = "ok";
export const RECORDS_NONE = "none";
export const RECORDS_UNREACHABLE = "unreachable";

// A final played outside the groups, counted in the caption's total. Zero once the final is seated
// from series, since its games are then written as groups.
const FINAL_MATCHES = 1;

/**
 * Table rows ready to draw. Places come from the service, which gives tied rows the same place;
 * `shared` marks those rows.
 */
export function tableRows(standings) {
  const entries = standings?.entries ?? [];
  const places = new Map();

  for (const row of entries) {
    places.set(row.place, (places.get(row.place) ?? 0) + 1);
  }

  return entries.map((row) => ({
    place: row.place,
    shared: (places.get(row.place) ?? 0) > 1,
    name: row.name,
    points: row.points,
    played: row.played,
    wins: row.wins,
    best: row.best,
    kills: row.kills,
    attributed: row.attributed,
    outlasted: row.outlasted,
    adjusted: row.adjusted ?? 0,

    // The points sum, spelled out so readers can check it.
    working: working(row, standings?.pointsPerKill ?? 0),
  }));
}

/**
 * The sum behind a row's points, as text. Logged and attributed kills score at the same rate, so
 * they form one term.
 */
export function working(row, pointsPerKill) {
  const kills = (row.kills ?? 0) + (row.attributed ?? 0);
  const terms = [`${row.outlasted ?? 0} outlasted`];

  if (kills > 0) terms.push(`${kills} × ${pointsPerKill} for kills`);

  // An operator adjustment, which may be negative.
  const adjusted = row.adjusted ?? 0;
  const sum = terms.join(" + ");
  if (adjusted > 0) return `${sum} + ${adjusted} adjusted`;
  if (adjusted < 0) return `${sum} − ${-adjusted} adjusted`;

  return sum;
}

/** The caption over the table: matches played so far and attributions folded in. */
export function caption(bracket) {
  const standings = bracket?.standings;
  if (!standings) return "no match has finished yet";

  const groups = bracket?.groups?.length ?? 0;
  const final = (bracket?.final?.spec ?? []).length > 0 ? 0 : FINAL_MATCHES;
  const of = groups > 0 ? ` of ${groups + final}` : "";
  const attributions = bracket?.attributions ?? 0;

  return [
    `after ${standings.matches}${of} match${standings.matches === 1 ? "" : "es"}`,
    `${attributions} attribution${attributions === 1 ? "" : "s"}`,
  ].join(" · ");
}

/**
 * The group cards in order, then the computed final. The final has no group number, since it is
 * derived from the standings rather than written as a group.
 */
export function groupCards(bracket) {
  const cards = (bracket?.groups ?? []).map((group) => ({
    label: `${group.number}`,
    name: group.name,
    members: group.members ?? [],
    computed: false,
    empty: (group.members ?? []).length === 0,
    note: group.series ? `counts toward ${group.series}` : undefined,
  }));

  const final = bracket?.final;
  if (!final || (final.seats ?? 0) <= 0) return cards;

  const seated = final.seated ?? [];
  const spec = final.spec ?? [];

  // With a spec, each seat names the series that filled it.
  const members = spec.length > 0
    ? (final.from ?? []).map((seat) => `${seat.name} · ${seat.series}`)
    : seated;
  const rule = spec.length > 0
    ? spec.map((row) => `top ${row.seats} of ${row.series}`).join(", ")
    : `the top ${final.seats} of the table`;

  cards.push({
    label: ABSENT,
    name: "Final",
    members,
    computed: true,
    empty: seated.length === 0,

    // Seats fill as groups are played, so a short final says how many are left.
    note: seated.length < final.seats
      ? spec.length > 0
        ? `${seated.length} of ${final.seats} seats — ${rule}`
        : `${seated.length} of ${final.seats} seats — the rest are decided by the groups still to play`
      : rule,
  });

  return cards;
}

/**
 * A banner about the store of record, or null. Distinguishes "no database" and "unreachable" from
 * simply having no attributions.
 */
export function recordsNote(state) {
  switch (state) {
    case RECORDS_UNREACHABLE:
      return {
        tone: "bad",
        text: "the booth cannot reach its database, so attributed kills are missing from this table",
      };

    case RECORDS_NONE:
      return {
        tone: "warn",
        text: "this booth has no database, so no kill has been attributed",
      };

    default:
      return null;
  }
}

/**
 * The bracket fetch, with the token (if any) in the Authorization header rather than the URL so it
 * stays out of screen shares. Without a token it still asks: a published booth answers anyone, and
 * a 401 tells the page to ask for a token.
 */
export function bracketRequest(token, location) {
  const headers = { Accept: "application/json" };
  const held = (token ?? "").trim();
  if (held !== "") headers.Authorization = `Bearer ${held}`;

  return {
    // Under this page's booth.
    url: `${boothBase(location)}/bracket`,
    init: { headers },
  };
}

/**
 * The attribution body from the form. Only trims and maps an empty killer to `null` (which retracts
 * a kill); validation is left to the service, whose message is shown verbatim.
 */
export function attributionBody(form) {
  const actor = (form.actor ?? "").trim();

  return {
    group: Number(form.group),
    generation: Number(form.generation),
    victim: (form.victim ?? "").trim(),
    actor: actor === "" ? null : actor,
    by: (form.by ?? "").trim(),
  };
}

/** The body `POST /adjustments` takes. The service validates it. */
export function adjustmentBody(form) {
  return {
    group: Number(form.group),
    player: (form.player ?? "").trim(),
    points: Number(form.points),
    reason: (form.reason ?? "").trim(),
    by: (form.by ?? "").trim(),
  };
}

/** Whether the form has enough in it to be worth sending. The service is still the authority. */
export function readyToSend(form) {
  const body = attributionBody(form);

  return (
    Number.isInteger(body.group) &&
    body.group >= 1 &&
    Number.isInteger(body.generation) &&
    body.generation >= 1 &&
    body.victim !== "" &&
    body.by !== ""
  );
}

/** Every name the bracket knows, for the datalist an operator picks a player out of. */
export function knownPlayers(bracket) {
  const names = new Set();

  for (const group of bracket?.groups ?? []) {
    for (const member of group.members ?? []) names.add(member);
  }

  for (const row of bracket?.standings?.entries ?? []) names.add(row.name);

  return [...names].sort((left, right) => (left < right ? -1 : left > right ? 1 : 0));
}

/**
 * The bracket editor's rows from `GET /bracket`, unmodified so an untouched bracket round-trips
 * exactly.
 */
export function groupsFromBracket(bracket) {
  const groups = bracket?.groups ?? [];

  return groups.length === 0
    ? [{ number: 1, name: "", series: "", members: [] }]
    : groups.map((group) => ({
      number: group.number,
      name: group.name,
      series: group.series ?? "",
      members: group.members ?? [],
    }));
}

/**
 * The `PUT /groups` body from the editor's rows, trimmed and numbered. The final's spec is sent back
 * unchanged, because the request replaces the whole bracket.
 */
export function groupsFormBody(rows, final = []) {
  return {
    groups: rows.map((row) => ({
      number: Number(row.number),
      name: `${row.name ?? ""}`.trim(),
      series: `${row.series ?? ""}`.trim(),
      members: (row.members ?? []).map((member) => `${member}`.trim()),
    })),
    final,
  };
}

/**
 * Whether the editor is worth sending: shape checks mirroring the service's `groupsFrom`, which
 * remains the authority. A player in two groups is allowed.
 */
export function groupsFormReady(rows) {
  return rows.every((row) => {
    const number = Number(row.number);

    return (
      Number.isInteger(number) &&
      number >= 1 &&
      `${row.name ?? ""}`.trim() !== "" &&
      row.members.every((member) => `${member}`.trim() !== "") &&
      new Set(row.members.map((member) => `${member}`.trim())).size === row.members.length
    );
  });
}
