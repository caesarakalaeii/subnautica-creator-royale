import assert from "node:assert/strict";
import { describe, it } from "node:test";

import {
  clock,
  countdown,
  highestSequence,
  metres,
  orderPlayers,
  phaseProgress,
  rifleReading,
  sphereText,
  stackCount,
  suitWorn,
  vitalLevel,
  zoneStanding,
} from "../static/view.js";

describe("the match clock", () => {
  it("counts minutes and seconds for a match that has not run an hour", () => {
    assert.equal(clock(0), "0:00");
    assert.equal(clock(9_000), "0:09");
    assert.equal(clock(150_000), "2:30");
    assert.equal(clock(3_599_000), "59:59");
  });

  it("grows an hours field rather than counting to 61 minutes", () => {
    assert.equal(clock(3_600_000), "1:00:00");
    assert.equal(clock(3_723_000), "1:02:03");
  });

  it("reads as a stopped clock when there is no match", () => {
    assert.equal(clock(null), "—");
    assert.equal(clock(undefined), "—");
  });
});

describe("the countdown to the next phase", () => {
  it("is the time the server sent, because the browser does not own the clock", () => {
    assert.equal(countdown(30_000), "0:30");
    assert.equal(countdown(305_000), "5:05");
  });

  it("says nothing is due rather than zero when the server sent no countdown", () => {
    assert.equal(countdown(null), "—");
  });

  it("never counts past zero, because a late push must not read as a negative phase", () => {
    assert.equal(countdown(-500), "0:00");
  });
});

describe("vitals", () => {
  it("colours health, food and water against the hundred they are out of", () => {
    assert.equal(vitalLevel("health", 92), "ok");
    assert.equal(vitalLevel("health", 44), "low");
    assert.equal(vitalLevel("health", 12), "critical");
    assert.equal(vitalLevel("food", 70), "ok");
    assert.equal(vitalLevel("water", 15), "critical");
  });

  it("treats oxygen as the seconds it is, not as a percentage", () => {
    // Oxygen is sent in seconds, not as a percentage.
    assert.equal(vitalLevel("oxygen", 40.5), "ok");
    assert.equal(vitalLevel("oxygen", 22), "low");
    assert.equal(vitalLevel("oxygen", 8), "critical");
  });

  it("calls a dead player's zero critical rather than unknown", () => {
    assert.equal(vitalLevel("health", 0), "critical");
    assert.equal(vitalLevel("oxygen", 0), "critical");
  });
});

describe("the order of the player table", () => {
  const players = [
    { name: "vela", alive: true },
    { name: "Ash", alive: false },
    { name: "Rook", alive: true },
    { name: "bee", alive: false },
  ];

  it("puts the living above the eliminated, and sorts each half by name", () => {
    assert.deepEqual(
      orderPlayers(players).map((player) => player.name),
      ["Rook", "vela", "Ash", "bee"],
    );
  });

  it("ignores case, so a lower-case name is not sorted below every capital", () => {
    const sorted = orderPlayers([{ name: "zoe", alive: true }, { name: "Adam", alive: true }]);
    assert.deepEqual(sorted.map((player) => player.name), ["Adam", "zoe"]);
  });

  it("does not reorder the array it was given", () => {
    const original = [...players];
    orderPlayers(players);
    assert.deepEqual(players, original);
  });

  it("has an answer for a match nobody has joined", () => {
    assert.deepEqual(orderPlayers([]), []);
    assert.deepEqual(orderPlayers(undefined), []);
  });
});

describe("distances", () => {
  it("rounds metres, because a centimetre of depth is noise on a broadcast", () => {
    assert.equal(metres(42.1), "42 m");
    assert.equal(metres(1399.6), "1400 m");
    assert.equal(metres(0), "0 m");
  });

  it("says nothing rather than zero when there is no number", () => {
    assert.equal(metres(null), "—");
  });

  it("writes a sphere the way the config authors one", () => {
    assert.equal(sphereText({ x: 200, y: -15, z: 100, r: 1400 }), "200, -15, 100 · r 1400");
    assert.equal(sphereText(null), "—");
  });
});

describe("where a player stands with the wall", () => {
  it("is inside, and says so without a distance", () => {
    assert.deepEqual(zoneStanding({ insideZone: true, distanceOutside: 0 }), {
      inside: true,
      label: "in",
    });
  });

  it("is outside, and says how far past the wall", () => {
    assert.deepEqual(zoneStanding({ insideZone: false, distanceOutside: 240.25 }), {
      inside: false,
      label: "240 m out",
    });
  });
});

describe("inventory", () => {
  it("counts the items rather than the stacks", () => {
    assert.equal(stackCount([{ item: "Titanium", count: 4 }, { item: "Quartz", count: 2 }]), 6);
  });

  it("is zero for a player carrying nothing", () => {
    assert.equal(stackCount([]), 0);
    assert.equal(stackCount(undefined), 0);
  });
});

