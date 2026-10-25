import assert from "node:assert/strict";
import { describe, it } from "node:test";

import {
  RECORDS_NONE,
  RECORDS_OK,
  RECORDS_UNREACHABLE,
  adjustmentBody,
  attributionBody,
  bracketRequest,
  caption,
  groupCards,
  knownPlayers,
  readyToSend,
  recordsNote,
  tableRows,
  working,
} from "../static/bracket.js";

/** A folded table as `GET /bracket` sends one. */
function standings(entries, pointsPerKill = 2, matches = 2) {
  return { matches, pointsPerKill, entries };
}

function row(place, name, points, extra = {}) {
  return {
    place,
    name,
    points,
    played: 2,
    wins: 0,
    best: 2,
    kills: 0,
    attributed: 0,
    outlasted: points,
    ...extra,
  };
}

describe("the table", () => {
  it("keeps the place the service sent rather than counting rows", () => {
    const rows = tableRows(standings([row(1, "Ada", 5), row(1, "Bea", 5), row(3, "Cy", 4)]));

    assert.deepEqual(rows.map((each) => each.place), [1, 1, 3]);
  });

  it("marks a place two rows are sharing", () => {
    const rows = tableRows(standings([row(1, "Ada", 5), row(1, "Bea", 5), row(3, "Cy", 4)]));

    assert.deepEqual(rows.map((each) => each.shared), [true, true, false]);
  });

  it("is empty rather than broken before anything is pushed", () => {
    assert.deepEqual(tableRows(null), []);
    assert.deepEqual(tableRows({}), []);
  });

  it("spells out the sum behind the points", () => {
    assert.equal(
      working({ outlasted: 5, kills: 0, attributed: 2 }, 2),
      "5 outlasted + 2 × 2 for kills",
    );
  });

  it("leaves the kills out of the sum when there are none", () => {
    assert.equal(working({ outlasted: 5, kills: 0, attributed: 0 }, 2), "5 outlasted");
  });

  it("shows an operator's adjustment as its own term, either way", () => {
    assert.equal(
      working({ outlasted: 6, kills: 0, attributed: 3, adjusted: 1 }, 2),
      "6 outlasted + 3 × 2 for kills + 1 adjusted",
    );
    assert.equal(working({ outlasted: 6, adjusted: -2 }, 2), "6 outlasted − 2 adjusted");
  });

  it("adds kills off the log and kills said afterwards at the same rate", () => {
    assert.equal(
      working({ outlasted: 1, kills: 1, attributed: 1 }, 2),
      "1 outlasted + 2 × 2 for kills",
    );
  });
});

describe("the caption", () => {
  it("says how far in the tournament is, counting the final", () => {
    const bracket = {
      standings: standings([], 2, 2),
      groups: [{ number: 1 }, { number: 2 }, { number: 3 }, { number: 4 }, { number: 5 }],
      attributions: 3,
    };

    assert.equal(caption(bracket), "after 2 of 6 matches · 3 attributions");
  });

  it("counts what is written rather than assuming five groups", () => {
    const bracket = { standings: standings([], 2, 1), groups: [{ number: 1 }], attributions: 0 };

    assert.equal(caption(bracket), "after 1 of 2 match · 0 attributions");
  });

  it("says nothing has finished rather than drawing a table of nobody", () => {
    assert.equal(caption({ standings: null, groups: [] }), "no match has finished yet");
  });

  it("counts the final's own games as groups once the final is seated from series", () => {
    const bracket = {
      standings: standings([], 2, 3),
      groups: [{ number: 1 }, { number: 2 }, { number: 3 }],
      final: { seats: 1, seated: [], spec: [{ series: "semi", seats: 1 }], from: [] },
      attributions: 0,
    };

    assert.equal(caption(bracket), "after 3 of 3 matches · 0 attributions");
  });
});

