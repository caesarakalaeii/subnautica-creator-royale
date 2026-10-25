// map3d.js draws the solid (3D) map with Three.js; the arithmetic lives in `scene.js`. It reads the
// same feed and palette as the flat map.
//
// The terrain image is stored south-up, so `texture.flipY` is off and UVs follow the heightmap's
// own indices; the scene itself is never flipped.

import * as THREE from "three";
import { OrbitControls } from "./vendor/OrbitControls.js";

import { handCamera } from "./camera.js";
import {
  CUT_NORMAL,
  cutElevation,
  cutRange,
  elevationFromPixel,
  framing,
  sceneMarkers,
  sceneSphere,
} from "./scene.js";
import { decodeMesh, drawGroups, planarUVs } from "./worldmesh.js";
import { playerColours } from "./palette.js";
import { ownedPods } from "./pods.js";
import { orderPlayers } from "./view.js";
import { drawPodSurface } from "./pods-shape.js";

// Same colours as the flat map, matching what players see in game.
const NOW_RING = 0xff5b5b;
const NEXT_RING = 0x5fe3f0;
const ALIVE_MARK = 0x7fd88a;
const OUT_MARK = 0xff5b5b;
const DEAD_MARK = 0x54707e;

// Marker radius in metres; deliberately large so pins are visible with the whole world in shot.
const MARKER_RADIUS = 42;

/**
 * Builds the solid map on a canvas. Resolves to a handle, or `null` without WebGL. `shot` names the
 * opening framing (`scene.js`); `driveable` enables mouse controls (false for OBS sources).
 */
export async function createSolidMap(canvas, { shot, driveable = true } = {}) {
  let renderer;
  try {
    renderer = new THREE.WebGLRenderer({ canvas, antialias: true, alpha: true });
  } catch {
    return null;
  }

  renderer.setPixelRatio(window.devicePixelRatio || 1);
  renderer.outputColorSpace = THREE.SRGBColorSpace;
  // Local clipping, so the depth cut affects only the terrain material, not markers or zone walls.
  renderer.localClippingEnabled = true;

  // The mesh draws the terrain (it can represent caves); the heightmap gives cheap seabed lookups
  // for the marker stalks, avoiding raycasts against the mesh.
  const meta = await (await fetch("/map/heightmap.json")).json();
  const samples = await readPixels("/map/heightmap.png", meta.size);

  const scene = new THREE.Scene();
  const camera = new THREE.PerspectiveCamera(48, 1, 10, 40000);
  // Named framings are defined in scene.js.
  const opening = framing(shot);
  camera.position.set(opening.position.x, opening.position.y, opening.position.z);

  const controls = new OrbitControls(camera, canvas);
  controls.target.set(opening.target.x, opening.target.y, opening.target.z);
  controls.enabled = driveable;
  controls.enableDamping = true;
  controls.maxDistance = 18000;
  controls.minDistance = 200;
  controls.update();

  scene.add(new THREE.HemisphereLight(0x93d7ff, 0x0b1a2b, 1.6));
  const sun = new THREE.DirectionalLight(0xffffff, 1.1);
  sun.position.set(-1500, 3000, 1200);
  scene.add(sun);

  const terrain = await terrainMesh();
  scene.add(terrain.mesh);

  const now = zoneSphere(NOW_RING);
  const next = zoneSphere(NEXT_RING);
  scene.add(now, next);

  const markers = new THREE.Group();
  scene.add(markers);
  const marks = new Map();

  let state = { zone: null, players: [], pods: [] };
  // Pod sprites, rebuilt when the pod set changes; unaffected by the depth cut.
  const podGroup = new THREE.Group();
  scene.add(podGroup);
  const podSprites = new Map();

  // Render on demand only, to avoid redrawing an unchanged scene every frame.
  let dirty = true;
  // Camera-move listeners (used by the camera publisher).
  const moved = [];
  const announceMove = () => {
    for (const listener of moved) listener();
  };
  controls.addEventListener("change", () => {
    dirty = true;
    announceMove();
  });

  function frame() {
    // update() advances damping and reports movement, so a fling renders until it settles.
    const easing = controls.update();
    if (dirty || easing) {
      renderer.render(scene, camera);
      dirty = false;
    }
    handle.raf = requestAnimationFrame(frame);
  }

  // Reads and sets the shot; logic in `camera.js`.
  const hand = handCamera(camera, controls, (depth) => {
    terrain.plane.constant = cutElevation(depth, terrain.meta);
    dirty = true;
  });

  const handle = {
    raf: 0,

    /** Resizes the renderer to the canvas's box. */
    resize() {
      const box = canvas.getBoundingClientRect();
      if (box.width === 0 || box.height === 0) return;

      renderer.setSize(box.width, box.height, false);
      camera.aspect = box.width / box.height;
      camera.updateProjectionMatrix();
      dirty = true;
    },

    /** The depth cut's range, from the mesh metadata. */
    cutRange: terrain.cutRange,

    /** Cuts away terrain above `depth` metres to reveal caves. Zero is off. */
    cut(depth) {
      hand.cutTo(depth);

      // The slider raises no controls `change`, so announce it for followers.
      announceMove();
    },

    /** Where the camera is, what it is aimed at, and how deep the cut is. */
    look: () => hand.look(),

    /** Applies a shot (camera and depth cut), as a following source does for each message. */
    aim(shot) {
      hand.aim(shot);
      dirty = true;
    },

    /** Calls `listener` on every camera move or cut change, including ones made via this handle. */
    onMove(listener) {
      moved.push(listener);
    },

    /** A new push. Only the spheres, the markers and the pods move; the terrain is built once. */
    update(next_state) {
      state = next_state;
      place(now, sceneSphere(state.zone?.now));
      place(next, sceneSphere(state.zone?.next));
      drawMarkers(markers, marks, sceneMarkers(state.players, playerColours(state.players)), samples, meta);
      drawPods(podGroup, podSprites, state.pods ?? [], state.players ?? []);
      dirty = true;
    },

    dispose() {
      controls.dispose();
      renderer.dispose();
    },
  };

  handle.resize();
  handle.raf = requestAnimationFrame(frame);

  return handle;
}

