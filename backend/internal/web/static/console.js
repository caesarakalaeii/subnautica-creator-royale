// console.js is the operator console's pure logic (no DOM). It has no rules of its own: controls are
// built from the verbs the mod reports, refusals are the mod's own messages, and whether a verb is
// allowed is decided by `MatchHost`, never here.

// The reply's store-of-record states (same as the bracket's).
export const RECORDS_OK = "ok";
export const RECORDS_NONE = "none";
export const RECORDS_UNREACHABLE = "unreachable";

// The states a command passes through, in order.
export const QUEUED = "queued";
export const SENT = "sent";
export const RAN = "ran";
export const EXPIRED = "expired";

// How long without a report before the game server counts as quiet. The mod reports on change and
// on a 30 s heartbeat.
const QUIET_AFTER_MS = 70_000;

/**
 * One control per verb the mod reported: a plain button, a button with a player-name field
 * (`takesSubject`), or type-to-confirm for destructive verbs (`mustBeTyped`).
 */
export function controls(report) {
  return (report?.verbs ?? []).map((verb) => ({
    verb: verb.verb,
    kind: verb.mustBeTyped ? "typed" : verb.takesSubject ? "subject" : "button",
    placeholder: verb.takesSubject ? "player" : "",

    // The confirmation to type is the verb itself.
    confirm: verb.mustBeTyped ? verb.verb : "",
  }));
}

/**
 * Groups controls into the rail's shelves by kind only, never by verb name, so new verbs need no
 * change here. Order within a shelf is the mod's (`AdminCommands.Word`).
 */
export function controlGroups(drawn) {
  const groups = [
    { key: "buttons", title: "press it", controls: [] },
    { key: "subjects", title: "about a player", controls: [] },
    { key: "typed", title: "type it", controls: [] },
  ];

  for (const one of drawn ?? []) {
    groups.find((group) => group.key.startsWith(one.kind))?.controls.push(one);
  }

  return groups.filter((group) => group.controls.length > 0);
}

/**
 * The command for a control and its typed text, or null when not armed (empty name field or
 * mismatched confirmation). Whitespace is squeezed like the mod's `AdminCommands.Squeezed`.
 */
export function verbFor(control, typed) {
  const said = squeeze(typed ?? "");

  if (control.kind === "typed") {
    return said.toLowerCase() === control.confirm.toLowerCase() ? control.confirm : null;
  }

  if (control.kind === "subject") {
    return said === "" ? null : `${control.verb} ${said}`;
  }

  return control.verb;
}

/** Whatever was typed, as words with one space between them. */
export function squeeze(text) {
  return `${text}`.trim().replace(/\s+/g, " ");
}

/**
 * Command-queue rows ready to draw. The wording keeps "still waiting for the game server" clearly
 * distinct from "given up on".
 */
export function commandRows(view) {
  return (view?.commands ?? []).map((row) => ({
    id: row.id,
    verb: row.verb,
    by: row.by,
    state: row.state,
    tone: toneOf(row.state),
    said: sentenceFor(row),

    // The mod's reply, verbatim.
    answer: row.answer ?? "",
    at: row.ranAt ?? row.sentAt ?? row.queuedAt ?? "",
  }));
}

/**
 * Whether a control's last command is still unanswered; the control stays disarmed meanwhile, since
 * repeated presses of verbs like `skip` or `next` would each run. `pending` covers a press whose
 * request has not returned an id yet. A missing row re-arms the control rather than stranding it.
 */
export function unanswered(view, id, pending = false) {
  if (pending) return true;

  if (!Number.isInteger(id) || id <= 0) return false;

  const row = (view?.commands ?? []).find((one) => one.id === id);
  if (!row) return false;

  return row.state === QUEUED || row.state === SENT;
}

function toneOf(state) {
  switch (state) {
    case RAN:
      return "ok";
    case EXPIRED:
      return "bad";
    case SENT:
      return "warn";
    default:
      return "";
  }
}

function sentenceFor(row) {
  switch (row.state) {
    case RAN:
      return "ran";
    case SENT:
      return "handed to the game server, no answer yet";
    case EXPIRED:
      return "given up on: nothing collected it in time";
    default:
      return "waiting for the game server to collect it";
  }
}

/**
 * Connected players with the booth's intended role beside the mod's actual role; `pending` marks
 * rows where they still disagree.
 */
