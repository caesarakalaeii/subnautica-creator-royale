// console-page.js is the DOM half of the operator console; the logic lives in console.js.
//
// It is a separate page from the dashboard because it needs the operator (write) token, while the
// dashboard runs on the view token in front of the cameras.

import { boothPath, storageKey } from "./booth.js";
import {
  commandRows,
  controlGroups,
  controls,
  followControls,
  followVerbFor,
  knownNames,
  linkNote,
  people,
  peopleBody,
  peopleText,
  reconcileNote,
  recordsNote,
  revisions,
  roster,
  rosterRead,
  rulesBody,
  rulesNote,
  rulesProblems,
  rulesText,
  runningRules,
  unanswered,
  verbFor,
} from "./console.js";

// The operator token's booth-scoped storage key; never put in the address bar.
const TOKEN_KEY = storageKey("lms.operatorToken", window.location);

// The operator's name, remembered between visits.
const BY_KEY = "lms.operatorName";

// Poll interval; about as soon as the mod can have answered a command.
const POLL_MS = 2_000;

const field = (name) => document.querySelector(`[data-field="${name}"]`);

// `refused` stops polling after a 401 until the token changes. `mine` and `myRules` mark the roster
// and rules editors as being edited, so polls do not overwrite them.
const state = { view: null, mine: false, myRules: false, refused: false };

// Per-control arming state, keyed by verb (`followOne:<ghost>` for follow controls), so a poll can
// re-arm a button once its command is answered. Rebuilt alongside the controls.
const fired = new Map();

function link(text, tone) {
  const box = field("link");
  box.dataset.tone = tone;
  box.querySelector(".link__text").textContent = text;
}

function say(text, tone) {
  const box = field("said");
  box.textContent = text;
  box.dataset.tone = tone ?? "";
}

function note(name, drawn) {
  const box = field(name);

  if (drawn === null) {
    box.hidden = true;
    return;
  }

  box.hidden = false;
  box.textContent = drawn.text;
  box.dataset.tone = drawn.tone;
}

function token() {
  return field("token").value.trim();
}

/** Draws the verb rail grouped by control kind, rebuilt only when the vocabulary changes. */
function drawVerbs(view) {
  const drawn = controls(view?.report);
  const box = field("verbs");

  const signature = drawn.map((one) => `${one.verb}:${one.kind}`).join("|");
  if (box.dataset.signature === signature) return;

  // Drop the entries for the buttons being replaced; `control` re-registers them. Follow controls
  // are managed by `drawFollows`.
  for (const key of [...fired.keys()]) {
    if (!key.startsWith("followOne:")) fired.delete(key);
  }

  box.dataset.signature = signature;
  box.replaceChildren(
    ...controlGroups(drawn).map((group) => {
      const shelf = document.createElement("div");
      shelf.className = "rail__group";

      const label = document.createElement("p");
      label.className = "rail__label";
      label.textContent = group.title;
      shelf.append(label);

      shelf.append(...group.controls.map(control));

      return shelf;
    }),
  );

  field("verbsEmpty").hidden = drawn.length > 0;
}

function control(one) {
  const box = document.createElement("div");
  box.className = one.kind === "typed" ? "verb verb--typed" : "verb";

  const word = document.createElement("span");
  word.className = "verb__word";
  word.textContent = one.verb;
  box.append(word);

  let typed = null;

  if (one.kind !== "button") {
    typed = document.createElement("input");
    typed.type = "text";
    typed.spellcheck = false;
    typed.placeholder = one.kind === "typed" ? `type "${one.verb}"` : one.placeholder;

    // Name suggestions only for player verbs; a confirmation must be typed out.
    if (one.kind === "subject") typed.setAttribute("list", "known-names");

    box.append(typed);
  }

  const go = document.createElement("button");
  go.type = "button";
  go.textContent = one.kind === "typed" ? "type it" : "run";
  go.addEventListener("click", () => run(one, typed));
  box.append(go);

  fired.set(one.verb, { go, id: null, pending: false });

  return box;
}

/** Draws one follow control per connected camera, rebuilt only when the set of cameras changes. */
function drawFollows(view) {
  const drawn = followControls(view);
  const box = field("follows");

  const signature = drawn.map((one) => one.ghost).join("|");
  if (box.dataset.signature === signature) return;

  // Drop entries for the buttons being replaced; `followControl` re-registers them.
  for (const one of drawn) fired.delete(`followOne:${one.ghost}`);

  box.dataset.signature = signature;
  box.replaceChildren(...drawn.map(followControl));

  field("followsEmpty").hidden = drawn.length > 0;
}

