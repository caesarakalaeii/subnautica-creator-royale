import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { describe, it } from "node:test";
import { fileURLToPath } from "node:url";

import { rules } from "./stylesheet.js";

// The stylesheet is read as text, not imported: these bytes are what OBS renders.
const stylesheet = readFileSync(
  fileURLToPath(new URL("../static/overlay.css", import.meta.url)),
  "utf8",
);

const parsed = rules(stylesheet);

describe("the overlay stylesheet", () => {
  it("strikes a name through only where the row says the player is out", () => {
    const striking = parsed.filter((rule) => rule.body.includes("line-through"));

    // Non-empty first, so deleting the strike outright cannot pass by vacuous truth.
    assert.ok(striking.length > 0, "no rule strikes an eliminated name through at all");

    for (const rule of striking) {
      for (const one of rule.selector.split(",")) {
        assert.ok(
          one.includes('[data-alive="false"]'),
          `${one.trim()} strikes a name through outside a dead row`,
        );
      }
    }
  });
});

describe("the overlay stylesheet's leader", () => {
  it("draws the name of whoever leads differently from everybody else's", () => {
    // An unscoped `.board__name { color: var(--kelp) }` is silently overridden by the base rule, so assert the rule exists and is scoped.
    const leading = parsed.filter(
      (rule) =>
        rule.selector.includes('[data-lead="true"]') &&
        rule.selector.includes(".board__name") &&
        rule.body.includes("color:"),
    );

    assert.equal(leading.length, 1, "the leader's name has no colour rule of its own");
  });
});
