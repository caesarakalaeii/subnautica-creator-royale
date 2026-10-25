import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { ownedPods } from "../static/pods.js";

const pod = (x, z, assigned = 1) => ({ x, y: 0, z, assigned });
const player = (alive = true) => ({ name: "n", alive });

describe("which pods are still somebody's", () => {
  it("drops a derelict, which nobody owns", () => {
    // Assigned zero is a pod left by a previous circle radius: a landmark, not a start.
    const pods = ownedPods([pod(0, 0, 1), pod(100, 100, 0)], [player()]);

    assert.deepEqual(pods, [pod(0, 0, 1)]);
  });

  it("treats a pod with no count on the wire as a derelict", () => {
    const pods = ownedPods([{ x: 0, y: 0, z: 0 }], [player()]);

    assert.deepEqual(pods, []);
  });

  it("cuts down to the living: three eliminations leave two", () => {
    // The roster, not the pod list, is the authority: an eliminated player's pod stays `assigned` on the wire.
    const roster = [player(true), player(true), player(false), player(false), player(false)];
    const pods = ownedPods([pod(0, 0), pod(100, 100), pod(200, 200), pod(300, 300), pod(400, 400)], roster);

    assert.equal(pods.length, 2);
  });

  it("keeps the layout's first seats when it cuts", () => {
    const roster = [player(true)];
    const first = pod(10, 20);
    const second = pod(30, 40);

    assert.deepEqual(ownedPods([first, second], roster), [first]);
  });

  it("owns nothing when nobody is left in the match", () => {
    assert.deepEqual(ownedPods([pod(0, 0), pod(100, 100)], []), []);
    assert.deepEqual(ownedPods([pod(0, 0)], [player(false)]), []);
  });

  it("owns nothing when there are no pods, however many players", () => {
    assert.deepEqual(ownedPods([], [player(), player()]), []);
    assert.deepEqual(ownedPods(null, [player()]), []);
    assert.deepEqual(ownedPods(undefined, undefined), []);
  });

  it("leaves every assigned pod alone while the living outnumber them", () => {
    const pods = [pod(0, 0), pod(100, 100), pod(200, 200, 2)];

    assert.deepEqual(ownedPods(pods, [player(), player(), player()]), pods);
  });
});
