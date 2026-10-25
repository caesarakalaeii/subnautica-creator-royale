// pods.js decides which pods are still somebody's.
//
// A pod arrives as `{x, y, z, assigned}` with no owner name, and its count does not drop when a
// player dies. So: pods with `assigned === 0` are derelicts and dropped, and the rest are capped
// at the number of living players, keeping the layout's first (best-spot-first) seats.

/**
 * The pods that could still be somebody's. `pods` is `snapshot.pods` in wire order; `players` is
 * the roster, where only the living count. An empty roster means no pods.
 */
export function ownedPods(pods, players) {
  if (!pods) return [];

  const assigned = pods.filter((pod) => (pod.assigned ?? 0) > 0);
  const alive = (players ?? []).filter((player) => player.alive).length;

  return assigned.slice(0, alive);
}
