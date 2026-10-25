import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { describe, it } from "node:test";
import { fileURLToPath } from "node:url";

// Pins the wiring between index.html and dashboard.js: both are read as text.
// dashboard.js looks every element up by data-field at load, so a dropped field kills the page silently.

const html = readFileSync(fileURLToPath(new URL("../static/index.html", import.meta.url)), "utf8");
const script = readFileSync(
  fileURLToPath(new URL("../static/dashboard.js", import.meta.url)),
  "utf8",
);

/** The names dashboard.js looks up, mined from its own `el` map rather than kept as a copy here. */
function wanted() {
  return [...script.matchAll(/field\("([A-Za-z]+)"\)/g)].map((match) => match[1]);
}

describe("the dashboard's wiring", () => {
  it("finds every element dashboard.js looks up", () => {
    assert.ok(wanted().length > 25, "the mining broke rather than the page");

    for (const name of wanted()) {
      assert.match(
        html,
        new RegExp(`data-field="${name}"`),
        `index.html has no data-field="${name}" for dashboard.js to find`,
      );
    }
  });

  it("looks each field up once, or the el map is two opinions about one element", () => {
    const seen = new Set();
    for (const name of wanted()) {
      assert.ok(!seen.has(name), `dashboard.js looks up "${name}" twice`);
      seen.add(name);
    }
  });

  it("keeps the link light's text span, which the poll writes into", () => {
    assert.match(html, /class="link__text"/);
  });

  it("ships on the shared Broadcast Deck sheet", () => {
    // There is no view.css: the dashboard uses the base stylesheet.
    assert.match(html, /<link rel="stylesheet" href="\/style\.css" \/>/);
    assert.doesNotMatch(html, /view\.css/);
  });
});
