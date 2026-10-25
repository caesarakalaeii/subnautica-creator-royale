// scene.js projects game coordinates into the 3D map's scene; it is `mapview.js`'s counterpart,
// and `map3d.js` draws what it returns.
//
// World coordinates are scene coordinates: no scaling, re-centring or flip, so the map cannot end
// up mirrored. Zone containment is not computed here; it comes from the server as `insideZone`.

/**
 * A heightmap sample as an elevation in metres. `meta` is the JSON `scripts/make-heightmap` writes
 * beside the image.
 */
export function decodeElevation(sample, meta) {
  const span = meta.elevTop - meta.elevBottom;

  return meta.elevBottom + (sample / meta.sampleMax) * span;
}

/**
 * One heightmap pixel as an elevation in metres, or `null` where the column has no terrain.
 * Elevation is big-endian across `r` and `g` (a canvas quantises each channel to 8 bits, so a
 * 16-bit greyscale image would lose precision); `b` flags whether there is terrain at all.
 */
export function elevationFromPixel(r, g, b, meta) {
  if (b !== meta.hasTerrain) return null;

  return decodeElevation((r << 8) | g, meta);
}

/**
 * A heightmap pixel's world coordinate: the inverse of `make-heightmap`'s
 * `x = i * metres - span / 2`, `z = j * metres - span / 2`.
 */
export function sampleToWorld(i, j, meta) {
  const half = meta.worldMetres / 2;

  return { x: i * meta.metresPerPixel - half, z: j * meta.metresPerPixel - half };
}

/**
 * One heightmap pixel as a terrain vertex, or `null` where there is no terrain, so the mesh ends at
 * the crater wall rather than sloping into the void.
 */
export function terrainVertex(i, j, elevation, meta) {
  if (elevation === null) return null;

  const { x, z } = sampleToWorld(i, j, meta);

  return { x, y: elevation, z };
}

/**
 * A zone sphere for the scene, or `null`. Unlike the flat map's `mapRing`, the height is kept, so
 * the shape matches what the server tests players against.
 */
export function sceneSphere(sphere) {
  if (!sphere) return null;

  return { x: sphere.x, y: sphere.y, z: sphere.z, r: sphere.r };
}

/**
 * A marker per player, in the same back-to-front order as `mapMarkers` (which decides which label
 * wins on overlap). `inside` is the server's verdict; `colour` is the player's palette entry.
 */
export function sceneMarkers(players, colours) {
  if (!players) return [];

  return players
    .map((player) => ({
      name: player.name,
      x: player.x,
      y: player.y,
      z: player.z,
      depth: player.depth,
      alive: player.alive,
      inside: player.insideZone,
      distanceOutside: player.distanceOutside,
      colour: colours ? colours.get(player.name) : undefined,
    }))
    .sort((left, right) => layer(left) - layer(right));
}

/**
 * A marker per escape pod at its own position. Pods are not affected by the depth cut. `assigned`
 * is passed through so the renderer can tell an owned pod from a derelict.
 */
export function scenePods(pods) {
  if (!pods) return [];

  return pods.map((pod) => ({
    x: pod.x,
    y: pod.y,
    z: pod.z,
    assigned: pod.assigned ?? 0,
  }));
}

function layer(marker) {
  if (!marker.alive) return 0;

  return marker.inside ? 1 : 2;
}

/**
 * Named camera framings, selectable from a browser source's address. `crater` is the default: the
 * whole world in shot, from the south looking north so it matches the north-up flat map. All
 * framings sit outside the first zone sphere so the wall does not fill the view.
 */
export const FRAMINGS = {
  crater: {
    position: { x: 0, y: 3600, z: 4600 },
    target: { x: 0, y: -500, z: 0 },
  },
  // Nearly straight down. Offset 400 m south because `lookAt` has no defined roll when the view
  // direction is parallel to the up vector.
  top: {
    position: { x: 0, y: 6200, z: 400 },
    target: { x: 0, y: -500, z: 0 },
  },
  // Low from the west, to show the crater's relief.
  rim: {
    position: { x: -5200, y: 900, z: 2600 },
    target: { x: 0, y: -700, z: 0 },
  },
};

/** The framing used when an address names none, or an unknown one. */
export const DEFAULT_FRAMING = "crater";

/** A named framing with its name, falling back to the default for an unknown name. */
export function framing(name) {
  const shot = Object.hasOwn(FRAMINGS, name ?? "") ? name : DEFAULT_FRAMING;

  return { name: shot, ...FRAMINGS[shot] };
}

/**
 * The depth cut's plane normal. Three.js keeps points where `normal · p + constant > 0`, so -y
 * with the cut's elevation as constant keeps everything below the cut. Shared with `cutKeeps`.
 */
export const CUT_NORMAL = { x: 0, y: -1, z: 0 };

/**
 * The depth cut's slider range, in metres of depth (positive down). `meta` is `cmd/worldmesh`'s
 * JSON. The maximum stops one cell above the world's floor so the cut never removes the seabed.
 */
export function cutRange(meta) {
  return { min: 0, max: -meta.minY - meta.cellMetres, step: meta.cellMetres };
}

/**
 * The cut's elevation for a depth. Depth 0 (off) puts the plane above the mesh rather than removing
 * it, because adding a clipping plane later recompiles the terrain shader and causes a hitch.
 */
export function cutElevation(depth, meta) {
  if (depth <= 0) return meta.maxY + meta.cellMetres;

  return -depth;
}

/** Whether a point at elevation `y` survives the cut. This is three.js's own half-space test. */
export function cutKeeps(y, depth, meta) {
  return CUT_NORMAL.y * y + cutElevation(depth, meta) > 0;
}

/** What the readout beside the slider says. */
export function cutLabel(depth) {
  return depth <= 0 ? "off" : `${depth} m`;
}