describe("the cards", () => {
  it("draws every group as it was written", () => {
    const cards = groupCards({
      groups: [{ number: 2, name: "Group 2", members: ["Vela", "Rook"] }],
      final: { seats: 0, seated: [] },
    });

    assert.equal(cards.length, 1);
    assert.equal(cards[0].label, "2");
    assert.deepEqual(cards[0].members, ["Vela", "Rook"]);
    assert.equal(cards[0].computed, false);
  });

  it("adds the final last, computed and with no number of its own", () => {
    const cards = groupCards({
      groups: [{ number: 1, name: "Group 1", members: ["Vela"] }],
      final: { seats: 5, seated: ["Vela", "Rook"] },
    });

    const final = cards.at(-1);
    assert.equal(final.name, "Final");
    assert.equal(final.computed, true);
    assert.equal(final.label, "—");
    assert.deepEqual(final.members, ["Vela", "Rook"]);
  });

  it("says a final short of its seats is waiting rather than wrong", () => {
    const cards = groupCards({ groups: [], final: { seats: 5, seated: ["Vela"] } });

    assert.match(cards.at(-1).note, /1 of 5 seats/);
  });

  it("says a full final is the top of the table", () => {
    const cards = groupCards({ groups: [], final: { seats: 2, seated: ["Vela", "Rook"] } });

    assert.equal(cards.at(-1).note, "the top 2 of the table");
  });

  it("draws no final at all when nothing seats one", () => {
    assert.deepEqual(groupCards({ groups: [], final: { seats: 0, seated: [] } }), []);
    assert.deepEqual(groupCards({}), []);
  });

  it("marks a group nobody is in, because one is written down before it is filled", () => {
    const cards = groupCards({ groups: [{ number: 6, name: "Group 6", members: [] }] });

    assert.equal(cards[0].empty, true);
  });

  it("says which series a game counts toward", () => {
    const cards = groupCards({ groups: [{ number: 14, name: "Semi 1 · game 1", series: "winners semi 1" }] });

    assert.equal(cards[0].note, "counts toward winners semi 1");
  });

  it("names the series that seated each finalist", () => {
    const cards = groupCards({
      groups: [],
      final: {
        seats: 3,
        seated: ["Rook", "Tess"],
        spec: [{ series: "winners semi 1", seats: 2 }, { series: "losers final", seats: 1 }],
        from: [{ name: "Rook", series: "winners semi 1" }, { name: "Tess", series: "losers final" }],
      },
    });

    const final = cards.at(-1);
    assert.deepEqual(final.members, ["Rook · winners semi 1", "Tess · losers final"]);
    assert.equal(final.note, "2 of 3 seats — top 2 of winners semi 1, top 1 of losers final");
  });
});

describe("what the page says about the database", () => {
  it("says nothing when the store of record answered", () => {
    assert.equal(recordsNote(RECORDS_OK), null);
  });

  it("says a table is missing its kills when the database cannot be reached", () => {
    const note = recordsNote(RECORDS_UNREACHABLE);

    assert.equal(note.tone, "bad");
    assert.match(note.text, /cannot reach/);
  });

  it("says a booth with no database has attributed nothing", () => {
    assert.equal(recordsNote(RECORDS_NONE).tone, "warn");
  });
});

describe("how the page asks for the bracket", () => {
  it("presents the view token it was given", () => {
    const request = bracketRequest("watching");

    assert.equal(request.url, "/bracket");
    assert.equal(request.init.headers.Authorization, "Bearer watching");
  });

  it("never puts the token in the address", () => {
    assert.equal(bracketRequest("watching").url.includes("watching"), false);
  });

  it("reads the bracket of the booth it was served under", () => {
    const request = bracketRequest("watching", { pathname: "/b/mzxw6ytb/bracket.html" });

    assert.equal(request.url, "/b/mzxw6ytb/bracket");
  });

  it("asks without a token when it has none", () => {
    for (const blank of ["", "   ", undefined]) {
      const request = bracketRequest(blank);

      assert.equal(request.url, "/bracket");
      assert.equal("Authorization" in request.init.headers, false);
    }
  });
});

describe("what an operator sends", () => {
  it("turns an empty killer into the null that takes a kill back", () => {
    const body = attributionBody({ group: "1", generation: "1", victim: "Rook", actor: "  ", by: "c" });

    assert.equal(body.actor, null);
  });

  it("trims what was typed, because a trailing space is a different player", () => {
    const body = attributionBody({
      group: "1",
      generation: "1",
      victim: " Rook ",
      actor: " Vela ",
      by: " ops ",
    });

    assert.equal(body.victim, "Rook");
    assert.equal(body.actor, "Vela");
    assert.equal(body.by, "ops");
  });

  it("sends the match as numbers, because the service takes numbers", () => {
    const body = attributionBody({ group: "3", generation: "2", victim: "Rook", by: "c" });

    assert.equal(body.group, 3);
    assert.equal(body.generation, 2);
  });

  it("will not send a form with a hole in it", () => {
    assert.equal(readyToSend({ group: "1", generation: "1", victim: "Rook", by: "c" }), true);
    assert.equal(readyToSend({ group: "", generation: "1", victim: "Rook", by: "c" }), false);
    assert.equal(readyToSend({ group: "1", generation: "1", victim: " ", by: "c" }), false);
    assert.equal(readyToSend({ group: "1", generation: "1", victim: "Rook", by: "" }), false);
    assert.equal(readyToSend({ group: "0", generation: "1", victim: "Rook", by: "c" }), false);
  });

  it("offers every name the bracket knows, once each, in order", () => {
    const names = knownPlayers({
      groups: [{ number: 1, members: ["Vela", "Rook"] }],
      standings: standings([row(1, "Rook", 4), row(2, "Ada", 2)]),
    });

    assert.deepEqual(names, ["Ada", "Rook", "Vela"]);
  });
});

describe("an adjustment", () => {
  it("is sent as numbers and trimmed names, the shape POST /adjustments takes", () => {
    assert.deepEqual(
      adjustmentBody({ group: "5", player: " Rook ", points: "1", reason: " a bug ", by: "ops " }),
      { group: 5, player: "Rook", points: 1, reason: "a bug", by: "ops" },
    );
  });
});

