import assert from "node:assert/strict";
import { readFileSync, readdirSync } from "node:fs";
import { describe, it } from "node:test";
import { fileURLToPath } from "node:url";

import { rules } from "./stylesheet.js";

// The pages are read as text and their imports resolved by hand: `map3d.js` imports the bare specifier `three`, which an import map resolves in the browser but node does not.
const staticDir = new URL("../static/", import.meta.url);

const source = (name) => readFileSync(new URL(name, staticDir), "utf8");

/** Every module a file imports, as an absolute path (relative specifiers only, static and dynamic). */
function imports(name) {
  const text = source(name);
  const from = new URL(name, staticDir);
  const specifiers = [
    ...text.matchAll(/(?:^|\s)(?:import|export)[\s\S]*?from\s*["']([^"']+)["']/g),
    ...text.matchAll(/\bimport\(\s*["']([^"']+)["']\s*\)/g),
  ].map((match) => match[1]);

  return new Set(
    specifiers
      .filter((specifier) => specifier.startsWith("."))
      .map((specifier) => fileURLToPath(new URL(specifier, from))),
  );
}

/** Every module a page reaches, directly or through another. */
function reaches(entry, seen = new Set()) {
  for (const module of imports(entry)) {
    if (seen.has(module)) continue;

    seen.add(module);
    reaches(module.slice(fileURLToPath(staticDir).length), seen);
  }

  return seen;
}

describe("the map source and the dashboard's map", () => {
  it("resolve the very same renderer, rather than each holding a copy", () => {
    const dashboard = reaches("dashboard.js");
    const panel = reaches("overlay-page.js");

    for (const name of ["map3d.js", "scene.js", "worldmesh.js"]) {
      const module = fileURLToPath(new URL(name, staticDir));

      assert.ok(dashboard.has(module), `the dashboard does not reach ${name}`);
      assert.ok(panel.has(module), `the map source does not reach ${name}`);
    }
  });

  it("take the opening framing from the one place framings are written down", () => {
    const renderer = source("map3d.js");

    assert.match(renderer, /\bframing\(/, "map3d.js does not ask scene.js for a framing");
    assert.ok(
      !/camera\.position\.set\(\s*-?\d/.test(renderer),
      "map3d.js still places the camera at coordinates of its own",
    );
  });

  it("leave the mouse to the flag the caller passed, not to a literal in the renderer", () => {
    const renderer = source("map3d.js");

    assert.match(
      renderer,
      /controls\.enabled = driveable;/,
      "map3d.js decides the mouse itself rather than honouring the flag it was passed",
    );
  });

  it("leave exactly one definition of the renderer in the tree", () => {
    const defining = readdirSync(staticDir)
      .filter((name) => name.endsWith(".js"))
      .filter((name) => /export\s+(?:async\s+)?function\s+createSolidMap\b/.test(source(name)));

    assert.deepEqual(defining, ["map3d.js"]);
  });
});

describe("the map panel's markup", () => {
  const html = source("overlay.html");

  /** The panel's own element, as it appears in the page. */
  const panel = html.match(/<section[^>]*data-field="map"[\s\S]*?<\/section>/)?.[0];

  it("is a canvas and nothing else", () => {
    assert.ok(panel, "the page has no map panel at all");

    const inside = panel.replace(/^<section[^>]*>/, "").replace(/<\/section>$/, "");
    const tags = [...inside.matchAll(/<([a-z][a-z0-9-]*)/gi)].map((match) => match[1].toLowerCase());

    assert.deepEqual(tags, ["canvas"]);
    assert.equal(inside.replace(/<[^>]*>/g, "").trim(), "", "the panel emits text over the shot");
  });

  it("keeps its box when the match blanks it, so the canvas is never sized to nothing", () => {
    // Panels blank with `display: none`, but the map panel must stay laid out: the renderer sizes itself from its box.
    // Asserted as a pair: the display rule lets the map panel through, and something else blanks it.
    const blanking = rules(source("overlay.css"))
      .filter((rule) => rule.selector.includes('[data-showing="no"]'));

    const unsizing = blanking
      .filter((rule) => /display:\s*none/.test(rule.body))
      .flatMap((rule) => rule.selector.split(","))
      .map((selector) => selector.trim())
      .filter((selector) => !selector.endsWith("ol"));

    assert.ok(unsizing.length > 0, "nothing blanks a panel between matches at all");
    for (const selector of unsizing) {
      assert.match(selector, /:not\([^)]*\.map[^)]*\)/, `${selector} unsizes the map's canvas`);
    }

    const blanked = blanking.filter((rule) =>
      rule.selector.replace(/:not\([^)]*\)/g, "").includes(".map"));

    assert.ok(blanked.length > 0, "nothing blanks the map panel between matches");
    for (const rule of blanked) {
      assert.match(rule.body, /opacity:\s*0/, `${rule.selector} does not blank the map`);
      assert.ok(!/display:\s*none/.test(rule.body), `${rule.selector} unsizes the map's canvas`);
    }
  });

  it("draws on a transparent page, like every other source in this booth", () => {
    assert.match(source("overlay.css"), /background:\s*transparent/);
  });

  it("decides nothing itself, so the decisions stay where a test executes them", () => {
    // `overlay-page.js` cannot be imported under `node --test` (DOM lookups and `boot()` at module scope), so it is guarded as text: it must stay thin.
    const page = source("overlay-page.js");

    assert.match(page, /mapState\(snapshot\)/, "the page does not ask overlays.js what to draw");
    assert.match(
      page,
      /createSolidMap\(\s*el\.mapCanvas,\s*mapOptions\(window\.location\)\s*\)/,
      "the page builds the renderer with options of its own",
    );

    assert.ok(
      !/snapshot\.players\s*\?\?/.test(page),
      "the page coerces the roster itself rather than in mapState",
    );
    assert.ok(
      !/driveable:/.test(page),
      "the page names driveable itself rather than in mapOptions",
    );
  });

  it("hands every push to the renderer, not only the one it was holding", () => {
    assert.match(
      source("overlay-page.js"),
      /solid\?\.update\(mapped\)/,
      "the page never hands a push to the map",
    );
  });

  it("replays the push it is already holding once the mesh has finished loading", () => {
    const page = source("overlay-page.js");

    assert.match(page, /mapped = mapState\(snapshot\)/, "the page keeps no state for the build");
    assert.match(
      page,
      /if \(mapped\) solid\.update\(mapped\)/,
      "the page drops the push it was holding while the mesh loaded",
    );
  });

  it("asks for no credential but the view token", () => {
    for (const name of ["overlay-page.js", "overlays.js"]) {
      assert.ok(
        !source(name).includes("lms.operatorToken"),
        `${name} reaches for the operator token`,
      );
    }

    assert.match(source("overlay-page.js"), /overlayToken\(/);
  });
});

