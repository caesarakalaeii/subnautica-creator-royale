// dashboard.js is the caster dashboard's DOM layer: logic comes from view.js, match data from
// feed.js. Roster rows are kept and updated in place, keyed by player name, so open inventories
// and button focus survive each push.

import { storageKey } from "./booth.js";
import {
  PUBLISH_MS,
  cameraSocket,
  createCameraLink,
  createPublisher,
  mayDrive,
} from "./camera.js";
import { createFeed, liveBase } from "./feed.js";
import { mergeEvents } from "./events.js";
import { FEED_SHOWN, createFeedView } from "./feedview.js";
import { mapMarkers, mapPods, mapRing } from "./mapview.js";
import { playerColours } from "./palette.js";
import { cutLabel } from "./scene.js";
// Shared with the overlay so both pages read the table the same way.
import { tournamentTable } from "./overlays.js";
import {
  ABSENT,
  clock,
  countdown,
  metres,
  orderPlayers,
  phaseProgress,
  rifleReading,
  sphereText,
  stackCount,
  suitWorn,
  vitalLevel,
  zoneStanding,
} from "./view.js";

// The view token's booth-scoped storage key. Not a cookie, which would be sent with every asset
// request.
const TOKEN_KEY = storageKey("lms.viewToken", window.location);

// The operator's last map view (flat or solid), remembered across reloads.
const VIEW_KEY = "lms.mapView";

// The last map size. The depth cut is deliberately not remembered, so a reload never opens on a
// half-cut world.
const SIZE_KEY = "lms.mapSize";

// The feed states that mean the operator has to type something before anything can be drawn.
const NEEDS_A_TOKEN = new Set(["blocked", "refused"]);

// Joins row orders for comparison; NUL cannot occur in a player name. Written as an escape so
// tools do not treat this file as binary.
const SEPARATOR = "\u0000";

// How many columns a row spans, for the inventory panel underneath it.
const COLUMNS = 14;

// Map colours. The ring colours match the ones players see in game.
const NOW_RING = "#ff5b5b";
const NEXT_RING = "#5fe3f0";
const ALIVE_MARK = "#7fd88a";
// Pod colours: light and desaturated so pods stay visible on the teal terrain without competing
// with player markers, on a dark backing stroke.
const POD_MARK = "#a8c6d4";
const POD_DERELICT = "#63848f";
const POD_BACKING = "#02070b";
const OUT_MARK = "#ff5b5b";
const DEAD_MARK = "#54707e";
const LABEL_FONT = "ui-monospace, Menlo, Consolas, monospace";

const field = (name) => document.querySelector(`[data-field="${name}"]`);

const el = {
  group: field("group"),
  elapsed: field("elapsed"),
  phase: field("phase"),
  untilNext: field("untilNext"),
  alive: field("alive"),
  state: field("state"),
  phaseFill: field("phaseFill"),
  zoneNow: field("zoneNow"),
  zoneNext: field("zoneNext"),
  damage: field("damage"),
  outside: field("outside"),
  link: field("link"),
  roster: field("roster"),
  empty: field("empty"),
  mapCanvas: field("mapCanvas"),
  mapCanvas3d: field("mapCanvas3d"),
  mapPanel: field("mapPanel"),
  mapToggle: field("mapToggle"),
  mapSize: field("mapSize"),
  mapCutSlider: field("mapCutSlider"),
  mapCutValue: field("mapCutValue"),
  feedList: field("feedList"),
  feedCount: field("feedCount"),
  feedEmpty: field("feedEmpty"),
  table: field("table"),
  tableCaption: field("tableCaption"),
  tableRows: field("tableRows"),
  gate: field("gate"),
  gateForm: field("gateForm"),
  gateWhy: field("gateWhy"),
};

const rows = new Map();

// The timeline, capped here; /events.html shows the whole window.
const feedView = createFeedView({
  list: el.feedList,
  count: el.feedCount,
  empty: el.feedEmpty,
  cap: FEED_SHOWN,
});

// The last push, kept so inventories and map resizes can redraw without waiting for the next one.
let players = [];
let pods = [];
let zone = null;
let phase = null;
let timeline = null;
let feed = null;

// The solid map, built lazily on first use since Three.js and the terrain mesh are large.
let solid = null;

// The in-flight build, so overlapping requests share it: a second WebGL context on the same canvas
// hangs rather than failing.
let building = null;

