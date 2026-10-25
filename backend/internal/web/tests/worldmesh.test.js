import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { describe, it } from "node:test";
import { fileURLToPath } from "node:url";

import {
  MAGIC,
  MAX_INDICES_PER_DRAW,
  decodeMesh,
  drawGroups,
  meshLayout,
  planarUVs,
} from "../static/worldmesh.js";

const read = (relative) =>
  JSON.parse(readFileSync(fileURLToPath(new URL(relative, import.meta.url)), "utf8"));

// The metadata that ships beside the mesh.
const meta = read("../static/map/worldmesh.json");

/** A whole file, built the way cmd/worldmesh builds one, so the decoder is tested end to end. */
function buildMesh(vertices, triangles, scale) {
  const indexCount = triangles * 3;
  const layout = meshLayout(vertices, indexCount);
  const buffer = new ArrayBuffer(layout.totalBytes);
  const bytes = new Uint8Array(buffer);

  for (let i = 0; i < MAGIC.length; i += 1) bytes[i] = MAGIC.charCodeAt(i);

  const header = new DataView(buffer);
  header.setUint32(MAGIC.length, vertices, true);
  header.setUint32(MAGIC.length + 4, indexCount, true);

  const quantised = new Int16Array(buffer, layout.positionsAt, vertices * 3);
  for (let i = 0; i < quantised.length; i += 1) quantised[i] = (i % 97) * scale;

  const indices = new Uint32Array(buffer, layout.indicesAt, indexCount);
  for (let i = 0; i < indexCount; i += 1) indices[i] = i % vertices;

  return { buffer, quantised, indices };
}

describe("the mesh file's layout", () => {
  it("pads the indices onto a four-byte boundary", () => {
    // An odd vertex count leaves the positions two bytes short of alignment; indices are read as a Uint32Array view, which needs a multiple of four.
    for (const vertices of [1, 3, 1252323]) {
      const layout = meshLayout(vertices, 3);
      assert.equal(layout.indicesAt % 4, 0, `${vertices} vertices left the indices misaligned`);
    }
  });

  it("needs no padding when the vertex count is even", () => {
    const layout = meshLayout(2, 3);
    assert.equal(layout.indicesAt, MAGIC.length + 8 + 2 * 3 * 2);
  });

  it("is exactly as long as its two arrays plus the header", () => {
    const layout = meshLayout(4, 6);
    assert.equal(layout.totalBytes, layout.indicesAt + 6 * 4);
  });
});

describe("reading a mesh", () => {
  it("scales the quantised positions back to metres", () => {
    const built = buildMesh(5, 2, 1);
    const mesh = decodeMesh(built.buffer, { vertices: 5, triangles: 2, positionScale: 8 });

    assert.equal(mesh.positions.length, 15);
    for (let i = 0; i < mesh.positions.length; i += 1) {
      assert.equal(mesh.positions[i], built.quantised[i] / 8);
    }
  });

  it("reads the indices without copying them", () => {
    const built = buildMesh(5, 2, 1);
    const mesh = decodeMesh(built.buffer, { vertices: 5, triangles: 2, positionScale: 8 });

    assert.deepEqual([...mesh.indices], [...built.indices]);
  });

  it("refuses a file that is not one of ours", () => {
    const built = buildMesh(2, 1, 1);
    new Uint8Array(built.buffer)[0] = "X".charCodeAt(0);

    assert.throws(
      () => decodeMesh(built.buffer, { vertices: 2, triangles: 1, positionScale: 8 }),
      /this page reads/,
    );
  });

  it("refuses metadata that disagrees with the file's own header", () => {
    // The two files must come from the same generator run, or the world would be truncated.
    const built = buildMesh(4, 2, 1);

    assert.throws(
      () => decodeMesh(built.buffer, { vertices: 9, triangles: 2, positionScale: 8 }),
      /its metadata says 9/,
    );
    assert.throws(
      () => decodeMesh(built.buffer, { vertices: 4, triangles: 7, positionScale: 8 }),
      /7 triangles/,
    );
  });

  it("refuses a file that has been cut short", () => {
    const built = buildMesh(4, 2, 1);
    const truncated = built.buffer.slice(0, built.buffer.byteLength - 4);

    assert.throws(
      () => decodeMesh(truncated, { vertices: 4, triangles: 2, positionScale: 8 }),
      /want \d+/,
    );
  });
});

