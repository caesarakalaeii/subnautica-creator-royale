import assert from "node:assert/strict";
import { describe, it } from "node:test";

import {
  WORLD_METRES,
  mapMarkers,
  mapPods,
  mapRing,
  mapSpan,
  toMap,
} from "../static/mapview.js";

const player = (name, x, z, extra = {}) => ({
  name,
  x,
  z,
  y: -40,
  depth: 40,
  alive: true,
  insideZone: true,
  distanceOutside: 0,
  ...extra,
});

describe("the world's own dimensions", () => {
  it("are the game's, not a guess", () => {
    // The world span is shared with the mod and the terrain generator; a second value here would offset every marker.
    assert.equal(WORLD_METRES, 4096);
  });
});

describe("putting a game coordinate on the map", () => {
  it("puts the world's centre at the centre of the image", () => {
    assert.deepEqual(toMap(0, 0), { u: 0.5, v: 0.5, onMap: true });
  });

  it("is north up, the same way round as the minimap on every player's screen", () => {
    // Mirrors the mod's HUD transform: v is `1 - z`, because world +z is north and north is up.
    assert.deepEqual(toMap(-2048, -2048), { u: 0, v: 1, onMap: true });
    assert.deepEqual(toMap(2048, 2048), { u: 1, v: 0, onMap: true });
    assert.deepEqual(toMap(-2048, 2048), { u: 0, v: 0, onMap: true });
  });

  it("puts a known place where it belongs", () => {
    const centre = toMap(200, 100);

    assert.ok(centre.u > 0.5 && centre.u < 0.56, `u was ${centre.u}`);
    assert.ok(centre.v < 0.5 && centre.v > 0.47, `v was ${centre.v}`);
  });

  it("does not clamp somebody past the edge of the world, and says they are past it", () => {
    // Nothing is clamped on purpose: a player beyond ±2048 is outside the wall.
    const east = toMap(9000, 0);

    assert.ok(east.u > 1, `u was ${east.u}`);
    assert.equal(east.onMap, false);

    assert.ok(toMap(-9000, 0).u < 0);
    assert.equal(toMap(0, -9000).onMap, false);
    assert.equal(toMap(0, 9000).onMap, false);
  });
});

describe("a distance on the map", () => {
  it("is the same fraction of the image that it is of the world", () => {
    assert.equal(mapSpan(4096), 1);
    assert.equal(mapSpan(2048), 0.5);
    assert.equal(mapSpan(0), 0);
  });
});

describe("the zone's rings", () => {
  it("are a centre and a radius, both in map units", () => {
    const ring = mapRing({ x: 0, y: -15, z: 0, r: 1024 });

    assert.equal(ring.u, 0.5);
    assert.equal(ring.v, 0.5);
    assert.equal(ring.r, 0.25);
  });

  it("is the sphere's equator, because a flat map is a plan view", () => {
    // Depth is dropped: the ring is the sphere's widest circle.
    const shallow = mapRing({ x: 0, y: -10, z: 0, r: 1024 });
    const deep = mapRing({ x: 0, y: -1200, z: 0, r: 1024 });

    assert.equal(shallow.r, deep.r);
  });

  it("are nothing at all when there is no sphere", () => {
    assert.equal(mapRing(null), null);
    assert.equal(mapRing(undefined), null);
  });
});

describe("the markers", () => {
  it("carry a place and the two things that decide how one is drawn", () => {
    const [marker] = mapMarkers([player("Vela", 0, 0)]);

    assert.equal(marker.name, "Vela");
    assert.equal(marker.u, 0.5);
    assert.equal(marker.alive, true);
    assert.equal(marker.inside, true);
    assert.equal(marker.onMap, true);
  });

  it("say when somebody is in the wall, which is the marker a caster is looking for", () => {
    const [marker] = mapMarkers([player("Quill", 0, 0, { insideZone: false, distanceOutside: 240 })]);

    assert.equal(marker.inside, false);
  });

  it("draw the eliminated under the living, so a corpse never hides somebody playing", () => {
    const markers = mapMarkers([
      player("Ghost", 0, 0, { alive: false }),
      player("Vela", 0, 0),
    ]);

    assert.deepEqual(markers.map((marker) => marker.name), ["Ghost", "Vela"]);
  });

  it("put somebody in the wall on top of everybody else alive", () => {
    const markers = mapMarkers([
      player("Rook", 0, 0),
      player("Quill", 10, 10, { insideZone: false }),
      player("Bram", 0, 0, { alive: false }),
    ]);

    assert.deepEqual(markers.map((marker) => marker.name), ["Bram", "Rook", "Quill"]);
  });

  it("say when somebody has left the map, which the renderer has to draw differently", () => {
    const [marker] = mapMarkers([player("Wanderer", 4000, 0)]);

    assert.equal(marker.onMap, false);
  });

  it("has an answer for a match nobody has joined", () => {
    assert.deepEqual(mapMarkers([]), []);
    assert.deepEqual(mapMarkers(undefined), []);
  });
  it("carry the roster's palette colour when one is given", () => {
    const colours = new Map([["Vela", { hex: 0xf2b544, css: "#f2b544" }]]);
    const [marker] = mapMarkers([player("Vela", 0, 0)], colours);

    assert.deepEqual(marker.colour, colours.get("Vela"));
  });

  it("carry no colour on the one-argument path, which the old callers still use", () => {
    const [marker] = mapMarkers([player("Vela", 0, 0)]);

    assert.equal(marker.colour, undefined);
  });

  it("carry the seat colour on the dead too, and let the renderer decide what a corpse looks like", () => {
    const colours = new Map([["Ghost", { hex: 0x4dc3e8, css: "#4dc3e8" }]]);
    const [marker] = mapMarkers([player("Ghost", 0, 0, { alive: false })], colours);

    assert.deepEqual(marker.colour, colours.get("Ghost"));
  });

  it("leave a player with no seat uncoloured rather than guessing one", () => {
    const colours = new Map([["Ada", { hex: 0xf2b544, css: "#f2b544" }]]);
    const [stray] = mapMarkers([player("Stray", 0, 0)], colours);

    assert.equal(stray.colour, undefined);
  });
});