// The toggle's original title, saved while a failed load replaces it.
let toggleTitle = null;

// The camera control channel this dashboard drives, or null unless the address has `?control=`.
let driving = null;

boot();

function boot() {
  el.gateForm.addEventListener("submit", () => {
    const typed = String(new FormData(el.gateForm).get("token") ?? "").trim();
    if (!typed) return;

    localStorage.setItem(TOKEN_KEY, typed);

    // A feed holds the token it was made with, so a new token means a new feed.
    feed?.stop();
    start(typed);
  });

  // Block Escape: a closed gate with no token would never reopen until a reload.
  el.gate.addEventListener("cancel", (event) => event.preventDefault());

  // The canvases are sized in device pixels from their boxes, so redraw on resize.
  window.addEventListener("resize", () => {
    drawMap();
    solid?.resize();
  });

  el.mapToggle.addEventListener("click", () => {
    showMap(el.mapPanel.dataset.view === "solid" ? "flat" : "solid");
  });

  el.mapSize.addEventListener("click", () => {
    sizeMap(document.body.dataset.map === "large" ? "inset" : "large");
  });

  // `input` rather than `change`, so the cut follows the thumb; it only moves a clipping plane.
  el.mapCutSlider.addEventListener("input", () => {
    cutTo(Number(el.mapCutSlider.value));
  });

  sizeMap(localStorage.getItem(SIZE_KEY) === "large" ? "large" : "inset");
  if (localStorage.getItem(VIEW_KEY) === "solid") showMap("solid");

  start(token());
}

function start(value) {
  feed = createFeed({
    base: liveBase(window.location),
    token: value,
    connect: (url) => new WebSocket(url),
    onSnapshot: draw,
    onStatus: link,
  });

  feed.start();
}

/**
 * The view token from a `?token=` link (stored, then stripped from the address bar so it does not
 * show in screen shares), otherwise from browser storage.
 */
function token() {
  const fromLink = new URLSearchParams(window.location.search).get("token");

  if (fromLink) {
    localStorage.setItem(TOKEN_KEY, fromLink);
    window.history.replaceState({}, "", window.location.pathname);

    return fromLink;
  }

  return localStorage.getItem(TOKEN_KEY) ?? "";
}

function link(state, detail) {
  el.link.dataset.state = state;
  el.link.querySelector(".link__text").textContent = detail || state;

  // The bar truncates long text, so the full text goes on the title.
  el.link.title = detail || state;

  if (NEEDS_A_TOKEN.has(state)) {
    el.gateWhy.textContent = state === "refused" ? detail : "";
    if (!el.gate.open) el.gate.showModal();
    return;
  }

  if (state === "live" && el.gate.open) el.gate.close();
}

function draw(snapshot) {
  players = snapshot.players ?? [];
  pods = snapshot.pods ?? [];
  zone = snapshot.zone ?? null;

  drawMatch(snapshot.match);
  drawZone(snapshot.zone);
  drawRoster();
  drawMap();
  solid?.update({ zone, players, pods });
  drawFeed(snapshot.events);
  drawTable(snapshot.standings);
}

/**
 * Draws the tournament table as received (the mod already orders and places rows). Hidden until a
 * match has finished.
 */
function drawTable(standings) {
  const table = tournamentTable(standings);

  el.table.hidden = table === null;
  if (!table) return;

  el.tableCaption.textContent = table.caption;
  el.tableRows.replaceChildren(...table.rows.map((row, index) => {
    const tied = table.rows.some((other, at) => at !== index && other.place === row.place);
    const tr = document.createElement("tr");

    tr.dataset.lead = String(row.name === table.lead);
    tr.dataset.shared = String(tied);
    tr.append(
      cell("place", String(row.place), "table__place"),
      cell("name", row.name, "table__name"),
      cell("played", String(row.played), "table__num"),
      cell("wins", String(row.wins), "table__num"),
      cell("kills", String(row.kills), "table__num"),
      cell("points", String(row.points), "table__num table__points"),
    );

    return tr;
  }));
}

/**
 * Switches the map panel between flat and solid, drawing the state already in hand. If the solid map
 * cannot be built, the flat map stays and the button says why.
 */
