// Smoke check for the pod sprite path in map3d.js: stubs THREE and pods-shape's canvas and asserts that owned pods
// become sprites at their own position, colour-paired by seat, and that the depth cut never touches pod materials.
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";

const source = readFileSync(fileURLToPath(new URL("../static/map3d.js", import.meta.url)), "utf8");

// The depth cut lives on the terrain's material alone: count the `clippingPlanes` declarations.
const code = source.replace(/\/\*[\s\S]*?\*\//g, "").replace(/\/\/.*/g, "");
const clips = (code.match(/clippingPlanes/g) ?? []).length;
if (clips !== 1 || !/clippingPlanes: \[plane\]/.test(code))
  throw new Error("the cut plane is not terrain-only");
if (!/drawPods\(podGroup, podSprites, state\.pods \?\? \[\], state\.players \?\? \[\]\)/.test(source))
  throw new Error("update() does not draw pods");
// Only the terrain material gets `plane`.
if ((source.match(/\.plane\.constant/g) ?? []).length !== 1)
  throw new Error("the cut plane is no longer terrain-only");
// Upright: sizeAttenuation true means world-sized, false is screen-sized.
if (!/sprite\.material\.sizeAttenuation = true;/.test(source))
  throw new Error("pod sprite lost sizeAttenuation");

// Player pins take the palette: the seat's hex colours the marker, the outside verdict is a hollow dot, the dead keep the dead ink.
if (!/sceneMarkers\(state\.players, playerColours\(state\.players\)\)/.test(source))
  throw new Error("update() does not palettes the markers");
if (!/marker\.colour\?\.hex \?\? ALIVE_MARK/.test(source))
  throw new Error("drawMarkers lost the palette fallback");
if (!/mark\.dot\.material\.wireframe = marker\.alive && !marker\.inside;/.test(source))
  throw new Error("outside verdict is no longer a form");
if (!/marker\.alive \? \(marker\.colour\?\.hex \?\? ALIVE_MARK\) : DEAD_MARK/.test(source))
  throw new Error("dead pins must stay on the dead-ink");

// The pairing rule shared with the 2D map.
const mapview = readFileSync(fileURLToPath(new URL("../static/mapview.js", import.meta.url)), "utf8");
if (!/ownedPods\(pods, players\)/.test(mapview)) throw new Error("mapview lost the owned filter");
if (!/ownedPods\(pods, players\)/.test(source)) throw new Error("map3d lost the owned filter");

console.log("pod map3d wiring: OK");