/** Like `control`, for one camera: labelled with the camera's name instead of the verb. */
function followControl(one) {
  const box = document.createElement("div");
  box.className = "verb follow";

  const word = document.createElement("span");
  word.className = "verb__word";
  word.textContent = one.ghost;
  box.append(word);

  const typed = document.createElement("input");
  typed.type = "text";
  typed.spellcheck = false;
  typed.placeholder = "let go";
  typed.setAttribute("list", "known-names");
  box.append(typed);

  const go = document.createElement("button");
  go.type = "button";
  go.textContent = "run";
  go.addEventListener("click", () => runFollow(one, typed));
  box.append(go);

  fired.set(`followOne:${one.ghost}`, { go, id: null, pending: false });

  return box;
}

async function run(one, typed) {
  const verb = verbFor(one, typed?.value ?? "");

  if (verb === null) {
    say(
      one.kind === "typed"
        ? `type "${one.verb}" beside it to run it`
        : `${one.verb} needs a player's name`,
      "bad",
    );

    return;
  }

  // If the control was rebuilt since the click, the press goes unguarded rather than blocked.
  const entry = fired.get(one.verb);

  // Guards against synthetic clicks on a disabled button.
  if (entry?.go.disabled) return;

  // Set before the await to close the double-click window; `pending` stops a poll from re-arming
  // the button before the booth returns a row id.
  if (entry) {
    entry.pending = true;
    entry.go.disabled = true;
  }

  const queued = await write("/commands", "POST", { verb, by: whoAmI() });

  if (queued === null) {
    // Nothing was queued, so re-arm immediately.
    if (entry) {
      entry.pending = false;
      entry.go.disabled = false;
    }

    return;
  }

  if (entry) {
    entry.id = queued.id;
    entry.pending = false;
  }

  say(`${verb} queued as #${queued.id}; the game server runs it on its next tick`, "ok");

  if (typed) typed.value = "";

  // Re-read now rather than waiting for the next poll.
  await read();
}

/**
 * Sends one follow command for a camera. Guarded per camera (`followOne:<ghost>`), like `run`.
 */
async function runFollow(one, typed) {
  const verb = followVerbFor(one, typed?.value ?? "");
  const key = `${one.verb}:${one.ghost}`;

  const entry = fired.get(key);

  // Guards against synthetic clicks on a disabled button.
  if (entry?.go.disabled) return;

  if (entry) {
    entry.pending = true;
    entry.go.disabled = true;
  }

  const queued = await write("/commands", "POST", { verb, by: whoAmI() });

  if (queued === null) {
    if (entry) {
      entry.pending = false;
      entry.go.disabled = false;
    }

    return;
  }

  if (entry) {
    entry.id = queued.id;
    entry.pending = false;
  }

  say(`${verb} queued as #${queued.id}; the game server runs it on its next tick`, "ok");
  typed.value = "";

  await read();
}

/** Re-arms each control once the queue shows its command answered; runs on every draw. */
function drawArmed(view) {
  for (const entry of fired.values()) {
    entry.go.disabled = unanswered(view, entry.id, entry.pending);

    // Answered: forget the row id so the next press is judged on its own row.
    if (!entry.go.disabled) entry.id = null;
  }
}

function drawRoster(view) {
  const rows = roster(view);

  field("roster").replaceChildren(
    ...rows.map((row) => {
      const tr = document.createElement("tr");

      if (row.unbound) tr.className = "roster__unbound";
      else if (row.pending) tr.className = "roster__pending";

      for (const text of [
        row.name,
        row.believed,
        row.unbound ? "nobody wrote this name down" : row.intended,
      ]) {
        const td = document.createElement("td");
        td.textContent = text;
        tr.append(td);
      }

      return tr;
    }),
  );

  field("rosterEmpty").hidden = rows.length > 0;
}

function drawQueue(view) {
  const rows = commandRows(view);

  field("queue").replaceChildren(
    ...rows.map((row) => {
      const li = document.createElement("li");
      li.dataset.tone = row.tone;

      const head = document.createElement("div");
      head.className = "queue__head";

      const verb = document.createElement("span");
      verb.textContent = `#${row.id} ${row.verb}`;

      const said = document.createElement("span");
      said.className = "queue__state";
      said.textContent = `${row.said} · ${row.by}`;

      head.append(verb, said);
      li.append(head);

      if (row.answer !== "") {
        const answer = document.createElement("p");
        answer.className = "queue__answer";

        // The mod's reply, verbatim.
        answer.textContent = row.answer;
        li.append(answer);
      }

      return li;
    }),
  );

  field("queueEmpty").hidden = rows.length > 0;
}

