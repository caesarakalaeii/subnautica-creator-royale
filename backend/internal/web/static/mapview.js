// mapview.js projects game coordinates onto the booth's map, shared by the 2D and 3D renderers.
//
// It ports `Lms.Core.Zones.WorldMap` (`NorthUp`, `Fraction`); keep the two in step. The world
// span matches Subnautica's own start-point map, which maps a normalised point to `4096 * n - 2048`.
import { ownedPods } from "./pods.js";
import { orderPlayers } from "./view.js";

/** The playable world, metres across. Square in x and z. */
export const WORLD_METRES = 4096;

// Half the span, which is where the world's origin sits in the image.
const HALF = WORLD_METRES / 2;

/**
 * A game coordinate as an image fraction, north up (`WorldMap.NorthUp`): `u` from x, `v` is one
 * minus the z fraction. Not clamped: off the map is a real state, reported by `onMap`. Depth is
 * dropped (plan view).
 */
export function toMap(x, z) {
  const u = (x + HALF) / WORLD_METRES;
  const v = 1 - (z + HALF) / WORLD_METRES;

  return { u, v, onMap: u >= 0 && u <= 1 && v >= 0 && v <= 1 };
}

/**
 * A distance in metres as an image fraction (`WorldMap.Fraction`), the same on both axes, so the
 * map panel must be square for circles to stay round.
 */
export function mapSpan(metres) {
  return metres / WORLD_METRES;
}

/** A zone sphere as a circle at its equator (its widest), or null when there is no sphere. */
export function mapRing(sphere) {
  if (!sphere) return null;

  const { u, v } = toMap(sphere.x, sphere.z);

  return { u, v, r: mapSpan(sphere.r) };
}

/**
 * A marker per owned escape pod (see `ownedPods`). Off-map pods are dropped, not clamped. `colour`
 * pairs the k-th owned pod with the k-th living player's palette entry.
 */
export function mapPods(pods, players, colours) {
  if (!pods) return [];

  // Without a roster, every pod is projected.
  const owned = players ? ownedPods(pods, players) : pods;
  // Seats in palette order (`orderPlayers`), not wire order, so a pod matches its player's pin.
  const alive = orderPlayers(players).filter((player) => player.alive);
  return owned
    .map((pod, seat) => {
      const place = toMap(pod.x, pod.z);

      return {
        u: place.u,
        v: place.v,
        onMap: place.onMap,
        assigned: pod.assigned ?? 0,
        colour: seat < alive.length && colours ? colours.get(alive[seat].name) : undefined,
      };
    })
    .filter((pod) => pod.onMap);
}

/**
 * A marker per player, back to front: the eliminated, the living, then anyone outside the wall.
 * `colour` is the player's palette entry from `colours`.
 */
export function mapMarkers(players, colours) {
  if (!players) return [];

  return players
    .map((player) => {
      const place = toMap(player.x, player.z);

      return {
        name: player.name,
        u: place.u,
        v: place.v,
        onMap: place.onMap,
        alive: player.alive,
        inside: player.insideZone,
        depth: player.depth,
        colour: colours ? colours.get(player.name) : undefined,
      };
    })
    .sort((left, right) => layer(left) - layer(right));
}

function layer(marker) {
  if (!marker.alive) return 0;

  return marker.inside ? 1 : 2;
}
