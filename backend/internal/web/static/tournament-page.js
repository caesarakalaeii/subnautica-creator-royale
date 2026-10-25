// tournament-page.js is the DOM half of the tournament overview; the logic lives in tournament.js.

import { storageKey } from "./booth.js";
import { bracketRequest } from "./bracket.js";
import { SCHEDULE, current, dateOf, kindOf, slotState, stages, timeOf, until } from "./tournament.js";

// A published bracket needs no token. Before that, only a caster with the view token sees players.
const VIEW_TOKEN_KEY = storageKey("lms.viewToken", window.location);

// Players change rarely, so poll slowly; the clock tick moves the live row.
const POLL_MS = 30_000;
const TICK_MS = 15_000;

// How each stage advances players, as shown to viewers.
const ONWARD = {
  "Winners semi 1": "top 2 go to the grand final",
  "Winners semi 2": "top 2 go to the grand final",
  "Losers bracket": "the winner goes to the grand final",
  "Grand final": "five players, three games",
};

const field = (name) => document.querySelector(`[data-field="${name}"]`);
const now = () => Date.now() / 1000;

let bracket = null;

function el(tag, className, text) {
  const node = document.createElement(tag);
  if (className) node.className = className;
  if (text !== undefined) node.textContent = text;

  return node;
}

function membersOf(group) {
  return bracket?.groups?.find((each) => each.number === group)?.members ?? [];
}

function span(slot) {
  return slot.end === null ? `${timeOf(slot.start)} –` : `${timeOf(slot.start)} – ${timeOf(slot.end)}`;
}

function drawNow() {
  const at = now();
  const { live, next } = current(at);

  field("live").textContent = live?.title ?? "off air";
  field("liveWhen").textContent = live
    ? [live.end === null ? "" : `until ${timeOf(live.end)}`, membersOf(live.group).join(" · ")].filter(Boolean).join(" · ")
    : "";

  field("next").textContent = next?.title ?? "that was the tournament";
  field("nextWhen").textContent = next ? `${dateOf(next.start)}, ${timeOf(next.start)} · ${until(at, next.start)}` : "";
}

function drawFlow() {
  const all = stages(bracket);
  const columns = [
    ["day 1 · groups", all.filter((stage) => stage.day === 1)],
    ["day 2 · semi-finals and losers", all.filter((stage) => stage.day === 2 && stage.name !== "Grand final")],
    ["day 2 · grand final", all.filter((stage) => stage.name === "Grand final")],
  ];

  field("flow").replaceChildren(
    ...columns.map(([heading, list]) => {
      const column = el("div", "flow__col");
      column.append(el("h3", "flow__head", heading));

      for (const stage of list) {
        const card = el("article", stage.name === "Grand final" ? "card card--final" : "card");
        const head = el("h3", "card__head");
        head.append(el("span", "card__name", stage.name));
        card.append(head);

        const members = el("ol", "card__members");
        stage.members.forEach((name, i) => {
          const item = el("li", undefined, name);
          if (stage.notes?.[i]) item.append(el("span", "card__from", ` · ${stage.notes[i]}`));
          members.append(item);
        });
        card.append(members);

        if (stage.members.length === 0) {
          card.append(el("p", "card__empty", stage.day === 1 ? "players to be announced" : "decided as the event plays"));
        }
        if (ONWARD[stage.name]) card.append(el("p", "card__note", ONWARD[stage.name]));

        column.append(card);
      }

      return column;
    }),
  );
}

function drawDays() {
  const at = now();
  const { next } = current(at);

  field("days").replaceChildren(
    ...SCHEDULE.map((day) => {
      const box = el("section", "day");
      const head = el("h3", "day__head", `Day ${day.day}`);
      head.append(el("span", "day__date", dateOf(day.slots[0].start)));
      box.append(head);

      const list = el("ol", "slots");
      for (const slot of day.slots) {
        const row = el("li", "slot");
        row.dataset.state = slotState(slot, at);
        row.dataset.kind = kindOf(slot);
        if (slot === next) row.dataset.next = "true";

        row.append(el("span", "slot__time", span(slot)), el("span", "slot__title", slot.title));

        const who = membersOf(slot.group);
        if (who.length > 0) row.append(el("span", "slot__who", who.join(" · ")));

        list.append(row);
      }
      box.append(list);

      return box;
    }),
  );
}

function draw() {
  drawNow();
  drawFlow();
  drawDays();
}

function say(text, tone) {
  const link = field("link");
  link.dataset.tone = tone;
  link.querySelector(".link__text").textContent = text;
}

async function read() {
  let token = "";
  try {
    token = localStorage.getItem(VIEW_TOKEN_KEY) ?? "";
  } catch {
    // Storage blocked: carry on without a token.
  }

  const request = bracketRequest(token, window.location);

  try {
    const response = await fetch(request.url, request.init);
    if (!response.ok) {
      say(response.status === 401 ? "players appear once the bracket is published" : `the booth answered ${response.status}`, "warn");

      return;
    }

    bracket = await response.json();
    say("up to date", "ok");
    draw();
  } catch {
    // Keep what was drawn; the schedule needs no booth.
    say("cannot reach the booth", "bad");
  }
}

const zone = Intl.DateTimeFormat().resolvedOptions().timeZone;
field("zone").textContent = `last man standing · times in ${zone}`;
field("zoneName").textContent = `· your time, ${zone}`;

draw();
read();
setInterval(read, POLL_MS);
setInterval(() => {
  drawNow();
  drawDays();
}, TICK_MS);