function drawPeople(view) {
  const rows = people(view);

  field("people").replaceChildren(
    ...rows.map((person) => {
      const li = document.createElement("li");

      const name = document.createElement("span");
      name.className = "people__name";
      name.textContent = person.drawn;
      li.append(name);

      if (person.notes !== "") {
        const notes = document.createElement("p");
        notes.className = "people__notes";
        notes.textContent = person.notes;
        li.append(notes);
      }

      return li;
    }),
  );

  // An unread roster is not an empty one; the notes strip explains the outage.
  field("peopleEmpty").hidden = rows.length > 0 || !rosterRead(view);

  // Rebuilt only when the roster changes, so an open dropdown is not closed by a poll.
  const chooser = field("overridePerson");
  const signature = rows.map((person) => `${person.id}:${person.drawn}`).join("|");
  if (chooser.dataset.signature === signature) return;

  const chosen = chooser.value;
  chooser.dataset.signature = signature;
  chooser.replaceChildren(
    ...rows.map((person) => {
      const option = document.createElement("option");
      option.value = `${person.id}`;
      option.textContent = person.drawn;

      return option;
    }),
  );

  if (chosen !== "") chooser.value = chosen;
}

function drawOverrides(view) {
  const rows = view?.overrides ?? [];

  field("overrides").replaceChildren(
    ...[...rows].reverse().map((override) => {
      const li = document.createElement("li");

      const name = document.createElement("span");
      name.className = "overrides__name";
      name.textContent = `${override.name} → ${override.role}`;
      li.append(name);

      const note = document.createElement("p");
      note.className = "overrides__note";
      note.textContent = [override.note, `${override.by}`, override.at].filter(Boolean).join(" · ");
      li.append(note);

      return li;
    }),
  );

  field("overridesEmpty").hidden = rows.length > 0;
}

function drawKnownNames(view) {
  field("knownNames").replaceChildren(
    ...knownNames(view).map((name) => {
      const option = document.createElement("option");
      option.value = name;

      return option;
    }),
  );
}

/** Fills the roster editor from the view, unless the operator is editing it. */
function drawPeopleJson(view) {
  // An unread roster would prefill as empty, and sending that back would remove everyone.
  if (state.mine || !rosterRead(view)) return;

  field("peopleJson").value = peopleText(view);
}

function drawRules(view) {
  note("rulesState", rulesNote(view));

  const problems = rulesProblems(view);
  const box = field("rulesProblems");

  box.hidden = problems.length === 0;
  box.replaceChildren(
    ...problems.map((problem) => {
      const li = document.createElement("li");

      // The loader's message, verbatim.
      li.textContent = problem;

      return li;
    }),
  );

  const running = runningRules(view);
  const drawn = [];

  // The running file goes first. It has no state or answer since the booth never wrote it.
  if (running !== null) {
    drawn.push(
      revisionRow({
        which: "the file the game server is running",
        aside: running.by,
        said: "",
        tone: "",
        take: running.revision,
      }),
    );
  }

  for (const row of revisions(view)) {
    drawn.push(
      revisionRow({
        which: `#${row.revision} ${row.state}`,
        aside: [row.note, row.by].filter(Boolean).join(" · "),
        said: row.said,
        tone: row.tone,
        take: row.revision,
      }),
    );
  }

  // Rebuilt only when a row changes, so keyboard focus on its buttons survives polls.
  const list = field("revisions");
  const signature = JSON.stringify([running, revisions(view)]);
  if (list.dataset.signature !== signature) {
    list.dataset.signature = signature;
    list.replaceChildren(...drawn);
  }
  field("revisionsEmpty").hidden = drawn.length > 0;

  if (state.myRules) return;

  field("rulesYaml").value = rulesText(view);
}

/**
 * One revision-list row. Its button only loads the document into the editor; sending it back goes
 * through the normal `PUT /rules` send.
 */
