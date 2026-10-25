import assert from "node:assert/strict";
import { describe, it } from "node:test";

import {
  BITE_WINDOW_MS,
  FEED_LIMIT,
  HIDDEN_KINDS,
  eventLine,
  eventTone,
  divider,
  matchLabel,
  mergeEvents,
} from "../static/events.js";

function event(seq, kind, extra = {}) {
  return {
    seq,
    group: 2,
    generation: 1,
    at: "2026-10-24T12:34:56.789Z",
    elapsedMs: seq * 1000,
    kind,
    player: null,
    actor: null,
    phase: null,
    amount: null,
    detail: null,
    x: null,
    y: null,
    z: null,
    biome: null,
    ...extra,
  };
}

const kinds = (state) => state.entries.map((entry) => entry.kind);

describe("taking a push into the feed", () => {
  it("puts the newest at the top, because that is where a caster looks first", () => {
    const state = mergeEvents(null, [event(1, "MatchStarted"), event(2, "PhaseStarted")]);

    assert.deepEqual(kinds(state), ["PhaseStarted", "MatchStarted"]);
  });

  it("ignores an event it has already taken, because pushes overlap on purpose", () => {
    const first = mergeEvents(null, [event(1, "MatchStarted"), event(2, "PhaseStarted")]);
    const again = mergeEvents(first, [event(2, "PhaseStarted"), event(3, "PlayerJoined")]);

    assert.deepEqual(kinds(again), ["PlayerJoined", "PhaseStarted", "MatchStarted"]);
  });

  it("holds the highest sequence it has seen, which is what a reconnect resumes after", () => {
    const state = mergeEvents(null, [event(41, "PhaseStarted"), event(43, "PlayerJoined")]);

    assert.equal(state.highest, 43);
  });

  it("has an answer for a push with no events in it", () => {
    const state = mergeEvents(null, []);

    assert.deepEqual(state.entries, []);
    assert.equal(state.highest, 0);
  });

  it("never shows position samples, which are most of the log by a wide margin", () => {
    assert.ok(HIDDEN_KINDS.has("PlayerPosition"));

    const state = mergeEvents(null, [
      event(1, "PlayerPosition", { player: "Vela" }),
      event(2, "PlayerEliminated", { player: "Rook" }),
    ]);

    assert.deepEqual(kinds(state), ["PlayerEliminated"]);
  });

  it("still counts a hidden event towards the cursor, or it would be read again for ever", () => {
    const state = mergeEvents(null, [event(9, "PlayerPosition", { player: "Vela" })]);

    assert.equal(state.highest, 9);
  });

  it("keeps a bounded window rather than a whole weekend of DOM", () => {
    const many = Array.from({ length: FEED_LIMIT + 30 }, (_, i) => event(i + 1, "PlayerJoined"));
    const state = mergeEvents(null, many);

    assert.equal(state.entries.length, FEED_LIMIT);
    assert.equal(state.entries[0].seq, FEED_LIMIT + 30);
  });

  it("does not mutate the state it was handed", () => {
    const first = mergeEvents(null, [event(1, "MatchStarted")]);
    const before = first.entries.length;

    mergeEvents(first, [event(2, "PhaseStarted")]);

    assert.equal(first.entries.length, before);
  });
});

describe("a log that starts again", () => {
  it("is read as a restarted game server, not as a batch of duplicates", () => {
    const before = mergeEvents(null, [event(400, "PhaseStarted")]);
    const after = mergeEvents(before, [event(1, "MatchStarted"), event(2, "PlayerJoined")]);

    assert.deepEqual(kinds(after), ["PlayerJoined", "MatchStarted", "PhaseStarted"]);
    assert.equal(after.highest, 2);
  });

  it("keeps the old events distinguishable, so the new log's seq 1 is not the old one", () => {
    const before = mergeEvents(null, [event(1, "MatchStarted"), event(2, "PhaseStarted")]);
    const restarted = mergeEvents(before, [event(1, "MatchStarted")]);

    assert.equal(restarted.entries.length, 3);
    assert.notEqual(restarted.entries[0].key, restarted.entries[2].key);
  });
});