export function roster(view) {
  const intent = view?.intent ?? {};
  const wanted = new Map();

  for (const name of intent.ghosts ?? []) wanted.set(fold(name), "ghost");
  for (const name of intent.contestants ?? []) wanted.set(fold(name), "contestant");

  const unbound = new Set((intent.unbound ?? []).map(fold));

  return (view?.report?.connected ?? []).map((present) => {
    const believed = present.inMatch ? "contestant" : "ghost";
    const intended = wanted.get(fold(present.name)) ?? "";

    return {
      name: present.name,
      believed,
      intended,

      // Not on the booth's roster, so it has no intended role.
      unbound: unbound.has(fold(present.name)),

      // True until the mod applies the booth's intent.
      pending: intended !== "" && intended !== believed,
    };
  });
}

/**
 * Roster people with a display string: the label (e.g. channel name) and the in-game name, both shown
 * when they differ. Matching uses the name.
 */
export function people(view) {
  return (view?.people ?? []).map((person) => ({
    id: person.id,
    name: person.name,
    label: person.label,
    notes: person.notes ?? "",
    drawn: person.label === person.name ? person.name : `${person.label} · ${person.name}`,
  }));
}

/** Every name the console knows, for the datalist beside a verb that takes one. */
export function knownNames(view) {
  const names = new Set();

  for (const present of view?.report?.connected ?? []) names.add(present.name);
  for (const person of view?.people ?? []) names.add(person.name);

  return [...names].sort((left, right) => left.localeCompare(right));
}

/**
 * One follow control per connected ghost (camera), based on what the mod reports rather than the
 * booth's intent. Rebuilt on every draw.
 */
export function followControls(view) {
  return (view?.report?.connected ?? [])
    .filter((present) => !present.inMatch)
    .map((present) => ({
      ghost: present.name,
      verb: "followOne",
      kind: "subject",
      placeholder: "player",
    }));
}

/**
 * The command a follow control sends. Like `verbFor`, but an empty field is valid: a bare
 * `followOne <ghost>` releases that camera.
 */
export function followVerbFor(control, typed) {
  const said = squeeze(typed ?? "");

  return said === "" ? `${control.verb} ${control.ghost}` : `${control.verb} ${control.ghost} ${said}`;
}

/**
 * The status line about roster reconciling, including the service's reason when it is idle.
 */
export function reconcileNote(view) {
  const intent = view?.intent ?? {};

  if (!intent.reconciling) {
    return {
      text: intent.why || "the booth is not reconciling the roster",
      tone: "warn",
    };
  }

  const pending = intent.pending ?? [];
  if (pending.length > 0) {
    return {
      text: `group ${intent.group}: ${pending.join(", ")} — asked for, not applied yet`,
      tone: "warn",
    };
  }

  const ghosts = (intent.ghosts ?? []).length;

  return {
    text: `group ${intent.group}: ${(intent.contestants ?? []).length} in the match, ` +
      `${ghosts} ghost${ghosts === 1 ? "" : "s"}`,
    tone: "ok",
  };
}

/**
 * Whether the game server is still reporting. Measured against the booth's own receive time, never
 * the mod's `at`, to avoid comparing two machines' clocks.
 */
export function linkNote(view, now = Date.now()) {
  if (!view?.report) {
    return { text: "no game server has reported yet", tone: "bad" };
  }

  const seen = Date.parse(view.reportedAt ?? "");
  if (Number.isNaN(seen)) {
    return { text: "reporting", tone: "ok" };
  }

  const quiet = now - seen;
  if (quiet > QUIET_AFTER_MS) {
    return { text: `the game server has been quiet for ${Math.round(quiet / 1000)}s`, tone: "bad" };
  }

  if (view.report.acceptsCommands === false) {
    return { text: "that server refuses commands (telemetry.acceptsCommands)", tone: "warn" };
  }

  return { text: "reporting", tone: "ok" };
}

// The states a config revision passes through.
export const RULES_QUEUED = "queued";
export const RULES_SENT = "sent";
export const RULES_WAITING = "waiting";
export const RULES_ADOPTED = "adopted";
export const RULES_REFUSED = "refused";

/**
 * The status of the latest config revision. "Waiting for the lobby" (held while a match runs) must
 * read clearly differently from a refusal.
 */