describe("the map source following a caster", () => {
  it("holds a camera that arrived while the mesh was still loading", () => {
    const page = source("overlay-page.js");

    assert.match(page, /aimed = camera/, "the page keeps no camera for the build");
    assert.match(page, /if \(aimed\) solid\.aim\(aimed\)/, "the camera it was holding is dropped");
  });

  it("follows only when its address pins a channel, and never 'whoever is driving'", () => {
    assert.match(source("overlay-page.js"), /onAChannel\(window\.location\)/);
  });

  it("cannot be driven by the mouse even while it is following one", () => {
    assert.match(source("overlays.js"), /driveable: false/);
    assert.ok(
      !/driveable:/.test(source("overlay-page.js")),
      "the page names driveable itself rather than in mapOptions",
    );
  });
});

describe("the dashboard driving a source", () => {
  it("publishes off the event the renderer already raises, rather than polling", () => {
    assert.match(source("dashboard.js"), /map\.onMove\(/);
    assert.ok(
      !/setInterval\([^)]*publish/.test(source("dashboard.js")),
      "the dashboard polls the camera instead of listening for it",
    );
  });

  it("puts the publish rate in the one place it is written down", () => {
    const page = source("dashboard.js");

    assert.match(page, /windowMs: PUBLISH_MS/, "the dashboard names a rate of its own");
    assert.match(page, /createPublisher\(/);
  });

  it("does not put the operator token in the socket's address", () => {
    // The write credential goes in the subprotocol, the one header a browser may set on a handshake.
    const page = source("dashboard.js");

    assert.match(page, /cameraSocket\(window\.location, token\(\)\)/,
      "the socket's address is built from something other than the view token");
    assert.match(page, /connect: \(url, protocols\) => new WebSocket\(url, protocols\)/,
      "the dashboard's socket cannot carry a subprotocol, so the token would have to go in the URL");
  });
});