/**
 * The heightmap's RGBA bytes. `colorSpaceConversion: "none"` is required: the channels hold
 * elevation data, not colour.
 */
async function readPixels(url, size) {
  const blob = await (await fetch(url)).blob();
  const bitmap = await createImageBitmap(blob, { colorSpaceConversion: "none" });
  const surface = new OffscreenCanvas(size, size);
  const context = surface.getContext("2d", { willReadFrequently: true });

  context.drawImage(bitmap, 0, 0);

  return context.getImageData(0, 0, size, size).data;
}

/**
 * Loads the terrain mesh (an isosurface from `backend/cmd/worldmesh`, so caves are included),
 * gunzipped with `DecompressionStream`. Returns the mesh, its clipping plane and the metadata.
 */
async function terrainMesh() {
  const meta = await (await fetch("/map/worldmesh.json")).json();

  const response = await fetch("/map/worldmesh.bin.gz");
  const inflated = meta.gzipped
    ? new Response(response.body.pipeThrough(new DecompressionStream("gzip")))
    : response;
  const mesh = decodeMesh(await inflated.arrayBuffer(), meta);

  const geometry = new THREE.BufferGeometry();
  geometry.setAttribute("position", new THREE.BufferAttribute(mesh.positions, 3));
  geometry.setAttribute("uv", new THREE.BufferAttribute(planarUVs(mesh.positions, meta.worldMetres), 2));
  geometry.setIndex(new THREE.BufferAttribute(mesh.indices, 1));
  // Normals are computed at load rather than shipped, to keep the download smaller.
  geometry.computeVertexNormals();

  // Split into several draws: Firefox caps indices per draw (`webgl.max-vert-ids-per-draw`).
  const groups = drawGroups(mesh.indices.length);
  for (const group of groups) {
    geometry.addGroup(group.start, group.count, 0);
  }

  const texture = await new THREE.TextureLoader().loadAsync("/map/terrain.png");
  // Pairs with `planarUVs`: the image's row 0 is z = -2048, which is uv.y = 0 with no flip.
  texture.flipY = false;
  texture.colorSpace = THREE.SRGBColorSpace;

  // DoubleSide so cave interiors (back faces) and cut surfaces render. The clipping plane exists
  // from the start, parked above the mesh, because adding one later recompiles the shader.
  const plane = new THREE.Plane(
    new THREE.Vector3(CUT_NORMAL.x, CUT_NORMAL.y, CUT_NORMAL.z),
    cutElevation(0, meta),
  );
  const material = new THREE.MeshStandardMaterial({
    map: texture,
    roughness: 0.95,
    side: THREE.DoubleSide,
    clippingPlanes: [plane],
  });

  // Three.js only issues groups as separate draws when the material is an array. Every entry shares
  // one material instance, so one plane clips the whole terrain.
  const object = new THREE.Mesh(geometry, groups.map(() => material));

  return { mesh: object, plane, meta, cutRange: cutRange(meta) };
}