async function showMap(view) {
  if (view === "solid" && solid === null) {
    if (building === null) {
      // Progress indicator: the mesh is tens of megabytes.
      el.mapToggle.textContent = "…";
      el.mapToggle.disabled = true;
      building = (async () => {
        try {
          const { createSolidMap } = await import("./map3d.js");

          return await createSolidMap(el.mapCanvas3d);
        } catch (failure) {
          console.error("the solid map would not build", failure);

          return failure;
        }
      })();
    }

    const built = await building;
    el.mapToggle.disabled = false;

    // A failed load (e.g. a cut-off download) can be retried. No WebGL is permanent:
    // `createSolidMap` returns null for that.
    if (built instanceof Error) {
      building = null;
      el.mapToggle.textContent = "3D";
      toggleTitle ??= el.mapToggle.title;
      el.mapToggle.title = "The solid map did not load. Press to try again; the flat map is unaffected.";
      localStorage.removeItem(VIEW_KEY);

      return;
    }

    solid = built;

    if (solid === null) {
      el.mapToggle.textContent = "2D only";
      el.mapToggle.disabled = true;
      el.mapToggle.title = "This browser would not give the map WebGL. The flat map is unaffected.";
      localStorage.removeItem(VIEW_KEY);

      return;
    }

    if (toggleTitle !== null) el.mapToggle.title = toggleTitle;

    driveTheSources(solid);

    // The slider range comes from the mesh metadata; the HTML values are placeholders.
    const range = solid.cutRange;
    el.mapCutSlider.min = String(range.min);
    el.mapCutSlider.max = String(range.max);
    el.mapCutSlider.step = String(range.step);
    cutTo(Number(el.mapCutSlider.value));
  }

  el.mapPanel.dataset.view = view;
  el.mapToggle.textContent = view === "solid" ? "2D" : "3D";
  el.mapToggle.setAttribute("aria-pressed", view === "solid" ? "true" : "false");
  localStorage.setItem(VIEW_KEY, view);

  if (view === "solid") {
    solid.resize();
    solid.update({ zone, players, pods });
  } else {
    drawMap();
  }
}

/**
 * Publishes this dashboard's camera to the control channel named in the address. Requires the
 * operator token, since driving is a write. Camera moves are throttled to `PUBLISH_MS`.
 */
function driveTheSources(map) {
  const secret = mayDrive(window.location, window.localStorage);
  if (!secret || driving) return;

  driving = createCameraLink({
    url: cameraSocket(window.location, token()),
    driving: true,
    token: secret,
    connect: (url, protocols) => new WebSocket(url, protocols),
  });

  driving.start();

  const publisher = createPublisher({
    windowMs: PUBLISH_MS,
    send: (camera) => driving.publish(camera),
  });

  map.onMove(() => publisher.moved(map.look()));
}

/**
 * Switches the map between inset and large. Only the grid placement changes (`style.css`), so the
 * WebGL context and loaded mesh survive; the large layout hides the roster.
 */
function sizeMap(size) {
  document.body.dataset.map = size;
  el.mapSize.textContent = size === "large" ? "small" : "big";
  el.mapSize.setAttribute("aria-pressed", size === "large" ? "true" : "false");
  localStorage.setItem(SIZE_KEY, size);

  // Neither canvas resizes itself.
  drawMap();
  solid?.resize();
}

/** Cuts away terrain above `depth` metres to see into caves. Zero is off. */
function cutTo(depth) {
  el.mapCutValue.textContent = cutLabel(depth);
  solid?.cut(depth);
}

function drawMatch(match) {
  el.group.textContent = match ? `group ${match.group} · run ${match.generation}` : "no match";
  el.elapsed.textContent = clock(match?.elapsedMs);
  el.phase.textContent = match ? `${match.phase} / ${match.phases}` : ABSENT;
  el.untilNext.textContent = countdown(match?.untilNextMs);
  el.alive.textContent = match ? String(match.alive) : ABSENT;
  el.state.textContent = match ? match.state.toLowerCase() : "no match";
  el.state.closest(".readout").dataset.state = match?.state ?? "";

  phase = phaseProgress(phase, match);
  el.phaseFill.style.width = `${phase.fraction * 100}%`;
}

