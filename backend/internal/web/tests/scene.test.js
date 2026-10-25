import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { describe, it } from "node:test";
import { fileURLToPath } from "node:url";

import { mapRing } from "../static/mapview.js";
import {
  CUT_NORMAL,
  DEFAULT_FRAMING,
  FRAMINGS,
  cutElevation,
  cutKeeps,
  cutLabel,
  cutRange,
  decodeElevation,
  elevationFromPixel,
  framing,
  sampleToWorld,
  sceneMarkers,
  scenePods,
  sceneSphere,
  terrainVertex,
} from "../static/scene.js";

// Containment is not exported by scene.js (the server answers it); this local copy asserts a geometry property.
const insideSphere = (sphere, point) =>
  sphere !== null &&
  Math.hypot(point.x - sphere.x, point.y - sphere.y, point.z - sphere.z) <= sphere.r;

const read = (relative) =>
  JSON.parse(readFileSync(fileURLToPath(new URL(relative, import.meta.url)), "utf8"));

// The encoding is read from the file shipped beside the image, so the decoder cannot drift from the encoder.
const meta = read("../static/map/heightmap.json");

// The mesh's own metadata: the depth cut's range is the mesh's extent.
const mesh = read("../static/map/worldmesh.json");

// The frozen contract file.
const contract = read("../../../../docs/telemetry/example.json");

const player = (name, x, y, z, extra = {}) => ({
  name,
  x,
  y,
  z,
  depth: -y,
  alive: true,
  insideZone: true,
  distanceOutside: 0,
  ...extra,
});

describe("the heightmap's encoding", () => {
  it("is described by the file that ships with the image, not by a constant in here", () => {
    assert.equal(meta.imageIsSouthUp, true);
    assert.equal(meta.hasTerrain, 255);
    assert.equal(meta.sampleMax, 65535);
    assert.equal(meta.worldMetres, 4096);
    assert.equal(meta.size * meta.metresPerPixel, meta.worldMetres);
  });

  it("puts the ends of the range where the generator says they are", () => {
    assert.equal(decodeElevation(0, meta), meta.elevBottom);
    assert.equal(decodeElevation(meta.sampleMax, meta), meta.elevTop);
  });

  it("is monotonic, so deeper samples are never drawn higher", () => {
    let last = -Infinity;
    for (let sample = 0; sample <= meta.sampleMax; sample += 977) {
      const elevation = decodeElevation(sample, meta);
      assert.ok(elevation > last, `sample ${sample} went backwards`);
      last = elevation;
    }
  });

  it("takes terrain from its own channel rather than a reserved elevation", () => {
    assert.equal(elevationFromPixel(0, 0, 0, meta), null);
    assert.equal(elevationFromPixel(255, 255, 0, meta), null);
    assert.equal(elevationFromPixel(0, 0, meta.hasTerrain, meta), meta.elevBottom);
    assert.equal(elevationFromPixel(255, 255, meta.hasTerrain, meta), meta.elevTop);
  });

  it("reads the elevation big-endian across the two channels", () => {
    const sample = (0x12 << 8) | 0x34;
    assert.equal(
      elevationFromPixel(0x12, 0x34, meta.hasTerrain, meta),
      decodeElevation(sample, meta),
    );
  });

  it("keeps the vertical resolution a single channel would have silently thrown away", () => {
    // Elevation is in two channels: a canvas quantises to 8 bits per channel, so a 16-bit greyscale file would lose the 4 m resolution.
    const span = meta.elevTop - meta.elevBottom;
    const step = meta.verticalStepMetres;
    const perSample = span / meta.sampleMax;

    assert.ok(perSample < step, `${perSample.toFixed(3)} m per sample must be finer than ${step} m`);

    const low = Math.round((0 - meta.elevBottom) / span * meta.sampleMax);
    const high = Math.round((step - meta.elevBottom) / span * meta.sampleMax);
    assert.notEqual(decodeElevation(low, meta), decodeElevation(high, meta));
  });
});

describe("a heightmap pixel's place in the world", () => {
  it("is the generator's own relation, so nothing is flipped", () => {
    // The decode walks x and z the same way the generator does, so no south-up flip is needed in 3D.
    assert.deepEqual(sampleToWorld(0, 0, meta), { x: -2048, z: -2048 });

    const centre = meta.size / 2;
    assert.deepEqual(sampleToWorld(centre, centre, meta), { x: 0, z: 0 });
  });

  it("carries the elevation up as the vertex's height", () => {
    assert.deepEqual(terrainVertex(0, 0, meta.elevTop, meta), {
      x: -2048,
      y: meta.elevTop,
      z: -2048,
    });
  });

  it("has no vertex at all where there is no terrain", () => {
    assert.equal(terrainVertex(0, 0, null, meta), null);
  });
});

