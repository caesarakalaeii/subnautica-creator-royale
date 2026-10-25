// overlay-page.js drives the OBS browser sources: one panel per page, chosen by `?panel=`, with the
// token in the same query.
//
// Overlays are on camera, so they fail invisibly: a dropped feed keeps the last frame, and no match
// draws an empty frame. Errors are logged, never drawn.

import { cameraSocket, createCameraLink, onAChannel } from "./camera.js";
import { createFeed, liveBase } from "./feed.js";
import {
  PANELS,
  exitNote,
  keepEvents,
  mapOptions,
  mapState,
  matchBoard,
  overlayPanel,
  overlayToken,
  overlayURL,
  panelShowing,
  toastQueue,
  tournamentTable,
} from "./overlays.js";
import { clock, countdown, phaseProgress } from "./view.js";

// A phase's last minute, which the countdown highlights.
const SOON_MS = 60_000;

// Toasts must expire without a push, so the queue is also swept on a timer.
const SWEEP_MS = 250;

const field = (name) => document.querySelector(`[data-field="${name}"]`);

const el = {
  mapCanvas: field("mapCanvas"),
  alive: field("alive"),
  phase: field("phase"),
  untilNext: field("untilNext"),
  phaseFill: field("phaseFill"),
  toasts: field("toasts"),
  boardRows: field("boardRows"),
  tableCaption: field("tableCaption"),
  tableRows: field("tableRows"),
  setupUrls: field("setupUrls"),
  setupLink: field("setupLink"),
};

const panel = overlayPanel(window.location);
const cards = new Map();

let toasts = null;
let kept = null;
let phase = null;

// The solid map once built; stays null if WebGL is unavailable.
let solid = null;

// The last map state, applied as soon as the (slow-loading) map finishes building.
let mapped = null;

// The last camera from the control channel, kept for the same reason. Null means use the
// address's framing.
let aimed = null;

boot();

function boot() {
  document.body.dataset.panel = panel;

  if (panel === "setup") describeTheSources();

  if (panel === "map") {
    buildTheMap();
    followTheCaster();

    // The canvas is sized in device pixels from its box, so it must be told when OBS resizes it.
    window.addEventListener("resize", () => solid?.resize());
  }

  const feed = createFeed({
    base: liveBase(window.location),
    token: overlayToken(window.location, window.localStorage),
    connect: (url) => new WebSocket(url),
    onSnapshot: draw,

    // Exposed only as a data attribute (used by the setup card's CSS); live panels never show errors.
    onStatus: (state) => {
      document.documentElement.dataset.link = state;
      if (panel === "setup") el.setupLink.textContent = state;
    },
  });

  feed.start();

  if (panel === "toasts") setInterval(sweep, SWEEP_MS);
}

/**
 * Builds the solid map on this page's canvas, reusing the dashboard's `map3d.js`. Imported
 * dynamically so the other panels do not load Three.js and the large terrain mesh. The framing
 * comes from the address.
 */
async function buildTheMap() {
  // Covers the first draw too: this is not awaited, so anything escaping would be an unhandled
  // rejection.
  try {
    const { createSolidMap } = await import("./map3d.js");

    solid = await createSolidMap(el.mapCanvas, mapOptions(window.location));
    if (solid === null) return;

    solid.resize();
    if (mapped) solid.update(mapped);
    if (aimed) solid.aim(aimed);
  } catch (failure) {
    console.error("the map source would not build", failure);
  }
}

/**
 * Follows the caster driving this source's control channel, only when the address names one. Uses
 * the view token, which can follow but never drive.
 */
function followTheCaster() {
  if (!onAChannel(window.location)) return;

  const token = overlayToken(window.location, window.localStorage);
  if (!token) return;

  createCameraLink({
    url: cameraSocket(window.location, token),
    driving: false,
    token,
    connect: (url) => new WebSocket(url),
    onCamera: (camera) => {
      aimed = camera;
      solid?.aim(camera);
    },
  }).start();
}

