import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { POD_CORNER_RADIUS, drawPodSurface } from "../static/pods-shape.js";

// A 2D context stub: records property writes as calls, in order, since the order of stroke and fill is part of the contract.
function recordingContext() {
  const calls = [];
  const trace = (name) => (...args) => calls.push([name, ...args]);

  const context = {
    calls,
    clearRect: trace("clearRect"),
    beginPath: trace("beginPath"),
    closePath: trace("closePath"),
    moveTo: trace("moveTo"),
    lineTo: trace("lineTo"),
    arcTo: trace("arcTo"),
    fill: trace("fill"),
    stroke: trace("stroke"),
  };

  return new Proxy(context, {
    set(target, key, value) {
      if (key in target) return Reflect.set(target, key, value);

      calls.push([key, value]);

      return true;
    },
    get(target, key) {
      return target[key];
    },
  });
}

describe("the pod marker's shape", () => {
  const size = 64;

  it("rounds only the top two corners, on both the backing and the fill", () => {
    const context = recordingContext();
    drawPodSurface(context, size, "#f2b544");

    const arcs = context.calls.filter(([name]) => name === "arcTo");
    const lines = context.calls.filter(([name]) => name === "lineTo");
    const paths = context.calls.filter(([name]) => name === "beginPath").length;

    assert.equal(paths, 2, "one outline for the backing, one for the fill");
    assert.equal(arcs.length, paths * 2, "exactly two rounded corners per path");

    // The outline is three straight runs and two arcs per path: square bottom, rounded top.
    assert.equal(lines.length, paths * 3);
  });

  it("strokes a dark backing under the fill, in that order", () => {
    const context = recordingContext();
    drawPodSurface(context, size, "#4dc3e8");

    const stroke = context.calls.findIndex(([name]) => name === "stroke");
    const fill = context.calls.findIndex(([name]) => name === "fill");
    assert.ok(stroke !== -1 && fill !== -1, "both are drawn");
    assert.ok(stroke < fill, "the backing goes under the fill");

    const strokes = context.calls.slice(0, stroke).filter(([name]) => name === "strokeStyle");
    assert.deepEqual(strokes.at(-1), ["strokeStyle", "#02070b"]);
  });

  it("is semi-transparent, both stroke and fill", () => {
    const context = recordingContext();
    drawPodSurface(context, size, "#4dc3e8");

    const alphas = context.calls
      .filter(([name]) => name === "stroke" || name === "fill")
      .map(([name]) => {
        const set = context.calls.slice(0, context.calls.indexOf(name ? context.calls.find(([n]) => n === name) : 0));
        void set;

        return context.globalAlpha;
      });
    void alphas;

    const sets = context.calls.filter(([name, value]) => name === "globalAlpha" && value < 1);
    assert.ok(sets.length >= 2, "one alpha for the stroke and one for the fill, both under one");
  });

  it("clears the surface first, so a reused canvas holds no old pod", () => {
    const context = recordingContext();
    drawPodSurface(context, size, "#4dc3e8");

    assert.deepEqual(context.calls[0], ["clearRect", 0, 0, size, size]);
  });
});
