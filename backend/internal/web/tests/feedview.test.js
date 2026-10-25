import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { describe, it } from "node:test";
import { fileURLToPath } from "node:url";

import { FEED_SHOWN, WINDOW_LIMIT, countText, shown } from "../static/feedview.js";

/** Lines as the timeline holds them, newest first. */
function entries(count) {
  return Array.from({ length: count }, (_, index) => ({ key: `k${index}` }));
}

describe("the glance cap", () => {
  it("keeps the newest ten, in the order they arrived", () => {
    assert.deepEqual(
      shown(entries(25), FEED_SHOWN).map((entry) => entry.key),
      entries(25).slice(0, FEED_SHOWN).map((entry) => entry.key),
    );
  });

  it("shows everything when there are not ten yet", () => {
    assert.equal(shown(entries(7), FEED_SHOWN).length, 7);
  });

  it("caps nothing on the archive page", () => {
    assert.equal(shown(entries(25), 0).length, 25);
  });

  it("is ten, so the panel stays a glance beside the roster", () => {
    assert.equal(FEED_SHOWN, 10);
  });
});

describe("the archive's bound", () => {
  it("keeps the booth's whole window", () => {
    assert.equal(WINDOW_LIMIT, 5000);
  });
});

describe("the count line", () => {
  it("admits the cap when it bit", () => {
    assert.equal(countText(10, 214), "10 of 214 lines");
  });

  it("reads plainly when it did not", () => {
    assert.equal(countText(7, 7), "7 lines");
    assert.equal(countText(1, 1), "1 line");
    assert.equal(countText(0, 0), "");
  });
});

describe("the shipped pages", () => {
  const index = readFileSync(
    fileURLToPath(new URL("../static/index.html", import.meta.url)),
    "utf8",
  );
  const events = readFileSync(
    fileURLToPath(new URL("../static/events.html", import.meta.url)),
    "utf8",
  );
  const page = readFileSync(
    fileURLToPath(new URL("../static/events-page.js", import.meta.url)),
    "utf8",
  );

  it("offers the archive from the dashboard's own feed head", () => {
    assert.match(index, /href="events\.html"/);
  });

  it("is a first-class page with the source offer and its own script", () => {
    assert.match(events, /<!-- source-offer -->/);
    assert.match(events, /<script type="module" src="\/events-page\.js"><\/script>/);
  });

  it("merges at the booth's whole window rather than the glance limit", () => {
    assert.match(page, /mergeEvents\(timeline, [^,]+, WINDOW_LIMIT\)/);
  });
});