function revisionRow({ which, aside, said, tone, take }) {
  const li = document.createElement("li");
  li.dataset.tone = tone;

  const head = document.createElement("div");
  head.className = "queue__head";

  const label = document.createElement("span");
  label.textContent = which;

  const by = document.createElement("span");
  by.className = "queue__state";
  by.textContent = aside;

  head.append(label, by);
  li.append(head);

  if (said !== "") {
    const answer = document.createElement("p");
    answer.className = "queue__answer";

    // The game server's reply, verbatim.
    answer.textContent = said;
    li.append(answer);
  }

  const back = document.createElement("button");
  back.type = "button";
  back.className = "go go--quiet revisions__take";
  back.dataset.revision = `${take}`;
  back.textContent = "take this one back";
  back.title = "puts this document in the editor above; sending it is the same push as any other";
  li.append(back);

  return li;
}

/**
 * Loads one revision's document into the editor. The list carries no documents, so it is fetched on
 * demand; revision 0 (the server's running file) comes from the view instead.
 */
async function takeBack(revision) {
  const yaml = await documentOf(revision);
  if (yaml === null) return;

  field("rulesYaml").value = yaml;

  // Mark the editor as the operator's so the next poll does not overwrite it.
  state.myRules = true;

  // Prefill the revision note, which the audit log records.
  field("rulesNote").value =
    revision === 0 ? "back to the game server's own file" : `back to revision ${revision}`;

  // Nothing has been sent yet, so do not claim the rules changed.
  say(
    revision === 0
      ? "the game server's own file is in the editor; send it to push it back"
      : `revision ${revision} is in the editor; send it to push it back`,
    "ok",
  );
}

/** The document behind one row, from the view for revision 0 and from the booth for the rest. */
async function documentOf(revision) {
  if (revision === 0) {
    const running = runningRules(state.view);

    if (running === null) {
      say("the game server has not said what it is running", "bad");

      return null;
    }

    return running.yaml;
  }

  const body = await fetched(`/rules/${revision}`);
  if (body === null) return null;

  const yaml = `${body.yaml ?? ""}`;
  if (yaml === "") {
    // Refuse loudly, so an empty file is not pushed by accident.
    say(`revision ${revision} came back with no document in it`, "bad");

    return null;
  }

  return yaml;
}

function draw(view) {
  state.view = view;

  note("records", recordsNote(view.records));
  note("reconcile", reconcileNote(view));
  drawVerbs(view);
  drawFollows(view);
  drawRoster(view);
  drawQueue(view);
  drawPeople(view);
  drawOverrides(view);
  drawKnownNames(view);
  drawPeopleJson(view);
  drawRules(view);
  drawArmed(view);

  const state_ = linkNote(view);
  link(state_.text, state_.tone);
}

async function read() {
  if (token() === "" || state.refused) {
    if (token() === "") link("locked", "");
    field("operator").hidden = true;
    field("lock").hidden = false;

    return;
  }

  try {
    const response = await fetch(boothPath("/console", window.location), {
      headers: { Accept: "application/json", Authorization: `Bearer ${token()}` },
    });

    if (response.status === 401) {
      // Stop polling until the token changes, so a stale page does not keep sending a bad token.
      state.refused = true;
      link("that is not the operator token", "bad");
      field("operator").hidden = true;
      field("lock").hidden = false;

      return;
    }

    if (!response.ok) {
      link(`the booth answered ${response.status}`, "bad");

      return;
    }

    field("operator").hidden = false;
    field("lock").hidden = true;
    draw(await response.json());
  } catch (failure) {
    // Keep what was drawn last; the status corner shows the failure.
    link("cannot reach the booth", "bad");
  }
}

/** One write, and the service's own reason shown verbatim when it refuses. */
async function write(route, method, body) {
  if (token() === "") {
    say("paste the operator token first", "bad");

    return null;
  }

  try {
    const response = await fetch(boothPath(route, window.location), {
      method,
      headers: { "Content-Type": "application/json", Authorization: `Bearer ${token()}` },
      body: JSON.stringify(body),
    });

    const answer = await response.json().catch(() => ({}));

    if (!response.ok) {
      say(answer.error ?? `the booth answered ${response.status}`, "bad");

      return null;
    }

    return answer;
  } catch (failure) {
    say("cannot reach the booth", "bad");

    return null;
  }
}

/**
 * One authenticated GET of a booth route, showing a refusal verbatim. Separate from `read`, whose
 * 401 handling stops the poll.
 */
async function fetched(route) {
  if (token() === "") {
    say("paste the operator token first", "bad");

    return null;
  }

  try {
    const response = await fetch(boothPath(route, window.location), {
      headers: { Accept: "application/json", Authorization: `Bearer ${token()}` },
    });

    const answer = await response.json().catch(() => ({}));

    if (!response.ok) {
      say(answer.error ?? `the booth answered ${response.status}`, "bad");

      return null;
    }

    return answer;
  } catch (failure) {
    say("cannot reach the booth", "bad");

    return null;
  }
}