/** One zone wall, drawn as the full sphere the server tests players against. */
function zoneSphere(colour) {
  const sphere = new THREE.Mesh(
    new THREE.SphereGeometry(1, 48, 32),
    new THREE.MeshBasicMaterial({
      color: colour,
      transparent: true,
      opacity: 0.05,
      side: THREE.DoubleSide,
      depthWrite: false,
    }),
  );
  // A sparse wireframe, so the wall reads as a shape without hiding the players.
  const wire = new THREE.LineSegments(
    new THREE.WireframeGeometry(new THREE.SphereGeometry(1, 16, 10)),
    new THREE.LineBasicMaterial({ color: colour, transparent: true, opacity: 0.16 }),
  );
  sphere.add(wire);
  sphere.visible = false;

  return sphere;
}

function place(mesh, sphere) {
  mesh.visible = sphere !== null;
  if (sphere === null) return;

  mesh.position.set(sphere.x, sphere.y, sphere.z);
  mesh.scale.setScalar(sphere.r);
}

/**
 * A marker per player: a dot at their depth, a stalk to the seabed below (to make depth readable),
 * and their name. The colour is the player's palette entry (grey when dead); outside the zone the
 * dot is drawn as wireframe.
 */
function drawMarkers(group, marks, markers, samples, meta) {
  const wanted = new Set(markers.map((marker) => marker.name));
  for (const [name, mark] of marks) {
    if (!wanted.has(name)) {
      group.remove(mark.root);
      marks.delete(name);
    }
  }

  for (const marker of markers) {
    let mark = marks.get(marker.name);
    if (!mark) {
      mark = buildMark(marker.name);
      marks.set(marker.name, mark);
      group.add(mark.root);
    }

    const colour = marker.alive ? (marker.colour?.hex ?? ALIVE_MARK) : DEAD_MARK;
    mark.dot.material.color.setHex(colour);
    mark.stalk.material.color.setHex(colour);
    mark.dot.material.opacity = marker.alive ? 0.95 : 0.5;
    mark.dot.material.wireframe = marker.alive && !marker.inside;
    mark.root.position.set(marker.x, marker.y, marker.z);

    const seabed = seabedUnder(marker.x, marker.z, samples, meta);
    const drop = seabed === null ? 0 : Math.max(0, marker.y - seabed);
    mark.stalk.scale.y = drop;
    mark.stalk.position.y = -drop / 2;
    mark.stalk.visible = drop > MARKER_RADIUS;

    // Offset in metres while the label is screen-sized, so it is tightest when zoomed out.
    mark.label.position.y = MARKER_RADIUS * 7;
  }
}

/**
 * A sprite per owned escape pod at its real position, unaffected by the depth cut. Pods carry no
 * owner name, so the k-th owned pod takes the k-th living player's colour, as in `mapview.js`.
 */
