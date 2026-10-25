import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { describe, it } from "node:test";
import { fileURLToPath } from "node:url";

import { rules } from "./stylesheet.js";

// Pins the two overflow rules a narrow window reads: the stacked document must not scroll sideways, and the side pane scrolls its own content.
// The stylesheet is read as text and parsed into rules.

const stylesheet = readFileSync(
  fileURLToPath(new URL("../static/style.css", import.meta.url)),
  "utf8",
);

const parsed = rules(stylesheet);

/** The body of the last `@media (max-width: ...)` block, the stacked layout. */
const stacked = parsed.filter((rule) => rule.selector.startsWith("@media")).at(-1);

describe("the stacked layout's roster", () => {
  it("gives the roster pane a scroll of its own on both axes", () => {
    const scrolls = parsed.filter(
      (rule) => rule.selector.includes(".pane--roster") && /overflow(-x)?:\s*auto/.test(rule.body),
    );
    assert.ok(scrolls.length > 0, "no rule gives the roster pane its own scroll");

    for (const rule of scrolls) {
      if (rule.selector.trim().startsWith("@media")) {
        assert.ok(
          rule === stacked,
          ".pane--roster gets its own scroll outside the stacked layout",
        );
      }
    }
  });
});

describe("the side pane's scroll", () => {
  it("is the pane's own, not the document's", () => {
    const panes = parsed.filter(
      (rule) => /^\.pane--side\b/.test(rule.selector.trim()) && rule.body.includes("overflow"),
    );
    assert.ok(panes.length > 0, "no .pane--side rule mentions overflow at all");

    for (const rule of panes) {
      assert.match(
        rule.body,
        /overflow(-y)?:\s*(auto|hidden)/,
        `.pane--side says "${rule.body.match(/overflow[^;}]*/)?.[0]}" — neither scrolls nor hides`,
      );
      assert.ok(
        rule.selector.includes("@media") || !/overflow:\s*hidden/.test(rule.body),
        "the base .pane--side hides its overflow again",
      );
    }
  });
});

describe("a sheet page's main", () => {
  // The sheet pages are documents whose <main> is a `.sheet`; the dashboard's two-column grid on `main` must not apply to them.
  const sheet = rules(readFileSync(fileURLToPath(new URL("../static/bracket.css", import.meta.url)), "utf8"))
    .find((rule) => rule.selector === ".sheet");

  it("is a block, not the dashboard's grid", () => {
    assert.match(sheet.body, /display:\s*block/);
  });

  it("grows with its content rather than being squeezed to the window", () => {
    assert.match(sheet.body, /flex:\s*none/);
  });
});
