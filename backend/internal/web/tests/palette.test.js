import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { PALETTE, playerColours, seatColour } from "../static/palette.js";

describe("the player palette", () => {
  it("colours by roster position, not by name", () => {
    const roster = [{ name: "Ada" }, { name: "Bea" }, { name: "Cy" }];
    const colours = playerColours(roster);

    assert.deepEqual(colours.get("Ada"), PALETTE[0]);
    assert.deepEqual(colours.get("Bea"), PALETTE[1]);
    assert.deepEqual(colours.get("Cy"), PALETTE[2]);
  });

  it("does not recolour anybody when a player disconnects and rejoins", () => {
    const full = playerColours([{ name: "Ada" }, { name: "Bea" }, { name: "Cy" }]);
    const away = playerColours([{ name: "Ada" }, { name: "Cy" }]);
    const back = playerColours([{ name: "Ada" }, { name: "Bea" }, { name: "Cy" }]);

    assert.deepEqual(back.get("Ada"), full.get("Ada"));
    assert.deepEqual(back.get("Bea"), full.get("Bea"));
    assert.deepEqual(back.get("Cy"), full.get("Cy"));
    assert.notDeepEqual(away.get("Cy"), full.get("Cy"), "the seat moved while Bea was away");
  });

  it("seats the eliminated after the living, so the living keep their half of the order", () => {
    const colours = playerColours([
      { name: "Ada" },
      { name: "Cy" },
      { name: "Bea", alive: false },
    ]);

    assert.deepEqual(colours.get("Ada"), PALETTE[0]);
    assert.deepEqual(colours.get("Cy"), PALETTE[1]);
    assert.deepEqual(colours.get("Bea"), PALETTE[2]);
  });

  it("never hands two players one colour", () => {
    const roster = ["Ada", "Bea", "Cy", "Dov", "Eve"].map((name) => ({ name }));
    const colours = [...playerColours(roster).values()];

    assert.equal(new Set(colours).size, roster.length, "each seat its own colour");
  });

  it("wraps rather than collapsing on a roster longer than the palette", () => {
    const roster = Array.from({ length: PALETTE.length + 2 }, (_, seat) => ({
      name: `p${seat}`,
    }));
    const colours = [...playerColours(roster).values()];

    assert.equal(colours.length, roster.length);
    assert.deepEqual(colours[PALETTE.length], PALETTE[0]);
    assert.deepEqual(colours[PALETTE.length + 1], PALETTE[1]);
  });

  it("answers an empty roster with an empty map", () => {
    assert.equal(playerColours([]).size, 0);
    assert.equal(playerColours(null).size, 0);
    assert.equal(playerColours(undefined).size, 0);
  });

  it("answers the same colours whatever order the roster arrives in", () => {
    // The seat is a property of the roster, so the map a caller hands in is the caster's own reading order, not the order as pushed.
    const pushed = [{ name: "Zoe" }, { name: "Ada" }];
    const ordered = [{ name: "Ada" }, { name: "Zoe" }];

    assert.deepEqual(playerColours(pushed), playerColours(ordered));
  });

  it("offers both inks a renderer needs, so neither map derives its own", () => {
    // The 3D map feeds `hex` to Three.js and the 2D map feeds `css` to a canvas, both derived from one colour.
    for (const entry of PALETTE) {
      assert.match(entry.css, /^#[0-9a-f]{6}$/);
      assert.equal(Number.parseInt(entry.css.slice(1), 16), entry.hex);
    }

    assert.deepEqual(seatColour(0), PALETTE[0]);
    assert.deepEqual(seatColour(PALETTE.length), PALETTE[0]);
  });
});