describe("the escape pods", () => {
  const pod = (x, z, assigned = 1) => ({ x, y: 0, z, assigned });
  const player = (alive = true) => ({ name: "n", alive });

  it("go where the circle put them", () => {
    const [centre, north] = mapPods([pod(0, 0), pod(0, 600)], [player(), player()]);

    assert.deepEqual({ u: centre.u, v: centre.v }, { u: 0.5, v: 0.5 });
    assert.equal(north.u, 0.5);
    assert.ok(north.v < 0.5, "north is up, the same way round as every player's minimap");
  });

  it("keeps the assigned count, so a derelict can be drawn as one", () => {
    const [mine] = mapPods([pod(0, 0, 1), pod(100, 100, 0)], [player()]);

    assert.equal(mine.assigned, 1);
    assert.equal(mine.colour, undefined);
  });

  it("treats a pod with no count as a derelict rather than as undefined", () => {
    const pods = mapPods([{ x: 0, y: 0, z: 0 }], [player()]);

    assert.deepEqual(pods, []);
  });

  it("pairs a pod with the same seat the palette gives that player, whatever order the roster arrived in", () => {
    const colours = new Map([
      ["Ada", { hex: 0xf2b544, css: "#f2b544" }],
      ["Bea", { hex: 0x4dc3e8, css: "#4dc3e8" }],
    ]);
    const roster = [{ name: "Bea", alive: true }, { name: "Ada", alive: true }];
    const [first, second] = mapPods([pod(0, 0), pod(100, 100)], roster, colours);

    assert.deepEqual(first.colour, colours.get("Ada"));
    assert.deepEqual(second.colour, colours.get("Bea"));
  });

  it("carries the owner's palette colour, paired by seat among the living", () => {
    const colours = new Map([
      ["Ada", { hex: 0xf2b544, css: "#f2b544" }],
      ["Bea", { hex: 0x4dc3e8, css: "#4dc3e8" }],
    ]);
    const roster = [
      { name: "Ada", alive: true },
      { name: "Bea", alive: true },
      { name: "Cy", alive: false },
    ];
    const [first, second] = mapPods([pod(0, 0), pod(100, 100)], roster, colours);

    assert.deepEqual(first.colour, colours.get("Ada"));
    assert.deepEqual(second.colour, colours.get("Bea"));
  });

  it("cuts the drawn pods down with the roster", () => {
    const colours = new Map([["Ada", { hex: 0xf2b544, css: "#f2b544" }]]);
    const roster = [{ name: "Ada", alive: true }];
    const pods = mapPods([pod(0, 0), pod(100, 100), pod(200, 200)], roster, colours);

    assert.equal(pods.length, 1);
    assert.deepEqual(pods[0].colour, colours.get("Ada"));
  });

  it("still projects every pod when no roster is given, as the old callers do", () => {
    const [centre] = mapPods([pod(0, 0, 1)]);

    assert.deepEqual({ u: centre.u, v: centre.v }, { u: 0.5, v: 0.5 });
    assert.equal(centre.assigned, 1);
    assert.equal(centre.colour, undefined);
  });

  it("drops a pod outside the world rather than pinning it to the edge", () => {
    // A landmark clamped to the border would be in the wrong place, unlike a player marker.
    assert.deepEqual(mapPods([pod(9000, 9000)], [player()]), []);
  });

  it("is an empty list for a world with no pods in it", () => {
    assert.deepEqual(mapPods([], [player()]), []);
    assert.deepEqual(mapPods(null, [player()]), []);
    assert.deepEqual(mapPods(undefined, undefined), []);
  });
});