function draw(snapshot) {
  document.body.dataset.showing = panelShowing(panel, snapshot) ? "yes" : "no";

  if (panel === "status") drawStatus(snapshot.match);
  if (panel === "board") {
    // Accumulated, because each push carries only new events.
    kept = keepEvents(kept, snapshot.events);
    drawBoard(snapshot.players, kept.events, snapshot.match);
  }
  if (panel === "toasts") {
    toasts = toastQueue(toasts, snapshot.events, Date.now());
    drawToasts();
  }
  if (panel === "table") drawTable(snapshot.standings);
  if (panel === "map") {
    mapped = mapState(snapshot);
    solid?.update(mapped);
  }
}

function drawStatus(match) {
  if (!match) return;

  el.alive.textContent = String(match.alive);
  el.phase.textContent = `${match.phase} / ${match.phases}`;
  el.untilNext.textContent = countdown(match.untilNextMs);

  const soon = match.untilNextMs !== null && match.untilNextMs !== undefined
    && match.untilNextMs <= SOON_MS;
  el.untilNext.closest(".strip__cell").dataset.soon = String(soon);

  phase = phaseProgress(phase, match);
  el.phaseFill.style.width = `${phase.fraction * 100}%`;
}

function sweep() {
  const before = toasts?.toasts.length ?? 0;

  toasts = toastQueue(toasts, [], Date.now());

  // Redraw only when a toast actually expired.
  if ((toasts.toasts.length ?? 0) !== before) drawToasts();
}

function drawToasts() {
  for (const card of toasts.toasts) {
    if (cards.has(card.key)) continue;

    const li = document.createElement("li");
    li.className = "toast";
    li.dataset.kind = card.kind;

    const mark = document.createElement("span");
    mark.className = "toast__mark";

    const text = document.createElement("span");
    text.className = "toast__text";
    text.textContent = card.text;

    li.append(mark, text);
    cards.set(card.key, li);

    // Newest on top, where the entry animation comes in.
    el.toasts.prepend(li);
  }

  for (const [key, li] of cards) {
    if (toasts.toasts.some((card) => card.key === key)) continue;

    li.remove();
    cards.delete(key);
  }
}

function drawBoard(players, events, match) {
  const rows = matchBoard(players, events, match);
  const lead = rows.length > 1 && rows[0].placement !== rows[1].placement ? rows[0].name : null;

  el.boardRows.replaceChildren(...rows.map((row) => boardRow(row, row.name === lead)));
}

function boardRow(row, lead) {
  const tr = document.createElement("tr");
  tr.dataset.alive = String(row.alive);
  tr.dataset.lead = String(lead);

  // The cause has its own cell: the name cell is struck through for the dead, and a
  // text-decoration would cross out anything inside it.
  tr.append(
    td(String(row.placement), "board__place"),
    td(row.name, "board__name"),
    td(String(row.kills), "board__num"),
    td(String(row.points), "board__num board__points"),
    td(exitNote(row) ?? "", "board__cause"),
  );

  return tr;
}

/** Draws the tournament table as received, replacing it wholesale each frame. */
function drawTable(standings) {
  const table = tournamentTable(standings);
  if (!table) return;

  el.tableCaption.textContent = table.caption;
  el.tableRows.replaceChildren(...table.rows.map((row, index) =>
    tableRow(row, row.name === table.lead, shared(table.rows, index))));
}

/** Whether this row's place is shared with another row. */
function shared(rows, index) {
  return rows.some((other, at) => at !== index && other.place === rows[index].place);
}

function tableRow(row, lead, tied) {
  const tr = document.createElement("tr");
  tr.dataset.lead = String(lead);
  tr.dataset.shared = String(tied);

  tr.append(
    td(String(row.place), "board__place"),
    td(row.name, "board__name"),
    td(String(row.wins), "board__num"),
    td(String(row.kills), "board__num"),
    td(String(row.points), "board__num board__points"),
  );

  return tr;
}

function td(text, className) {
  const cell = document.createElement("td");
  cell.className = className;
  cell.textContent = text;

  return cell;
}

/**
 * Fills the setup card (shown when no valid panel was asked for) with every panel's address, using
 * this page's token.
 */
function describeTheSources() {
  const token = overlayToken(window.location, window.localStorage);

  el.setupUrls.replaceChildren(...Object.entries(PANELS).map(([name, size]) => {
    const li = document.createElement("li");
    const url = document.createElement("b");
    url.textContent = overlayURL(name, window.location, token);

    li.append(url, document.createTextNode(` — ${size}`));

    return li;
  }));
}
