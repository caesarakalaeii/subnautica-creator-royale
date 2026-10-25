// bracket-page.js is the DOM half of the bracket page; the logic lives in bracket.js.

import { boothPath, storageKey } from "./booth.js";
import {
  adjustmentBody,
  attributionBody,
  bracketRequest,
  caption,
  groupCards,
  groupsFormBody,
  groupsFormReady,
  groupsFromBracket,
  knownPlayers,
  readyToSend,
  recordsNote,
  tableRows,
 } from "./bracket.js";

// The operator token's booth-scoped storage key. Kept in storage, never in the address bar, so it
// stays out of screen shares.
const TOKEN_KEY = storageKey("lms.operatorToken", window.location);

// The view token needed to read an unpublished bracket; same key as the dashboard.
const VIEW_TOKEN_KEY = storageKey("lms.viewToken", window.location);

// The operator's name for the "said by" field, remembered between visits.
const BY_KEY = "lms.operatorName";

// How often the bracket is re-read; it changes only when a match ends or a record is written.
const POLL_MS = 10_000;

const field = (name) => document.querySelector(`[data-field="${name}"]`);

const state = { bracket: null, mine: false };

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

/** Draws the standings table. */
function drawStandings(bracket) {
  const rows = tableRows(bracket?.standings);
  const body = field("standings");

  body.replaceChildren(
    ...rows.map((row) => {
      const tr = document.createElement("tr");

      // The points working goes in the row's tooltip; as a column it would double the table width.
      tr.title = row.working;

      const cells = [
        // A tied place is marked with "=".
        [row.shared ? `=${row.place}` : `${row.place}`, "table__place"],
        [row.name, "table__name"],
        [row.points, "table__number table__number--total"],
        [row.played, "table__number"],
        [row.wins, "table__number"],
        [row.best, "table__number"],
        [row.outlasted, "table__number"],
        [row.kills, "table__number"],
        [row.attributed, "table__number table__number--attributed"],
        [row.adjusted > 0 ? `+${row.adjusted}` : `${row.adjusted}`, "table__number"],
      ];

      for (const [text, className] of cells) {
        const td = document.createElement("td");
        td.className = className;
        td.textContent = `${text}`;
        tr.append(td);
      }

      return tr;
    }),
  );

  field("standingsEmpty").hidden = rows.length > 0;
  field("standingsTable").hidden = rows.length === 0;
}

function drawGroups(bracket) {
  const cards = groupCards(bracket);

  field("groups").replaceChildren(
    ...cards.map((card) => {
      const box = document.createElement("article");
      box.className = card.computed ? "card card--computed" : "card";

      const head = document.createElement("h3");
      head.className = "card__head";

      const label = document.createElement("span");
      label.className = "card__label";
      label.textContent = card.label;

      const name = document.createElement("span");
      name.className = "card__name";
      name.textContent = card.name;

      head.append(label, name);
      box.append(head);

      const list = document.createElement("ol");
      list.className = "card__members";

      for (const member of card.members) {
        const item = document.createElement("li");
        item.textContent = member;
        list.append(item);
      }

      box.append(list);

      if (card.empty) {
        const empty = document.createElement("p");
        empty.className = "card__empty";
        empty.textContent = "nobody yet";
        box.append(empty);
      }

      if (card.note) {
        const note = document.createElement("p");
        note.className = "card__note";
        note.textContent = card.note;
        box.append(note);
      }

      return box;
    }),
  );

  field("groupsEmpty").hidden = cards.length > 0;
}

function drawRecords(bracket) {
  const note = recordsNote(bracket?.records);
  const box = field("records");

  box.hidden = note === null;
  if (note === null) return;

  box.textContent = note.text;
  box.dataset.tone = note.tone;
}

function drawKnownPlayers(bracket) {
  field("knownPlayers").replaceChildren(
    ...knownPlayers(bracket).map((name) => {
      const option = document.createElement("option");
      option.value = name;

      return option;
    }),
  );
}

/**
 * Rebuilds the bracket editor's rows from the bracket just read, unless the operator has started
 * editing, so a poll never overwrites unsaved input.
 */
function drawGroupsRows(bracket) {
  if (state.mine) return;

  field("groupsRows").replaceChildren(...groupsFromBracket(bracket).map((row) => groupRow(row)));
}

