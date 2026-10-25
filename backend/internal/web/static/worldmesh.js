// worldmesh.js decodes the terrain mesh written by `backend/cmd/worldmesh`; map3d.js turns it into
// geometry. The layout is described by `worldmesh.json`, written beside the mesh by the generator.

/** The file's first eight bytes; the version is part of the magic. */
export const MAGIC = "LMSMESH1";

const HEADER_BYTES = MAGIC.length + 8;

/**
 * Byte offsets of each section, given the header counts. Indices are padded to a four-byte
 * boundary because they are read as a `Uint32Array` view, and positions (6 bytes per vertex)
 * leave them misaligned for odd vertex counts.
 */
export function meshLayout(vertexCount, indexCount) {
  const positionsAt = HEADER_BYTES;
  const positionBytes = vertexCount * 3 * 2;
  const indicesAt = positionsAt + positionBytes + ((4 - (positionBytes % 4)) % 4);

  return { positionsAt, indicesAt, totalBytes: indicesAt + indexCount * 4 };
}

/**
 * Decodes the mesh into positions and indices. `meta` is the generator's JSON and is checked
 * against the file header, so a stale copy of either file is caught.
 */
export function decodeMesh(buffer, meta) {
  if (buffer.byteLength < HEADER_BYTES) {
    throw new Error(`the mesh is ${buffer.byteLength} bytes, too short to have a header`);
  }

  const magic = String.fromCharCode(...new Uint8Array(buffer, 0, MAGIC.length));
  if (magic !== MAGIC) {
    throw new Error(`the mesh says "${magic}", this page reads "${MAGIC}"`);
  }

  const header = new DataView(buffer);
  const vertexCount = header.getUint32(MAGIC.length, true);
  const indexCount = header.getUint32(MAGIC.length + 4, true);

  if (vertexCount !== meta.vertices) {
    throw new Error(`the mesh has ${vertexCount} vertices, its metadata says ${meta.vertices}`);
  }
  if (indexCount !== meta.triangles * 3) {
    throw new Error(`the mesh has ${indexCount} indices, its metadata says ${meta.triangles} triangles`);
  }

  const layout = meshLayout(vertexCount, indexCount);
  if (buffer.byteLength < layout.totalBytes) {
    throw new Error(`the mesh is ${buffer.byteLength} bytes, want ${layout.totalBytes}`);
  }

  // Positions are copied because they are scaled; indices stay a zero-copy view.
  const quantised = new Int16Array(buffer, layout.positionsAt, vertexCount * 3);
  const positions = new Float32Array(quantised.length);
  for (let i = 0; i < quantised.length; i += 1) {
    positions[i] = quantised[i] / meta.positionScale;
  }

  return { positions, indices: new Uint32Array(buffer, layout.indicesAt, indexCount) };
}

/**
 * Terrain-image UVs per vertex, projected straight down (caves take the seabed colour above them).
 * Pairs with `texture.flipY = false`: `make-minimap` writes row 0 at z = -2048, so
 * `uv.y = (z + half) / span`.
 */
export function planarUVs(positions, worldMetres) {
  const half = worldMetres / 2;
  const uvs = new Float32Array((positions.length / 3) * 2);

  for (let v = 0; v < positions.length / 3; v += 1) {
    uvs[v * 2] = (positions[v * 3] + half) / worldMetres;
    uvs[v * 2 + 1] = (positions[v * 3 + 2] + half) / worldMetres;
  }

  return uvs;
}

/**
 * The most indices per draw call. Firefox refuses more than 30,000,000 per draw
 * (`webgl.max-vert-ids-per-draw`), which the terrain exceeds; this leaves ample headroom.
 */
export const MAX_INDICES_PER_DRAW = 3_000_000;

/**
 * Splits the index range into draw groups that tile it exactly, each a multiple of three so no
 * triangle is cut.
 */
export function drawGroups(indexCount, maxPerDraw = MAX_INDICES_PER_DRAW) {
  if (indexCount % 3 !== 0) {
    throw new Error(`${indexCount} indices is not a whole number of triangles`);
  }

  const perDraw = Math.max(3, Math.floor(maxPerDraw / 3) * 3);
  const groups = [];
  for (let start = 0; start < indexCount; start += perDraw) {
    groups.push({ start, count: Math.min(perDraw, indexCount - start) });
  }

  return groups;
}