describe("the cursor a reconnect resumes from", () => {
  it("is the highest sequence seen, not the last one in the list", () => {
    assert.equal(highestSequence([{ seq: 41 }, { seq: 43 }, { seq: 42 }]), 43);
  });

  it("is zero before anything has happened, which asks for the whole window", () => {
    assert.equal(highestSequence([]), 0);
    assert.equal(highestSequence(undefined), 0);
  });
});

describe("the rule that drains as a phase runs", () => {
  it("is full the first time a phase is seen, because that push is the whole interval", () => {
    assert.deepEqual(phaseProgress(null, { phase: 1, untilNextMs: 300_000 }), {
      phase: 1,
      lengthMs: 300_000,
      fraction: 1,
    });
  });

  it("drains as the countdown does", () => {
    const started = phaseProgress(null, { phase: 1, untilNextMs: 300_000 });

    assert.equal(phaseProgress(started, { phase: 1, untilNextMs: 150_000 }).fraction, 0.5);
    assert.equal(phaseProgress(started, { phase: 1, untilNextMs: 0 }).fraction, 0);
  });

  it("measures the new phase rather than the last one when the wall moves", () => {
    const late = phaseProgress(null, { phase: 1, untilNextMs: 20_000 });
    const next = phaseProgress(late, { phase: 2, untilNextMs: 300_000 });

    assert.equal(next.lengthMs, 300_000);
    assert.equal(next.fraction, 1);
  });

  it("grows its idea of the interval rather than reading past full", () => {
    const midPhase = phaseProgress(null, { phase: 3, untilNextMs: 40_000 });
    const resumed = phaseProgress(midPhase, { phase: 3, untilNextMs: 90_000 });

    assert.equal(resumed.lengthMs, 90_000);
    assert.equal(resumed.fraction, 1);
  });

  it("is empty when nothing is due, and when there is no match at all", () => {
    assert.deepEqual(phaseProgress(null, { phase: 4, untilNextMs: null }), {
      phase: null,
      lengthMs: 0,
      fraction: 0,
    });
    assert.deepEqual(phaseProgress(null, null), { phase: null, lengthMs: 0, fraction: 0 });
  });
});

describe("what a fighter is wearing", () => {
  const worn = [
    { slot: "Body", item: "ReinforcedDiveSuit" },
    { slot: "Head", item: "Rebreather" },
    { slot: "Tank", item: "HighCapacityTank" },
  ];

  it("puts the suit in the column, because that is what changes what a knife does", () => {
    assert.equal(suitWorn(worn).label, "ReinforcedDiveSuit");
  });

  it("keeps the rest of the slots for the cell's title rather than dropping them", () => {
    assert.equal(suitWorn(worn).title, "Body: ReinforcedDiveSuit · Head: Rebreather · Tank: HighCapacityTank");
  });

  it("says nothing rather than empty for a player wearing nothing", () => {
    for (const nothing of [[], null, undefined]) {
      assert.equal(suitWorn(nothing).label, "—");
      assert.equal(suitWorn(nothing).title, "wearing nothing");
    }
  });

  it("shows a body slot filled with something that is not a suit, because the slot is the fact", () => {
    assert.equal(suitWorn([{ slot: "Body", item: "RadiationSuit" }]).label, "RadiationSuit");
  });

  it("is a dash when the slots are filled but the body is not", () => {
    const reading = suitWorn([{ slot: "Head", item: "Rebreather" }]);

    assert.equal(reading.label, "—");
    assert.equal(reading.title, "Head: Rebreather");
  });
});

describe("the stasis rifle a caster is about to call", () => {
  it("is a dash for somebody who has not got one", () => {
    assert.equal(rifleReading({ rifle: false, rifleCharge: 0 }).label, "—");
    assert.equal(rifleReading({ rifle: false, rifleCharge: 0 }).charging, false);
  });

  it("says it is carried when nobody is holding the trigger", () => {
    const reading = rifleReading({ rifle: true, rifleCharge: 0 });

    assert.equal(reading.label, "yes");
    assert.equal(reading.charging, false);
  });

  it("counts the charge up as a percentage, which is the window before the shot", () => {
    assert.equal(rifleReading({ rifle: true, rifleCharge: 0.5 }).label, "50%");
    assert.equal(rifleReading({ rifle: true, rifleCharge: 0.62 }).label, "62%");
    assert.equal(rifleReading({ rifle: true, rifleCharge: 1 }).label, "100%");
    assert.equal(rifleReading({ rifle: true, rifleCharge: 0.5 }).charging, true);
  });

  it("reads a charge on a rifle nobody said was carried, rather than swallowing it", () => {
    assert.equal(rifleReading({ rifle: false, rifleCharge: 0.4 }).label, "40%");
  });

  it("survives a row from a build that never sent the fields", () => {
    assert.equal(rifleReading({}).label, "—");
  });
});