describe("bites, which arrive faster than anyone can read them", () => {
  const bite = (seq, player, amount, elapsedMs) =>
    event(seq, "ZoneBite", { player, amount, elapsedMs });

  it("collapse into one line that counts the ticks and adds up the damage", () => {
    const state = mergeEvents(null, [
      bite(1, "Vela", 3, 1000),
      bite(2, "Vela", 3, 3000),
      bite(3, "Vela", 3, 5000),
    ]);

    assert.equal(state.entries.length, 1);
    assert.equal(state.entries[0].ticks, 3);
    assert.equal(state.entries[0].amount, 9);
  });

  it("collapse per player, so two people in the wall are two lines", () => {
    const state = mergeEvents(null, [
      bite(1, "Vela", 3, 1000),
      bite(2, "Rook", 3, 1000),
      bite(3, "Vela", 3, 3000),
      bite(4, "Rook", 3, 3000),
    ]);

    assert.equal(state.entries.length, 2);
    assert.deepEqual(
      state.entries.map((entry) => [entry.player, entry.ticks]).sort(),
      [["Rook", 2], ["Vela", 2]],
    );
  });

  it("collapse across pushes, not only within one", () => {
    const first = mergeEvents(null, [bite(1, "Vela", 3, 1000)]);
    const second = mergeEvents(first, [bite(2, "Vela", 3, 3000)]);

    assert.equal(second.entries.length, 1);
    assert.equal(second.entries[0].ticks, 2);
  });

  it("start a new line once the wall has left somebody alone for a while", () => {
    const first = mergeEvents(null, [bite(1, "Vela", 3, 1000)]);
    const later = mergeEvents(first, [bite(2, "Vela", 3, 1000 + BITE_WINDOW_MS + 1)]);

    assert.equal(later.entries.length, 2);
    assert.equal(later.entries[0].ticks, 1);
  });

  it("are not collapsed into a run from before a restart", () => {
    const before = mergeEvents(null, [bite(400, "Vela", 3, 1000)]);
    const after = mergeEvents(before, [bite(1, "Vela", 3, 2000)]);

    assert.equal(after.entries.length, 2);
  });

  it("are not collapsed into a run from the match before", () => {
    const before = mergeEvents(null, [bite(40, "Vela", 3, 290_000)]);
    const after = mergeEvents(before, [event(41, "ZoneBite", { player: "Vela", amount: 3, elapsedMs: 5000, group: 3 })]);

    assert.equal(after.entries.length, 2);
    assert.equal(after.entries[1].ticks, 1, "the old match's line is left as it was");
  });

  it("keep the line where it was rather than jumping it to the top on every tick", () => {
    const state = mergeEvents(null, [
      bite(1, "Vela", 3, 1000),
      event(2, "PlayerCaged", { player: "Rook", actor: "Tess" }),
      bite(3, "Vela", 3, 3000),
    ]);

    assert.deepEqual(kinds(state), ["PlayerCaged", "ZoneBite"]);
    assert.equal(state.entries[1].ticks, 2);
  });

  it("are stamped with when the run started, because that is where the line sits", () => {
    const state = mergeEvents(null, [
      bite(1, "Vela", 3, 60_000),
      bite(2, "Vela", 3, 62_000),
      bite(3, "Vela", 3, 64_000),
    ]);

    assert.equal(state.entries[0].startedMs, 60_000);
    assert.equal(state.entries[0].elapsedMs, 64_000, "the window is still measured from the last tick");
  });

  it("survive a bite with no amount on it", () => {
    const state = mergeEvents(null, [
      event(1, "ZoneBite", { player: "Vela", amount: null }),
      event(2, "ZoneBite", { player: "Vela", amount: null, elapsedMs: 3000 }),
    ]);

    assert.equal(state.entries[0].ticks, 2);
    assert.equal(state.entries[0].amount, 0);
  });
});