function drawZone(zone) {
  // The banner truncates with an ellipsis, so each value is also its title.
  const shown = (field, text) => {
    field.textContent = text;
    field.title = text;
  };

  shown(el.zoneNow, sphereText(zone?.now));

  // The last phase closes onto its own centre, sent as a null `next`.
  shown(el.zoneNext, zone ? (zone.next ? sphereText(zone.next) : "its own centre") : ABSENT);
  shown(el.damage, zone ? `${zone.damagePerTick} a tick` : ABSENT);

  const inTheWall = players.filter((player) => player.alive && !player.insideZone).length;
  shown(el.outside, inTheWall === 0 ? "nobody" : `${inTheWall} in the wall`);
  el.outside.dataset.danger = String(inTheWall > 0);
}

function drawRoster() {
  const ordered = orderPlayers(players);

  // Hidden in the markup; only a received push can show it.
  el.empty.hidden = ordered.length > 0;

  for (const player of ordered) {
    writeRow(rowFor(player.name), player);
  }

  // Remove rows for players who left.
  for (const [name, row] of rows) {
    if (ordered.some((player) => player.name === name)) continue;

    row.tr.remove();
    row.stacks.remove();
    rows.delete(name);
  }

  reorder(ordered);
}

/**
 * Draws the flat map: the current and next zone rings, pods, and player markers back to front.
 * Positions come from `mapview.js`.
 */
function drawMap() {
  const canvas = el.mapCanvas;
  const box = canvas.getBoundingClientRect();
  if (box.width === 0) return;

  // Device pixels, so thin lines stay sharp on high-density screens.
  const ratio = window.devicePixelRatio || 1;
  const width = Math.round(box.width * ratio);
  const height = Math.round(box.height * ratio);

  if (canvas.width !== width || canvas.height !== height) {
    canvas.width = width;
    canvas.height = height;
  }

  const context = canvas.getContext("2d");
  context.setTransform(ratio, 0, 0, ratio, 0, 0);
  context.clearRect(0, 0, box.width, box.height);

  // Next ring first, so the current one draws over it. Rings are sphere equators, so an "outside"
  // marker can appear inside a ring when the player is deeper than the sphere reaches there; the
  // marker reflects the server's 3D containment check.
  ring(context, box, mapRing(zone?.next), NEXT_RING);
  ring(context, box, mapRing(zone?.now), NOW_RING);

  // Pods under the players, sharing each owner's palette colour; unowned pods are not drawn.
  const colours = playerColours(players);
  for (const landmark of mapPods(pods, players, colours)) {
    pod(context, box, landmark);
  }

  for (const marker of mapMarkers(players, colours)) {
    dot(context, box, marker);
  }
}

function ring(context, box, circle, colour) {
  if (!circle) return;

  // One radius scaled by width; the panel is kept square so circles stay round.
  context.save();
  context.strokeStyle = colour;
  context.lineWidth = 1.5;
  context.setLineDash(colour === NEXT_RING ? [4, 4] : []);
  context.beginPath();
  context.arc(circle.u * box.width, circle.v * box.height, circle.r * box.width, 0, Math.PI * 2);
  context.stroke();
  context.restore();
}

/**
 * One escape pod: a hollow square (a dot is a living player, a cross a body), with a dark backing
 * stroke for contrast against the terrain.
 */
function pod(context, box, landmark) {
  const x = landmark.u * box.width;
  const y = landmark.v * box.height;
  const side = 8;
  const corner = side / 2;

  context.save();

  context.strokeStyle = POD_BACKING;
  context.lineWidth = 3;
  context.strokeRect(x - corner, y - corner, side, side);

  // The owner's colour when paired; otherwise the default pod tone, or grey for a derelict.
  context.strokeStyle = landmark.assigned > 0
    ? (landmark.colour?.css ?? POD_MARK)
    : POD_DERELICT;
  context.lineWidth = 1.4;
  context.strokeRect(x - corner, y - corner, side, side);

  context.restore();
}

/**
 * One player marker in their palette colour. A body is a dim cross; a living player inside the zone
 * is a filled dot; outside the zone (or off the map) a hollow dot with an extra ring.
 */