/** One group as a row: its number and name, and a box per member. */
function groupRow(row) {
  const box = document.createElement("div");
  box.className = "groups__group";

  const number = document.createElement("label");
  number.className = "field groups__number";
  number.innerHTML =
    '<span class="field__label">group</span>' +
    `<input type="number" min="1" value="${row.number}" data-name="number" />`;

  const name = document.createElement("label");
  name.className = "field groups__name";
  name.innerHTML =
    '<span class="field__label">name</span>' +
    `<input type="text" value="${escapeHTML(row.name)}" data-name="name" spellcheck="false" />`;

  // The series toward the final this group counts in, if any. The booth rejects unknown names.
  const series = document.createElement("label");
  series.className = "field groups__name";
  series.innerHTML =
    '<span class="field__label">series</span>' +
    `<input type="text" value="${escapeHTML(row.series ?? "")}" data-name="series" spellcheck="false" />`;

  const members = document.createElement("div");
  members.className = "groups__members";

  for (const member of row.members) {
    members.append(memberBox(member));
  }

  const addMember = document.createElement("button");
  addMember.type = "button";
  addMember.className = "go go--quiet";
  addMember.dataset.addMember = "";
  addMember.textContent = "another player";

  const remove = document.createElement("button");
  remove.type = "button";
  remove.className = "go go--quiet";
  remove.dataset.removeGroup = "";
  remove.textContent = "drop this group";

  box.append(number, name, series, members, addMember, remove);

  return box;
}

function memberBox(name) {
  const box = document.createElement("label");
  box.className = "field groups__member";
  box.innerHTML =
    '<span class="field__label">player</span>' +
    `<input type="text" list="known-players" value="${escapeHTML(name)}" data-name="member" ` +
    'spellcheck="false" />';

  const remove = document.createElement("button");
  remove.type = "button";
  remove.className = "go go--quiet";
  remove.dataset.removeMember = "";
  remove.textContent = "×";

  box.append(remove);

  return box;
}

// Escapes names placed into value attributes; player names come from the booth and are untrusted.
function escapeHTML(text) {
  return `${text}`.replace(/[&<>"']/g, (each) => ({
    "&": "&amp;",
    "<": "&lt;",
    ">": "&gt;",
    '"': "&quot;",
    "'": "&#39;",
  })[each]);
}

/** The editor's rows, read back as the rows `groupsFormBody` takes. */
function rowsFromForm() {
  return [...field("groupsRows").querySelectorAll(".groups__group")].map((box) => ({
    number: box.querySelector('[data-name="number"]').value,
    name: box.querySelector('[data-name="name"]').value,
    series: box.querySelector('[data-name="series"]').value,
    members: [...box.querySelectorAll('[data-name="member"]')].map((each) => each.value),
  }));
}

function draw(bracket) {
  state.bracket = bracket;

  field("caption").textContent = caption(bracket);
  drawRecords(bracket);
  drawStandings(bracket);
  drawGroups(bracket);
  drawKnownPlayers(bracket);
  drawGroupsRows(bracket);
}

/**
 * The view token from a `?token=` link (stored, then stripped from the address bar so it does not
 * show in screen shares), otherwise from browser storage. Same key and behaviour as the dashboard.
 */
function viewToken() {
  const fromLink = new URLSearchParams(window.location.search).get("token");

  if (fromLink) {
    localStorage.setItem(VIEW_TOKEN_KEY, fromLink);
    window.history.replaceState({}, "", window.location.pathname);

    return fromLink;
  }

  return localStorage.getItem(VIEW_TOKEN_KEY) ?? "";
}

async function read() {
  const token = field("viewToken").value.trim();
  const request = bracketRequest(token, window.location);

  try {
    const response = await fetch(request.url, request.init);
    if (!response.ok) {
      // Report in the status corner, so a missing token is not mistaken for an empty tournament.
      field("viewPanel").hidden = false;
      link(response.status !== 401
        ? `the booth answered ${response.status}`
        : token === ""
          ? "paste the view token to read the bracket"
          : "the booth refused that view token", "bad");

      return;
    }

    const bracket = await response.json();

    // A published bracket needs no token, so hide the token box.
    field("viewPanel").hidden = bracket.public === true;
    draw(bracket);
    link("up to date", "ok");
  } catch (failure) {
    // Keep what was drawn last; the status corner shows the failure.
    link("cannot reach the booth", "bad");
  }
}

/**
 * One authenticated write to a route under this page's booth. On refusal the service's own message
 * is shown verbatim.
 */