describe("a zone sphere in three dimensions", () => {
  it("keeps the height the flat map had to throw away", () => {
    assert.deepEqual(sceneSphere({ x: 200, y: -15, z: 100, r: 1400 }), {
      x: 200,
      y: -15,
      z: 100,
      r: 1400,
    });
  });

  it("is nothing when there is no sphere, the same as the flat one", () => {
    assert.equal(sceneSphere(null), null);
    assert.equal(mapRing(null), null);
  });
});

describe("markers in three dimensions", () => {
  it("put a player at their own elevation rather than on a plane", () => {
    const [marker] = sceneMarkers([player("Vela", 100, -300, 50)]);

    assert.equal(marker.x, 100);
    assert.equal(marker.y, -300);
    assert.equal(marker.z, 50);
    assert.equal(marker.name, "Vela");
  });

  it("draw back to front the same way the flat map does", () => {
    const markers = sceneMarkers([
      player("Outside", 0, -10, 0, { insideZone: false, distanceOutside: 40 }),
      player("Dead", 0, -10, 0, { alive: false }),
      player("Alive", 0, -10, 0),
    ]);

    assert.deepEqual(markers.map((m) => m.name), ["Dead", "Alive", "Outside"]);
  });

  it("carry the roster's palette colour on the living when one is given", () => {
    const colours = new Map([["Vela", { hex: 0xf2b544, css: "#f2b544" }]]);
    const [marker] = sceneMarkers([player("Vela", 100, -300, 50)], colours);

    assert.deepEqual(marker.colour, colours.get("Vela"));
  });

  it("carry no colour when no palette is given, as the old callers still call it", () => {
    const [marker] = sceneMarkers([player("Vela", 100, -300, 50)]);

    assert.equal(marker.colour, undefined);
  });

  it("carry the seat colour on the dead too, and leave the corpse's look to the renderer", () => {
    const colours = new Map([["Ghost", { hex: 0x4dc3e8, css: "#4dc3e8" }]]);
    const [marker] = sceneMarkers([player("Ghost", 0, -10, 0, { alive: false })], colours);

    assert.deepEqual(marker.colour, colours.get("Ghost"));
  });

  it("is an empty list rather than a throw when there are no players", () => {
    assert.deepEqual(sceneMarkers(null), []);
    assert.deepEqual(sceneMarkers([]), []);
  });
});

describe("what the 3D sphere fixes about the flat ring", () => {
  it("is stricter than the equator the flat map draws", () => {
    // Directly below a 1400 m sphere centred at -15 m: inside the circle in plan view, outside the sphere 1500 m down.
    const sphere = sceneSphere({ x: 0, y: -15, z: 0, r: 1400 });
    const deep = player("Deep", 0, -1500, 0);

    assert.equal(insideSphere(sphere, deep), false);

    const ring = mapRing({ x: 0, y: -15, z: 0, r: 1400 });
    const flatDistance = Math.hypot(deep.x, deep.z) / 4096;
    assert.ok(flatDistance < ring.r, "the flat map would call this one inside");
  });

  it("draws the contract's own players without inventing a vertical", () => {
    // A shape check against the frozen payload, not a geometry check: the fixture is not a coherent world state.
    const markers = sceneMarkers(contract.players);

    assert.equal(markers.length, contract.players.length);
    for (const marker of markers) {
      const source = contract.players.find((p) => p.name === marker.name);
      assert.equal(marker.y, source.y, `${marker.name} lost its elevation`);
      assert.equal(marker.inside, source.insideZone, `${marker.name} lost the server's verdict`);
    }
  });
});

describe("a named camera framing", () => {
  it("answers to the name an operator types into a browser source", () => {
    for (const name of Object.keys(FRAMINGS)) {
      assert.equal(framing(name).name, name, `${name} did not come back as itself`);
    }
  });

  it("falls back to the documented default rather than to nothing", () => {
    assert.equal(framing("whatever-was-typed").name, DEFAULT_FRAMING);
    assert.equal(framing(null).name, DEFAULT_FRAMING);
    assert.equal(framing(undefined).name, DEFAULT_FRAMING);
    assert.equal(framing("").name, DEFAULT_FRAMING);
  });

  it("puts the camera outside the world it is looking at", () => {
    for (const [name, shot] of Object.entries(FRAMINGS)) {
      const range = Math.hypot(
        shot.position.x - shot.target.x,
        shot.position.y - shot.target.y,
        shot.position.z - shot.target.z,
      );

      assert.ok(range > 2048, `${name} sits inside the crater at ${Math.round(range)} m`);
    }
  });

  it("aims every framing at somewhere in the world", () => {
    for (const [name, shot] of Object.entries(FRAMINGS)) {
      assert.ok(Math.abs(shot.target.x) <= 2048, `${name} looks past the east or west edge`);
      assert.ok(Math.abs(shot.target.z) <= 2048, `${name} looks past the north or south edge`);
    }
  });
});