function dot(context, box, marker) {
  // Off-map players are pinned to the border here and drawn hollow and labelled.
  const x = Math.min(box.width, Math.max(0, marker.u * box.width));
  const y = Math.min(box.height, Math.max(0, marker.v * box.height));

  context.save();

  if (!marker.alive) {
    // Bodies are uncoloured crosses.
    context.strokeStyle = DEAD_MARK;
    context.lineWidth = 1.2;
    context.beginPath();
    context.moveTo(x - 3, y - 3);
    context.lineTo(x + 3, y + 3);
    context.moveTo(x + 3, y - 3);
    context.lineTo(x - 3, y + 3);
    context.stroke();
    context.restore();
    return;
  }

  const ink = marker.colour?.css ?? (marker.inside ? ALIVE_MARK : OUT_MARK);

  if (marker.onMap && marker.inside) {
    context.fillStyle = ink;
    context.strokeStyle = "#02070b";
    context.lineWidth = 1.5;
    context.beginPath();
    context.arc(x, y, 4, 0, Math.PI * 2);
    context.fill();
    context.stroke();
  } else {
    // Outside the zone or off the map: hollow, plus an outer ring.
    context.strokeStyle = ink;
    context.lineWidth = 1.5;
    context.beginPath();
    context.arc(x, y, 4, 0, Math.PI * 2);
    context.stroke();
    context.beginPath();
    context.arc(x, y, 6.5, 0, Math.PI * 2);
    context.stroke();
  }

  // Labels only for players outside the zone or off the map.
  if (!marker.inside || !marker.onMap) {
    // Labels on the right half point inwards so they stay inside the panel.
    const inwards = x > box.width / 2;

    context.fillStyle = ink;
    context.font = "11px " + LABEL_FONT;
    context.textBaseline = "middle";
    context.textAlign = inwards ? "right" : "left";
    context.shadowColor = "#000";
    context.shadowBlur = 4;
    context.fillText(marker.name, inwards ? x - 8 : x + 8, y);
  }

  context.restore();
}

/** Merges new events into the timeline (newest first) and redraws it if it changed. */
function drawFeed(events) {
  const before = timeline;
  timeline = mergeEvents(timeline, events);

  if (before === timeline) return;

  feedView.draw(timeline.entries);
}

function rowFor(name) {
  const existing = rows.get(name);
  if (existing) return existing;

  const tr = document.createElement("tr");
  tr.dataset.name = name;
  tr.append(
    cell("name", nameCell(name)),
    meterCell("health"),
    numberCell("oxygen"),
    meterCell("food"),
    meterCell("water"),
    numberCell("depth"),
    cell("position", null, "col-pos"),
    textCell("biome", "col-biome"),
    textCell("vehicle", "col-vehicle"),
    // Combat-relevant: worn suit, held item, stasis rifle.
    textCell("suit", "col-suit"),
    textCell("held", "col-held"),
    cell("rifle", null, "col-rifle"),
    cell("zone", null, "zone-cell"),
    packCell(name),
  );

  const stacks = document.createElement("tr");
  stacks.className = "stacks";

  const holder = document.createElement("td");
  holder.colSpan = COLUMNS;
  stacks.append(holder);

  const row = { tr, stacks, expanded: false };
  rows.set(name, row);
  el.roster.append(tr, stacks);

  return row;
}

function writeRow(row, player) {
  const { tr } = row;
  const standing = zoneStanding(player);

  tr.classList.toggle("row--dead", !player.alive);
  tr.classList.toggle("row--out", player.alive && !standing.inside);

  meter(tr, "health", player.health);
  meter(tr, "food", player.food);
  meter(tr, "water", player.water);

  // Oxygen is in seconds with a tank-dependent maximum, so it is a coloured number, not a bar.
  figure(tr, "oxygen", Math.round(player.oxygen), vitalLevel("oxygen", player.oxygen));
  figure(tr, "depth", metres(player.depth), "ok");

  write(tr, "position", `${Math.round(player.x)} / ${Math.round(player.z)}`);
  write(tr, "biome", player.biome);
  write(tr, "vehicle", player.vehicle);
  write(tr, "held", player.held);

  // The suit column shows the body slot; the full equipment set is in its title.
  const suit = suitWorn(player.equipment);
  write(tr, "suit", suit.label === ABSENT ? null : suit.label);
  cellOf(tr, "suit").title = suit.title;

  const rifle = rifleReading(player);
  write(tr, "rifle", rifle.label === ABSENT ? null : rifle.label);
  cellOf(tr, "rifle").dataset.charging = String(rifle.charging);

  // Frozen in a stasis field: shown as a row state rather than a column, to keep the table narrow.
  tr.dataset.frozen = String(Boolean(player.frozen));
  cellOf(tr, "name").title = player.frozen ? "held in a stasis field" : "";

  write(tr, "zone", standing.label);
  cellOf(tr, "zone").dataset.inside = String(standing.inside);

  const total = stackCount(player.inventory);
  const pack = cellOf(tr, "pack").firstElementChild;
  pack.textContent = String(total);

  if (row.expanded) drawStacks(row, player.inventory);
}