describe("the terrain image over the mesh", () => {
  it("maps the north and south edges the way the image is stored", () => {
    // The file's row 0 is z = -2048 and `texture.flipY = false` makes uv.y = 0 row 0, so no flip is needed.
    const positions = new Float32Array([0, 0, -2048, 0, 0, 2048, -2048, 0, 0]);
    const uvs = planarUVs(positions, 4096);

    assert.equal(uvs[1], 0, "z = -2048 must sample the image's first row");
    assert.equal(uvs[3], 1, "z = +2048 must sample the image's last row");
    assert.equal(uvs[4], 0, "x = -2048 must sample the image's left edge");
  });

  it("puts the world's centre at the middle of the image", () => {
    const uvs = planarUVs(new Float32Array([0, -100, 0]), 4096);

    assert.deepEqual([...uvs], [0.5, 0.5]);
  });
});

describe("the mesh that actually ships", () => {
  it("is described by metadata the decoder can use", () => {
    assert.equal(meta.magic, MAGIC);
    assert.equal(meta.gzipped, true);
    assert.equal(meta.worldMetres, 4096);
    assert.ok(meta.positionScale > 0, "a scale of zero would divide every position to infinity");
    assert.ok(meta.vertices > 0 && meta.triangles > 0);
  });

  it("quantises finer than the grid it came from, so the scale is not the limit", () => {
    assert.ok(
      1 / meta.positionScale < meta.cellMetres,
      `${1 / meta.positionScale} m per step is not finer than the ${meta.cellMetres} m grid`,
    );
  });

  it("fits the world it claims to describe", () => {
    const extreme = Math.max(meta.worldMetres / 2, Math.abs(meta.minY), Math.abs(meta.maxY));
    assert.ok(extreme * meta.positionScale <= 32767,
      `${extreme} m at scale ${meta.positionScale} overflows int16`);
  });
});

describe("splitting the mesh into draws a browser will accept", () => {
  it("keeps every draw inside Firefox's limit for the mesh that actually ships", () => {
    // 10,018,998 triangles is 30,056,994 indices, over Firefox's webgl.max-vert-ids-per-draw of 30,000,000.
    const indexCount = meta.triangles * 3;
    const groups = drawGroups(indexCount);

    for (const group of groups) {
      assert.ok(group.count <= 30_000_000, `a draw of ${group.count} is past Firefox's limit`);
      assert.ok(group.count <= MAX_INDICES_PER_DRAW);
    }
    assert.ok(groups.length > 1, `${indexCount} indices should need more than one draw`);
  });

  it("tiles the whole range exactly, because a gap is a hole in the seabed", () => {
    const groups = drawGroups(30_056_994);

    let covered = 0;
    for (const [at, group] of groups.entries()) {
      assert.equal(group.start, covered, `group ${at} does not start where the last one ended`);
      covered += group.count;
    }
    assert.equal(covered, 30_056_994);
  });

  it("never ends a draw mid-triangle", () => {
    for (const indexCount of [3, 30, 3_000_003, 30_056_994]) {
      for (const group of drawGroups(indexCount)) {
        assert.equal(group.count % 3, 0, `a group of ${group.count} splits a triangle`);
        assert.equal(group.start % 3, 0, `a group starting at ${group.start} splits a triangle`);
      }
    }
  });

  it("uses a single draw when the mesh is small enough", () => {
    assert.deepEqual(drawGroups(999), [{ start: 0, count: 999 }]);
  });

  it("rounds a limit that is not a multiple of three down to one", () => {
    for (const group of drawGroups(30, 10)) {
      assert.equal(group.count % 3, 0);
      assert.ok(group.count <= 10);
    }
  });

  it("refuses an index count that is not whole triangles", () => {
    assert.throws(() => drawGroups(31), /not a whole number of triangles/);
  });
});
