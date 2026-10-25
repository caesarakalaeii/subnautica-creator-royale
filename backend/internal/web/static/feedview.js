// feedview.js draws the event timeline for both the dashboard (newest few) and the events page
// (whole window). Wording and grouping live in events.js; this keeps one DOM node per line across
// pushes so the list stays readable and scrollable.

import { divider, eventLine, eventTone } from "./events.js";
import { clock } from "./view.js";

// Lines shown in the dashboard's panel.
export const FEED_SHOWN = 10;

// Lines the events page keeps. Must match `maxEvents` in backend/internal/caster/store.go.
export const WINDOW_LIMIT = 5000;

/** The lines to draw, newest first. A cap of 0 draws all of them. */
export function shown(entries, cap) {
  return cap > 0 ? entries.slice(0, cap) : entries;
}

/** The count in the panel's head, e.g. "10 of 200 lines" when capped. */
export function countText(shownCount, total) {
  if (total === 0) return "";

  if (shownCount === total) return `${total} ${total === 1 ? "line" : "lines"}`;

  return `${shownCount} of ${total} lines`;
}

/**
 * One timeline panel. `draw` takes the whole merged timeline and applies the cap; lines that fall
 * outside it are removed from the DOM and the cache.
 */
export function createFeedView({ list, count, empty, cap = 0 }) {
  const lines = new Map();

  function draw(entries) {
    const visible = shown(entries, cap);

    empty.hidden = visible.length > 0;
    count.textContent = countText(visible.length, entries.length);

    visible.forEach((entry, index) => {
      writeLine(lineFor(entry, list, lines), entry, divider(entry, visible[index - 1] ?? null));
    });

    // A Set, because the uncapped archive can hold thousands of lines.
    const kept = new Set(visible.map((entry) => entry.key));
    for (const [key, line] of lines) {
      if (kept.has(key)) continue;

      line.li.remove();
      lines.delete(key);
    }

    orderLines(visible, list, lines);
  }

  return { draw };
}

function lineFor(entry, list, lines) {
  const existing = lines.get(entry.key);
  if (existing) return existing;

  const li = document.createElement("li");
  li.className = "line";
  li.dataset.key = entry.key;

  const match = document.createElement("span");
  match.className = "line__match";
  match.hidden = true;

  const at = document.createElement("span");
  at.className = "line__at";

  const text = document.createElement("span");
  text.className = "line__text";

  li.append(match, at, text);

  const line = { li, match, at, text, said: "" };
  lines.set(entry.key, line);
  list.append(li);

  return line;
}

function writeLine(line, entry, label) {
  const said = eventLine(entry);

  // Only rewrite text that changed (a bite run updates often), so the rest stays still.
  if (said !== line.said) {
    line.text.textContent = said;
    line.said = said;
  }

  line.li.dataset.tone = eventTone(entry.kind);
  line.at.textContent = clock(entry.startedMs);
  line.at.title = entry.at;
  line.match.hidden = label === null;

  if (label !== null) line.match.textContent = label;
}

// Reorders only when needed; append moves existing nodes rather than rebuilding.
function orderLines(visible, list, lines) {
  const wanted = visible.map((entry) => entry.key);
  const current = [...list.children].map((child) => child.dataset.key);

  if (wanted.join(" ") === current.join(" ")) return;

  for (const key of wanted) {
    list.append(lines.get(key).li);
  }
}