function whoAmI() {
  const by = field("by").value.trim();

  // Never empty: the service requires a name.
  return by === "" ? "the console" : by;
}

/** The chip in the header naming who is operating, from the same field the writes carry. */
function identity() {
  const by = field("by").value.trim();
  const op = field("opName");

  op.hidden = by === "";
  op.textContent = by === "" ? "" : `op: ${by}`;
}

function wire() {
  const secret = field("token");
  secret.value = localStorage.getItem(TOKEN_KEY) ?? "";
  secret.addEventListener("change", () => {
    localStorage.setItem(TOKEN_KEY, secret.value.trim());
    state.refused = false;
    read();
  });

  const by = field("by");
  by.value = localStorage.getItem(BY_KEY) ?? "";
  by.addEventListener("change", () => {
    localStorage.setItem(BY_KEY, by.value.trim());
    identity();
  });
  identity();

  // The drawer's tabs for setup-time editing (rules, roster, exceptions). Hidden panels still
  // receive poll updates.
  const bar = document.querySelector(".drawer__bar");
  bar.addEventListener("click", (event) => {
    const tab = event.target.closest("[data-tab]");
    if (tab === null) return;

    for (const other of bar.querySelectorAll("[data-tab]")) {
      other.setAttribute("aria-selected", `${other === tab}`);
    }

    for (const panel of document.querySelectorAll("[data-tab-panel]")) {
      panel.hidden = panel.dataset.tabPanel !== tab.dataset.tab;
    }
  });

  const editor = field("peopleJson");
  editor.addEventListener("input", () => {
    state.mine = true;
  });

  field("revertPeople").addEventListener("click", () => {
    state.mine = false;
    drawPeopleJson(state.view);
    say("back to what the booth holds", "");
  });

  submits(field("peopleForm"), async () => {
    let body;
    try {
      body = peopleBody(editor.value);
    } catch (failure) {
      say(`that is not the JSON this takes: ${failure.message}`, "bad");

      return;
    }

    const written = await write("/people", "PUT", body);
    if (written === null) return;

    state.mine = false;
    say(`${written.people} on the roster`, "ok");

    await read();
  });

  const rules = field("rulesYaml");
  rules.addEventListener("input", () => {
    state.myRules = true;
  });

  // Delegated on the list because rows are rebuilt. Not in `fired`: loading a revision queues
  // no command.
  field("revisions").addEventListener("click", (event) => {
    const button = event.target.closest("[data-revision]");
    if (button === null) return;

    const revision = Number.parseInt(button.dataset.revision, 10);
    if (Number.isNaN(revision)) return;

    takeBack(revision);
  });

  field("revertRules").addEventListener("click", () => {
    state.myRules = false;
    drawRules(state.view);
    say("back to the document the console opens on", "");
  });

  submits(field("rulesForm"), async () => {
    const written = await write(
      "/rules",
      "PUT",
      rulesBody(rules.value, whoAmI(), field("rulesNote").value),
    );

    if (written === null) return;

    state.myRules = false;
    field("rulesNote").value = "";

    // The game server has not applied it yet, so do not claim the rules changed.
    say(`revision ${written.revision} written; the game server picks it up on its next push`, "ok");

    await read();
  });

  submits(field("overrideForm"), async () => {
    const person = Number.parseInt(field("overridePerson").value, 10);
    if (Number.isNaN(person)) {
      say("choose somebody on the roster first", "bad");

      return;
    }

    const written = await write("/overrides", "POST", {
      person,
      role: field("overrideRole").value,
      note: field("overrideNote").value,
      by: whoAmI(),
    });

    if (written === null) return;

    say(`${written.name} → ${written.role} (row ${written.id})`, "ok");
    field("overrideNote").value = "";

    await read();
  });
}

/**
 * Wires a form's submit, disabling its buttons until the write completes, since a repeated submit
 * would append a second row.
 */
function submits(form, send) {
  form.addEventListener("submit", async (event) => {
    event.preventDefault();

    const buttons = [...form.querySelectorAll("button")];
    if (buttons.some((button) => button.disabled)) return;

    for (const button of buttons) button.disabled = true;
    try {
      await send();
    } finally {
      for (const button of buttons) button.disabled = false;
    }
  });
}

wire();
read();
setInterval(read, POLL_MS);