function drawStacks(row, inventory) {
  const holder = row.stacks.firstElementChild;

  holder.replaceChildren(inventory?.length ? stackList(inventory) : note("carrying nothing"));
}

function stackList(inventory) {
  const list = document.createElement("ul");
  list.className = "stacks__list";

  for (const line of inventory) {
    const item = document.createElement("li");
    item.className = "stacks__item";
    item.append(document.createTextNode(`${line.item} `), count(line.count));
    list.append(item);
  }

  return list;
}

/**
 * Reorders the rows, only when the order actually changed, so keyboard focus is not dropped on
 * every push.
 */
function reorder(ordered) {
  const wanted = ordered.map((player) => player.name);
  const current = [...el.roster.children]
    .filter((child) => !child.classList.contains("stacks"))
    .map((child) => child.dataset.name);

  if (wanted.join(SEPARATOR) === current.join(SEPARATOR)) return;

  for (const name of wanted) {
    const row = rows.get(name);
    el.roster.append(row.tr, row.stacks);
  }
}

// ---- helpers ---------------------------------------------------------------------------

function cell(name, child, className) {
  const td = document.createElement("td");
  td.dataset.cell = name;
  if (className) td.className = className;
  if (child) td.append(child);

  return td;
}

function nameCell(name) {
  const holder = document.createElement("span");
  holder.className = "name";

  const pip = document.createElement("span");
  pip.className = "name__pip";

  holder.append(pip, document.createTextNode(name));

  return holder;
}

/**
 * A text cell with capped width. The cap is on an inner element because auto-layout tables ignore
 * `max-width` on a `<td>`; `write()` puts the full value on the title.
 */
function textCell(name, width) {
  const clip = document.createElement("span");
  clip.className = "clip";

  return cell(name, clip, `col-text ${width}`);
}

function meterCell(name) {
  const fill = document.createElement("span");
  fill.className = "meter__fill";

  const track = document.createElement("span");
  track.className = "meter__track";
  track.append(fill);

  const value = document.createElement("span");
  value.className = "meter__figure";

  const holder = document.createElement("span");
  holder.className = "meter";
  holder.append(value, track);

  return cell(name, holder, "col-meter");
}

function numberCell(name) {
  return cell(name, null, "col-num");
}

function packCell(name) {
  const button = document.createElement("button");
  button.type = "button";
  button.className = "pack";
  button.setAttribute("aria-expanded", "false");
  button.title = `what ${name} is carrying`;
  button.addEventListener("click", () => toggle(name));

  return cell("pack", button, "col-pack");
}

function toggle(name) {
  const row = rows.get(name);
  if (!row) return;

  row.expanded = !row.expanded;
  row.stacks.classList.toggle("stacks--open", row.expanded);
  cellOf(row.tr, "pack").firstElementChild.setAttribute("aria-expanded", String(row.expanded));

  // Draw from the last push now, rather than opening empty until the next one.
  if (row.expanded) {
    drawStacks(row, players.find((player) => player.name === name)?.inventory);
  }
}

function cellOf(tr, name) {
  return tr.querySelector(`[data-cell="${name}"]`);
}

/**
 * Writes one cell and puts the full value on its title, since text columns are width-capped.
 */
function write(tr, name, value) {
  const td = cellOf(tr, name);
  const target = td.querySelector(".clip") ?? td;
  const absent = value === null || value === undefined || value === "";

  target.textContent = absent ? ABSENT : value;
  td.title = absent ? "" : String(value);
  td.classList.toggle("absent", absent);
}

function meter(tr, name, value) {
  const td = cellOf(tr, name);

  td.dataset.level = vitalLevel(name, value);
  td.querySelector(".meter__figure").textContent = String(Math.round(value));
  td.querySelector(".meter__fill").style.width = `${Math.min(100, Math.max(0, value))}%`;
}

function figure(tr, name, value, level) {
  const td = cellOf(tr, name);

  td.dataset.level = level;
  td.textContent = String(value);
}

function count(value) {
  const holder = document.createElement("b");
  holder.textContent = `×${value}`;

  return holder;
}

function note(text) {
  const holder = document.createElement("span");
  holder.className = "stacks__none";
  holder.textContent = text;

  return holder;
}