describe("the depth cut", () => {
  // The mesh holds the world's interior; the cut is a clipping plane on the terrain material, drawing what is below a chosen depth.

  it("takes its range from the mesh's own metadata rather than a number chosen here", () => {
    const range = cutRange(mesh);

    assert.equal(range.min, 0);
    assert.equal(range.step, mesh.cellMetres);
    assert.ok(range.max <= -mesh.minY, "the cut cannot go below the floor of the world");
  });

  it("is a depth, because that is the number every other panel says", () => {
    assert.equal(cutElevation(600, mesh), -600);
    assert.equal(cutElevation(4, mesh), -4);
  });

  it("clips nothing at all when it is off", () => {
    assert.ok(cutElevation(0, mesh) > mesh.maxY, "the plane must start clear of the mesh");
    assert.equal(cutKeeps(mesh.maxY, 0, mesh), true);
    assert.equal(cutKeeps(mesh.minY, 0, mesh), true);
    assert.equal(cutLabel(0), "off");
  });

  it("keeps what is below the cut and drops what is above it", () => {
    assert.equal(cutKeeps(-900, 600, mesh), true, "the floor under the cut must stay");
    assert.equal(cutKeeps(-100, 600, mesh), false, "the reef above it must go");
    assert.equal(cutKeeps(-600, 600, mesh), false, "the cut itself is not kept");
  });

  it("states the plane once, in the form the renderer hands to three.js", () => {
    // `cutKeeps` is `Plane.distanceToPoint(p) > 0` for this normal and constant.
    assert.deepEqual(CUT_NORMAL, { x: 0, y: -1, z: 0 });

    const depth = 480;
    const distance = (y) => CUT_NORMAL.y * y + cutElevation(depth, mesh);

    assert.equal(cutKeeps(-1000, depth, mesh), distance(-1000) > 0);
    assert.equal(cutKeeps(-10, depth, mesh), distance(-10) > 0);
  });

  it("never leaves the caster with an empty map at the far end", () => {
    const range = cutRange(mesh);

    assert.equal(cutKeeps(mesh.minY, range.max, mesh), true);
  });

  it("only ever removes more as it goes deeper", () => {
    const range = cutRange(mesh);
    let last = Infinity;

    for (let depth = range.min; depth <= range.max; depth += range.step) {
      const elevation = cutElevation(depth, mesh);
      assert.ok(elevation < last, `${depth} m did not cut deeper than the notch before it`);
      last = elevation;
    }
  });

  it("steps at the mesh's own resolution and no finer", () => {
    assert.equal(cutRange(mesh).step, mesh.cellMetres);
    assert.equal(cutLabel(mesh.cellMetres), `${mesh.cellMetres} m`);
  });

  it("says how deep the cut is, in metres, and nothing else", () => {
    assert.equal(cutLabel(600), "600 m");
    assert.equal(cutLabel(1916), "1916 m");
  });
});

describe("a pod as a scene marker", () => {
  const pod = (x, z, assigned = 1) => ({ x, y: 0, z, assigned });

  it("sits at the pod's own position, unlifted and unflipped", () => {
    const [marker] = scenePods([pod(-30.5, 12.25)]);

    assert.deepEqual({ x: marker.x, y: marker.y, z: marker.z }, { x: -30.5, y: 0, z: 12.25 });
  });

  it("carries the assigned count through, so the drawing can tell an owned pod from a derelict", () => {
    const [first, derelict] = scenePods([pod(0, 0, 1), pod(100, 100, 0)]);

    assert.equal(first.assigned, 1);
    assert.equal(derelict.assigned, 0);
  });

  it("treats a pod with no count as a derelict", () => {
    const [marker] = scenePods([{ x: 0, y: 0, z: 0 }]);

    assert.equal(marker.assigned, 0);
  });

  it("answers an empty or absent pod list with nothing to draw", () => {
    assert.deepEqual(scenePods([]), []);
    assert.deepEqual(scenePods(null), []);
    assert.deepEqual(scenePods(undefined), []);
  });
});