export function rulesNote(view) {
  const rules = view?.rules;

  if (!rules) {
    return { text: "the booth has not been given a config; the game server is running its own file", tone: "" };
  }

  const said = rules.answer ?? "";

  // No state: this is the game server's own running file, not a written revision, so it must not
  // read as queued.
  if (rules.state === "" || rules.state === undefined) {
    return {
      text: "the game server's own file, as it is running it; nothing has been written through the booth",
      tone: "",
    };
  }

  switch (rules.state) {
    case RULES_ADOPTED:
      return { text: `revision ${rules.revision} is running${said === "" ? "" : `: ${said}`}`, tone: "ok" };
    case RULES_WAITING:
      return {
        text: `revision ${rules.revision} is written and waiting for the lobby${said === "" ? "" : `: ${said}`}`,
        tone: "warn",
      };
    case RULES_REFUSED:
      return { text: `revision ${rules.revision} was refused: ${said}`, tone: "bad" };
    case RULES_SENT:
      return { text: `revision ${rules.revision} is with the game server, no answer yet`, tone: "warn" };
    case RULES_QUEUED:
      return { text: `revision ${rules.revision} is waiting for the game server to collect it`, tone: "" };

    // An unknown state is named as such rather than guessed at.
    default:
      return {
        text: `revision ${rules.revision} is in a state this console does not know: ${rules.state}`,
        tone: "warn",
      };
  }
}

/**
 * The config loader's problems by field path, verbatim
 * (e.g. `zone.phases[1].radius: must be a number of metres`).
 */
export function rulesProblems(view) {
  return view?.rules?.problems ?? [];
}

/**
 * The config to edit: the revision still in flight (queued, sent or waiting) if any, else the file
 * the game server reports running (so changes made on the server are not overwritten), else the
 * booth's latest revision.
 */
export function rulesText(view) {
  const rules = view?.rules;

  if (rules && [RULES_QUEUED, RULES_SENT, RULES_WAITING].includes(rules.state)) {
    return rules.yaml ?? "";
  }

  const running = view?.running?.yaml;

  if (running !== undefined && running !== "") {
    return running;
  }

  return rules?.yaml ?? "";
}

/**
 * The game server's running config as a revision-list row, with no state or answer since the booth
 * never wrote it. Revision 0 on the wire means this file. Null when the mod reports no config.
 */
export function runningRules(view) {
  const running = view?.running;
  const yaml = `${running?.yaml ?? ""}`;

  if (yaml === "") return null;

  return {
    revision: running.revision ?? 0,
    by: running.by ?? "the game server",
    yaml,
  };
}

/** The recent revisions, newest first, for a list somebody can take one back from. */
export function revisions(view) {
  return (view?.revisions ?? []).map((row) => ({
    revision: row.revision,
    by: row.by,
    note: row.note ?? "",
    state: row.state,
    tone: rulesTone(row.state),
    said: row.answer ?? "",
  }));
}

function rulesTone(state) {
  switch (state) {
    case RULES_ADOPTED:
      return "ok";
    case RULES_REFUSED:
      return "bad";
    case RULES_WAITING:
    case RULES_SENT:
      return "warn";
    default:
      return "";
  }
}

/** The body `PUT /rules` takes. The YAML is sent exactly as typed. */
export function rulesBody(yaml, by, note) {
  return { yaml, by, note: `${note ?? ""}`.trim() };
}

/** A banner for a missing or unreachable database, or null. */
export function recordsNote(state) {
  switch (state) {
    case RECORDS_NONE:
      return {
        text: "there is no database on this booth, so nothing here can be written down",
        tone: "warn",
      };
    case RECORDS_UNREACHABLE:
      return {
        text: "the booth cannot reach its database, so the queue and the roster may be behind",
        tone: "bad",
      };
    default:
      return null;
  }
}

/**
 * Whether the roster was actually read. An unreadable roster arrives as an empty list, and writing
 * that back would remove everyone.
 */
export function rosterRead(view) {
  return view?.roster === RECORDS_OK;
}

/**
 * The `PUT /people` body from the roster text. `removing` is passed through as typed: the booth
 * requires it as the operator's confirmation for removals, so it is never computed here.
 */
export function peopleBody(text) {
  const parsed = JSON.parse(text);
  const people = Array.isArray(parsed) ? parsed : (parsed.people ?? []);

  const body = {
    people: people.map((person) => ({
      // The id makes a rename update the existing row rather than add a new one.
      id: person.id ?? 0,
      name: `${person.name ?? ""}`.trim(),
      label: `${person.label ?? ""}`.trim(),
      notes: `${person.notes ?? ""}`.trim(),
    })),
  };

  if (!Array.isArray(parsed) && parsed.removing !== undefined) body.removing = parsed.removing;

  return body;
}

/** The roster as text to edit, which is exactly what `GET /console` sent. */
export function peopleText(view) {
  return JSON.stringify(
    {
      people: (view?.people ?? []).map((person) => ({
        id: person.id,
        name: person.name,
        label: person.label,
        notes: person.notes ?? "",
      })),
    },
    null,
    2,
  );
}

function fold(name) {
  return `${name}`.trim().toLowerCase();
}
