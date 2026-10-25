import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { describe, it } from "node:test";
import { fileURLToPath } from "node:url";

import { RECORDS_OK, followControls, followVerbFor } from "../static/console.js";

/** A console document as `GET /console` sends one. */
function view(over = {}) {
  return {
    report: {
      v: 1,
      at: "2026-10-24T12:00:00.000Z",
      acceptsCommands: true,
      ran: 0,
      connected: [],
      verbs: [],
      answers: [],
      ...(over.report ?? {}),
    },
    reportedAt: "2026-10-24T12:00:00.000Z",
    commands: [],
    intent: {
      group: 1,
      ghosts: [],
      contestants: [],
      unbound: [],
      pending: [],
      reconciling: true,
      why: "",
    },
    people: [],
    groups: [],
    overrides: [],
    records: RECORDS_OK,
    ...over,
  };
}

describe("the follow picker's ghosts", () => {
  it("is one control per camera the mod believes is a ghost", () => {
    const drawn = followControls(
      view({
        report: {
          connected: [
            { name: "Ada", inMatch: true },
            { name: "Caster", inMatch: false },
            { name: "Producer", inMatch: false },
          ],
        },
      }),
    );

    assert.deepEqual(
      drawn.map((one) => one.ghost),
      ["Caster", "Producer"],
    );
  });

  it("carries the verb and the field the panel sends it with", () => {
    const [one] = followControls(
      view({ report: { connected: [{ name: "Caster", inMatch: false }] } }),
    );

    assert.equal(one.verb, "followOne");
    assert.equal(one.kind, "subject");
    assert.equal(one.placeholder, "player");
  });

  it("follows what the mod believes, not what the booth intends", () => {
    const drawn = followControls(
      view({
        report: { connected: [{ name: "Caster", inMatch: false }] },
        intent: {
          group: 1,
          ghosts: [],
          contestants: ["Caster"],
          unbound: [],
          pending: ["unghost Caster"],
          reconciling: true,
          why: "",
        },
      }),
    );

    assert.deepEqual(
      drawn.map((one) => one.ghost),
      ["Caster"],
    );
  });

  it("never keeps a ghost the channel has stopped reporting", () => {
    const drawn = followControls(
      view({
        report: {
          connected: [{ name: "Caster", inMatch: false }, { name: "Producer", inMatch: true }],
        },
      }),
    );
    const before = drawn.length;

    drawn.forEach(() => {});
    const gone = followControls(
      view({
        report: { connected: [{ name: "Producer", inMatch: true }] },
      }),
    );

    assert.equal(before, 1);
    assert.deepEqual(gone, []);
  });
});

describe("the verb a follow picker sends", () => {
  const control = { ghost: "Caster", verb: "followOne", kind: "subject", placeholder: "player" };

  it("joins the ghost and the target the way the mod parses them", () => {
    assert.equal(followVerbFor(control, " Vela "), "followOne Caster Vela");
  });

  it("squeezes whitespace the way the mod does", () => {
    assert.equal(
      followVerbFor(control, "  Ada   Plays "),
      "followOne Caster Ada Plays",
    );
  });

  it("lets the camera go when the target field is left empty", () => {
    assert.equal(followVerbFor(control, ""), "followOne Caster");
    assert.equal(followVerbFor(control, "   "), "followOne Caster");
  });

  it("fires off the ghost's own name, so two pickers cannot send for each other", () => {
    const [left, right] = followControls(
      view({
        report: {
          connected: [
            { name: "Caster", inMatch: false },
            { name: "Producer", inMatch: false },
          ],
        },
      }),
    );

    assert.equal(followVerbFor(left, "Vela"), "followOne Caster Vela");
    assert.equal(followVerbFor(right, "Vela"), "followOne Producer Vela");
  });
});

describe("the page the panel ships on", () => {
  const html = readFileSync(
    fileURLToPath(new URL("../static/console.html", import.meta.url)),
    "utf8",
  );
  const page = readFileSync(
    fileURLToPath(new URL("../static/console-page.js", import.meta.url)),
    "utf8",
  );

  it("puts the panel between the verbs and the roster", () => {
    const verbs = html.indexOf('aria-labelledby="verbs-heading"');
    const follows = html.indexOf('aria-labelledby="follows-heading"');
    const roster = html.indexOf('aria-labelledby="roster-heading"');

    assert.ok(verbs !== -1 && follows !== -1 && roster !== -1);
    assert.ok(verbs < follows && follows < roster);
  });

  it("marks the panel with the fields the script fills", () => {
    assert.match(html, /data-field="follows"/);
    assert.match(html, /data-field="followsEmpty"/);
    assert.match(page, /field\("follows"\)/);
    assert.match(page, /field\("followsEmpty"\)/);
  });

  it("draws the panel from the poll, not a timer of its own", () => {
    assert.match(page, /drawFollows\(view\)/);
  });
});