function drawPods(group, sprites, pods, players) {
  const owned = ownedPods(pods, players);
  const colours = playerColours(players);
  const alive = orderPlayers(players).filter((player) => player.alive);

  // Re-paired every push; a pod whose colour changed is rebuilt.
  const wanted = new Map(
    owned.map((pod, seat) => [`${pod.x},${pod.y},${pod.z}`, colours.get(alive[seat % alive.length]?.name)]),
  );

  for (const [key, sprite] of sprites) {
    if (!wanted.has(key) || sprite.userData.ink !== wanted.get(key)?.css) {
      group.remove(sprite);
      sprite.material.map.dispose();
      sprite.material.dispose();
      sprites.delete(key);
    }
  }

  for (const pod of owned) {
    const key = `${pod.x},${pod.y},${pod.z}`;
    if (sprites.has(key)) continue;

    const colour = wanted.get(key);
    const sprite = podSprite(colour);
    sprite.userData.ink = colour?.css;
    sprite.position.set(pod.x, pod.y, pod.z);
    sprites.set(key, sprite);
    group.add(sprite);
  }
}

/** The pod's surface (`pods-shape.js`) as a sprite, sized in metres like a player marker. */
function podSprite(colour) {
  const size = 128;
  const surface = document.createElement("canvas");
  surface.width = size;
  surface.height = size;
  drawPodSurface(surface.getContext("2d"), size, colour?.css ?? "#63848f");

  const texture = new THREE.CanvasTexture(surface);
  texture.colorSpace = THREE.SRGBColorSpace;

  const sprite = new THREE.Sprite(
    new THREE.SpriteMaterial({ map: texture, transparent: true, depthTest: false }),
  );
  // World-sized, unlike name labels.
  sprite.material.sizeAttenuation = true;
  sprite.scale.set(MARKER_RADIUS * 3, MARKER_RADIUS * 3, 1);
  // With the player dots: above the terrain, under the names.
  sprite.renderOrder = 2;

  return sprite;
}

function buildMark(name) {
  const root = new THREE.Group();

  // Unlit and drawn over the terrain, so players behind rock stay visible.
  const dot = new THREE.Mesh(
    new THREE.SphereGeometry(MARKER_RADIUS, 20, 14),
    new THREE.MeshBasicMaterial({ transparent: true, opacity: 0.95, depthTest: false }),
  );
  dot.renderOrder = 2;
  const stalk = new THREE.Mesh(
    new THREE.CylinderGeometry(2.5, 2.5, 1, 6),
    new THREE.MeshBasicMaterial({ transparent: true, opacity: 0.5 }),
  );
  const label = nameSprite(name);

  root.add(dot, stalk, label);

  return { root, dot, stalk, label };
}

/** The player's name, drawn once into a texture and kept at a constant size on screen. */
function nameSprite(name) {
  const scale = 4;
  const font = 26;
  const surface = document.createElement("canvas");
  const context = surface.getContext("2d");

  context.font = `${font * scale}px ui-monospace, Menlo, Consolas, monospace`;
  surface.width = Math.ceil(context.measureText(name).width) + 8 * scale;
  surface.height = font * scale * 1.6;

  // Setting the size resets the context, so the font goes on again.
  context.font = `${font * scale}px ui-monospace, Menlo, Consolas, monospace`;
  context.fillStyle = "rgba(6, 16, 26, 0.72)";
  context.fillRect(0, 0, surface.width, surface.height);
  context.fillStyle = "#dcecf5";
  context.textBaseline = "middle";
  context.fillText(name, 4 * scale, surface.height / 2);

  const texture = new THREE.CanvasTexture(surface);
  texture.colorSpace = THREE.SRGBColorSpace;

  const sprite = new THREE.Sprite(
    new THREE.SpriteMaterial({ map: texture, transparent: true, depthTest: false }),
  );
  // Constant on-screen size, so names stay readable when zoomed out.
  sprite.material.sizeAttenuation = false;
  sprite.scale.set((surface.width / surface.height) * 0.027, 0.027, 1);
  // Drawn above the dots.
  sprite.renderOrder = 3;

  return sprite;
}

/** The seabed under a world column, read out of the heightmap already in memory. */
function seabedUnder(x, z, samples, meta) {
  const half = meta.worldMetres / 2;
  const i = Math.round((x + half) / meta.metresPerPixel);
  const j = Math.round((z + half) / meta.metresPerPixel);
  if (i < 0 || j < 0 || i >= meta.size || j >= meta.size) return null;

  const at = (j * meta.size + i) * 4;

  return elevationFromPixel(samples[at], samples[at + 1], samples[at + 2], meta);
}