describe("what a line says", () => {
  const line = (kind, extra) => eventLine(mergeEvents(null, [event(1, kind, extra)]).entries[0]);

  it("names the eliminated player and who did it", () => {
    assert.equal(
      line("PlayerEliminated", { player: "Rook", actor: "Vela", detail: "knife" }),
      "Rook eliminated by Vela — knife",
    );
  });

  it("says what did it when nobody did", () => {
    assert.equal(line("PlayerEliminated", { player: "Rook", detail: "zone" }), "Rook eliminated — zone");
  });

  it("says what the wall took off somebody while they were away", () => {
    assert.equal(
      line("ZoneArrears", { player: "Vela", amount: 12, detail: "away 187s" }),
      "Vela away 187s — the wall took 12",
    );
  });

  it("reads a prawn hit as both names and which arm", () => {
    assert.equal(
      line("PlayerHurt", { player: "Vela", actor: "Mara", amount: 20, detail: "claw" }),
      "Mara clawed Vela — 20",
    );
    assert.equal(
      line("PlayerHurt", { player: "Vela", actor: "Mara", amount: 4, detail: "drill" }),
      "Mara drilled Vela — 4",
    );
  });

  it("reads a blade hit with the blade's own verb rather than a prawn's", () => {
    assert.equal(
      line("PlayerHurt", { player: "Vela", actor: "Mara", amount: 12, detail: "knife" }),
      "Mara knifed Vela — 12",
    );
    assert.equal(
      line("PlayerHurt", { player: "Vela", actor: "Mara", amount: 20, detail: "heatblade" }),
      "Mara burned Vela — 20",
    );
  });

  it("calls a weapon it has never heard of a hit, rather than naming one that was not used", () => {
    assert.equal(
      line("PlayerHurt", { player: "Vela", actor: "Mara", amount: 7, detail: "grapple" }),
      "Mara hit Vela — 7",
    );
  });

  it("still reads a hit with no weapon on it at all", () => {
    assert.equal(
      line("PlayerHurt", { player: "Vela", actor: "Mara", amount: 7 }),
      "Mara hit Vela — 7",
    );
  });

  it("is not fooled by a weapon word that names a property every object already has", () => {
    // `detail` arrives off the wire; an object-literal lookup would answer "toString" with native code.
    assert.equal(
      line("PlayerHurt", { player: "Vela", actor: "Mara", amount: 7, detail: "toString" }),
      "Mara hit Vela — 7",
    );
  });

  it("names the winner, because that is the line the stream cuts to", () => {
    assert.equal(line("MatchEnded", { player: "Vela" }), "Vela wins");
    assert.equal(line("MatchEnded"), "match over, nobody left standing");
  });

  it("says which phase the wall is closing on", () => {
    assert.equal(line("PhaseStarted", { phase: 2 }), "phase 2 — the wall is closing");
    assert.equal(line("PhaseWarned", { phase: 3 }), "phase 3 is coming");
  });

  it("reads a bite as a stay in the wall rather than as one tick of it", () => {
    const state = mergeEvents(null, [
      event(1, "ZoneBite", { player: "Vela", amount: 3 }),
      event(2, "ZoneBite", { player: "Vela", amount: 3, elapsedMs: 3000 }),
    ]);

    assert.equal(eventLine(state.entries[0]), "Vela in the wall — 6 over 2 ticks");
  });

  it("reads a single bite without pluralising a tick", () => {
    assert.equal(line("ZoneBite", { player: "Vela", amount: 3 }), "Vela in the wall — 3 over 1 tick");
  });

  it("reads a ring's charge as a charge, which is the one amount that is not damage", () => {
    // `amount` on RingFired is the trigger charge (0..1); everywhere else it is damage.
    assert.equal(line("RingFired", { player: "Vela", amount: 1 }), "Vela fired a ring — full charge");
    assert.equal(line("RingFired", { player: "Vela", amount: 0.62 }), "Vela fired a ring — 62%");
    assert.equal(line("RingFired", { player: "Vela", amount: 0.04 }), "Vela fired a ring — a tap");
  });

  it("still reads a ring with no charge on it", () => {
    assert.equal(line("RingFired", { player: "Vela" }), "Vela fired a ring");
  });

  it("says who caged whom, and who let go", () => {
    assert.equal(line("PlayerCaged", { player: "Rook", actor: "Vela" }), "Rook caged by Vela");
    assert.equal(line("PlayerCaged", { player: "Rook" }), "Rook caged");
    assert.equal(line("PlayerReleased", { player: "Rook" }), "Rook is out of the ring");
  });

  it("says a kill was credited, and to nobody when it was nobody's", () => {
    assert.equal(line("KillAttributed", { player: "Rook", actor: "Vela" }), "Vela credited with Rook");
    assert.equal(line("KillAttributed", { player: "Rook" }), "Rook's death credited to nobody");
  });

  it("says the quiet things quietly", () => {
    assert.equal(line("PlayerJoined", { player: "Vela" }), "Vela joined");
    assert.equal(line("PlayerLeft", { player: "Vela" }), "Vela left");
    assert.equal(line("ZoneWarned", { player: "Vela" }), "Vela is outside where the wall is going");
    assert.equal(line("MatchPaused"), "paused");
    assert.equal(line("MatchResumed"), "resumed");
    assert.equal(line("MatchStarted"), "match started");
    assert.equal(line("MatchReset"), "match reset");
    assert.equal(line("MatchSkipped"), "a phase was brought forward");
    assert.equal(line("ZoneClosed"), "the zone has closed");
    assert.equal(line("GroupAdvanced", { group: 3 }), "group 3 is up");
  });

  it("shows a kind it has never heard of rather than swallowing it", () => {
    assert.equal(line("SomethingNew", { player: "Vela" }), "SomethingNew — Vela");
    assert.equal(line("SomethingNew"), "SomethingNew");
  });
});