async function write(route, method, body) {
  const token = field("token").value.trim();
  if (token === "") {
    say("paste the operator token first", "bad");

    return null;
  }

  try {
    const response = await fetch(boothPath(route, window.location), {
      method,
      headers: { "Content-Type": "application/json", Authorization: `Bearer ${token}` },
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

function wireViewToken() {
  const box = field("viewToken");
  box.value = viewToken();

  // Re-read immediately so a pasted token is confirmed without waiting for the poll.
  box.addEventListener("change", () => {
    localStorage.setItem(VIEW_TOKEN_KEY, box.value.trim());
    read();
  });
}

function wireOperator() {
  const toggle = field("operatorToggle");
  const panel = field("operator");

  toggle.addEventListener("click", () => {
    const open = toggle.getAttribute("aria-expanded") === "true";
    toggle.setAttribute("aria-expanded", `${!open}`);
    panel.hidden = open;
  });

  const token = field("token");
  token.value = localStorage.getItem(TOKEN_KEY) ?? "";
  token.addEventListener("change", () => localStorage.setItem(TOKEN_KEY, token.value.trim()));

  const by = field("by");
  by.value = localStorage.getItem(BY_KEY) ?? "";
  by.addEventListener("change", () => localStorage.setItem(BY_KEY, by.value.trim()));

  // Open the operator panel when a token is already stored.
  if (token.value !== "") {
    toggle.setAttribute("aria-expanded", "true");
    panel.hidden = false;
  }

  field("attributeForm").addEventListener("submit", async (event) => {
    event.preventDefault();

    const form = {
      group: field("group").value,
      generation: field("generation").value,
      victim: field("victim").value,
      actor: field("actor").value,
      by: by.value,
    };

    if (!readyToSend(form)) {
      say("the group, the attempt, the victim and who is saying so are all needed", "bad");

      return;
    }

    const written = await write("/attributions", "POST", attributionBody(form));
    if (written === null) return;

    const credited = written.actor ? `${written.actor} credited with ${written.victim}` :
      `${written.victim}'s death credited to nobody`;

    say(`${credited} (row ${written.id})`, "ok");

    // Clear victim and killer for the next attribution; group and attempt usually stay the same.
    field("victim").value = "";
    field("actor").value = "";

    await read();
  });

  field("adjustForm").addEventListener("submit", async (event) => {
    event.preventDefault();

    const body = adjustmentBody({
      group: field("adjustGroup").value,
      player: field("adjustPlayer").value,
      points: field("adjustPoints").value,
      reason: field("adjustReason").value,
      by: by.value,
    });

    if (body.by === "") {
      say("fill in \"said by\" in the kill form above: an adjustment is signed too", "bad");

      return;
    }

    const written = await write("/adjustments", "POST", body);
    if (written === null) return;

    const signed = written.points > 0 ? `+${written.points}` : `${written.points}`;
    say(`${signed} for ${written.player} in game ${written.group} (row ${written.id})`, "ok");

    field("adjustPoints").value = "";
    field("adjustReason").value = "";

    await read();
  });

  // Editor events are delegated on the form because rows are rebuilt on poll. Any input marks the
  // editor as the operator's, so polls stop overwriting it.
  field("groupsForm").addEventListener("input", (event) => {
    if (event.target.matches("[data-name]")) state.mine = true;
  });

  field("groupsForm").addEventListener("click", (event) => {
    const target = event.target.closest("button");

    if (target?.dataset.removeGroup !== undefined) {
      state.mine = true;
      target.closest(".groups__group").remove();

      return;
    }

    if (target?.dataset.removeMember !== undefined) {
      state.mine = true;
      target.closest(".groups__member").remove();

      return;
    }

    if (target?.dataset.addMember !== undefined) {
      state.mine = true;
      target.closest(".groups__group").querySelector(".groups__members").append(memberBox(""));
    }
  });

  field("addGroup").addEventListener("click", () => {
    state.mine = true;
    field("groupsRows").append(
      groupRow({ number: rowsFromForm().length + 1, name: "", series: "", members: [] }),
    );
  });

  field("revertGroups").addEventListener("click", () => {
    state.mine = false;
    drawGroupsRows(state.bracket);
    say("back to what the booth holds", "");
  });

  field("groupsForm").addEventListener("submit", async (event) => {
    event.preventDefault();

    const rows = rowsFromForm();
    if (!groupsFormReady(rows)) {
      say("every group needs a number and a name, and a player is named once in their group", "bad");

      return;
    }

    const written = await write("/groups", "PUT", groupsFormBody(rows, state.bracket?.final?.spec ?? []));
    if (written === null) return;

    say(`the bracket is ${written.groups} group(s)`, "ok");
    state.mine = false;

    await read();
  });
}

// wireViewToken must run before the first read, which takes the token from the field.
wireViewToken();
wireOperator();
read();
setInterval(read, POLL_MS);