describe("how loud a line is", () => {
  it("puts an elimination and a kill at the top of the shout", () => {
    assert.equal(eventTone("PlayerEliminated"), "out");
    assert.equal(eventTone("KillAttributed"), "out");
    assert.equal(eventTone("MatchEnded"), "out");
  });

  it("separates the wall from the ring from the match's own lifecycle", () => {
    assert.equal(eventTone("ZoneBite"), "zone");
    assert.equal(eventTone("ZoneWarned"), "zone");
    assert.equal(eventTone("ZoneArrears"), "zone");
    assert.equal(eventTone("PhaseStarted"), "phase");
    assert.equal(eventTone("PlayerCaged"), "ring");
    assert.equal(eventTone("RingFired"), "ring");
    assert.equal(eventTone("MatchStarted"), "match");

    assert.equal(eventTone("PlayerHurt"), "fight");
  });

  it("says nothing loudly about somebody arriving", () => {
    assert.equal(eventTone("PlayerJoined"), "quiet");
    assert.equal(eventTone("PlayerLeft"), "quiet");
  });

  it("has a tone for a kind it does not know", () => {
    assert.equal(eventTone("SomethingNew"), "match");
  });
});

describe("which match a line belongs to", () => {
  it("is on the event rather than inferred, because a push can straddle a reset", () => {
    const state = mergeEvents(null, [event(1, "MatchEnded", { group: 2, generation: 1 })]);

    assert.equal(matchLabel(state.entries[0]), "group 2 · run 1");
  });
});

describe("where the feed is broken up", () => {
  const entries = (...events) => mergeEvents(null, events).entries;

  it("names the match above the newest line, so the panel says what is being watched", () => {
    const [newest] = entries(event(1, "MatchStarted"));

    assert.equal(divider(newest, null), "group 2 · run 1");
  });

  it("says nothing between two lines of the same match", () => {
    const [newer, older] = entries(event(1, "MatchStarted"), event(2, "PhaseStarted"));

    assert.equal(divider(older, newer), null);
  });

  it("names the match again when the group or the run changes", () => {
    const [newer, older] = entries(
      event(1, "MatchEnded", { group: 2 }),
      event(2, "MatchStarted", { group: 3 }),
    );

    assert.equal(divider(older, newer), "group 2 · run 1");
  });

  it("says a restart out loud, even when the group did not change", () => {
    const before = mergeEvents(null, [event(1, "MatchStarted"), event(2, "PhaseStarted")]);
    const after = mergeEvents(before, [event(1, "MatchStarted")]);
    const [newest, firstOfTheOldLog, restOfIt] = after.entries;

    assert.equal(divider(newest, null), "group 2 · run 1");
    assert.equal(divider(firstOfTheOldLog, newest), "group 2 · run 1 · before the restart");
    assert.equal(divider(restOfIt, firstOfTheOldLog), null);
  });
});
