import assert from "node:assert/strict";
import { describe, it } from "node:test";

import {
  POINTS_PER_KILL,
  POINTS_PER_PLAYER_OUTLASTED,
  BOARD_KINDS,
  PANELS,
  TOAST_LIMIT,
  TOAST_MS,
  exitNote,
  keepEvents,
  mapOptions,
  mapState,
  matchBoard,
  overlayPanel,
  overlayToken,
  overlayURL,
  panelShowing,
  toastQueue,
  tournamentTable,
} from "../static/overlays.js";

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

const player = (name, alive = true) => ({ name, alive });
const out = (seq, name, extra) => event(seq, "PlayerEliminated", { player: name, ...extra });

describe("the token an overlay presents", () => {
  it("comes from the address, because nobody types into an OBS browser source", () => {
    assert.equal(overlayToken({ search: "?token=view-secret" }, {}), "view-secret");
  });

  it("is not taken out of the address the way the dashboard's is", () => {
    // The dashboard strips the token from the address; a browser source keeps it, because the URL is its configuration.
    assert.equal(overlayToken({ search: "?token=a&panel=board" }, {}), "a");
  });

  it("falls back to what the dashboard already stored, for a local window", () => {
    assert.equal(overlayToken({ search: "" }, { "lms.viewToken": "stored" }), "stored");
  });

  it("falls back to the booth's own key under a booth, never to the tournament's", () => {
    const location = { pathname: "/b/ada/overlay.html", search: "" };
    const storage = { "lms.viewToken": "the-tournaments", "lms.viewToken:ada": "adas" };

    assert.equal(overlayToken(location, storage), "adas");
  });

  it("is empty rather than undefined when there is none", () => {
    assert.equal(overlayToken({ search: "" }, {}), "");
  });
});

describe("the panel an overlay draws", () => {
  it("is the one named in the address, because that is a browser source's only configuration", () => {
    for (const name of Object.keys(PANELS)) {
      assert.equal(overlayPanel({ search: `?panel=${name}&token=view-secret` }), name);
    }
  });

  it("is the setup card when the address names no panel at all", () => {
    assert.equal(overlayPanel({ search: "?token=view-secret" }), "setup");
  });

  it("is the setup card for a panel name this page does not have", () => {
    assert.equal(overlayPanel({ search: "?panel=maps" }), "setup");
    assert.equal(overlayPanel({ search: "?panel=__proto__" }), "setup");
    assert.equal(overlayPanel({ search: "?panel=toString" }), "setup");
  });

  it("offers the map as one of the sources an operator can set up", () => {
    assert.ok("map" in PANELS, "the map is not one of the panels");
    for (const [name, size] of Object.entries(PANELS)) {
      assert.match(size, /^\d+ × \d+$/, `${name} does not quote a source size`);
    }
  });
});

describe("whether a panel has anything to show", () => {
  const running = { match: { alive: 4 }, standings: null };
  const between = { match: null, standings: { matches: 1, entries: [] } };

  it("draws nothing at all when no match is running", () => {
    for (const panel of ["status", "toasts", "board", "map"]) {
      assert.equal(panelShowing(panel, between), false, `${panel} drew without a match`);
      assert.equal(panelShowing(panel, running), true, `${panel} stayed blank during a match`);
    }
  });

  it("shows the tournament table for the gap between matches, which is what it is for", () => {
    assert.equal(panelShowing("table", between), true);
    assert.equal(panelShowing("table", running), false);
  });

  it("treats a panel it does not have as nothing to show, rather than throwing", () => {
    assert.equal(panelShowing("maps", running), false);
    assert.equal(panelShowing("setup", running), false);
  });
});

describe("what the map source draws", () => {
  it("takes the zone, the roster and the pods out of the push it is handed", () => {
    const zone = { now: { x: 10, z: 20, radius: 300 }, next: null };
    const players = [player("Vela"), player("Rook", false)];
    const pods = [{ x: 0, y: 0, z: 0, assigned: 1 }];

    assert.deepEqual(mapState({ match: { alive: 2 }, zone, players, pods }), {
      zone,
      players,
      pods,
    });
  });

  it("draws an empty world rather than undefined when a push carries none of it", () => {
    assert.deepEqual(mapState({ match: { alive: 8 } }), { zone: null, players: [], pods: [] });
    assert.deepEqual(mapState({ zone: null, players: null }), {
      zone: null,
      players: [],
      pods: [],
    });
  });
});

describe("how the map source configures its renderer", () => {
  it("takes the framing from the address, so one page serves every shot", () => {
    assert.equal(mapOptions({ search: "?panel=map&camera=rim" }).shot, "rim");
    assert.equal(mapOptions({ search: "?panel=map&camera=top" }).shot, "top");
  });

  it("names no framing when the address does not, leaving the default to scene.js", () => {
    assert.equal(mapOptions({ search: "?panel=map" }).shot, null);
  });

  it("is never driveable, whatever the address says", () => {
    for (const search of ["?panel=map", "?panel=map&camera=rim", "?panel=map&driveable=true"]) {
      assert.equal(mapOptions({ search }).driveable, false, `${search} left the source driveable`);
    }
  });
});

describe("elimination toasts", () => {
  it("say nothing at all about what happened before the page opened", () => {
    const primed = toastQueue(null, [out(1, "Rook"), out(2, "Tess")], 0);

    assert.deepEqual(primed.toasts, []);
    assert.equal(primed.highest, 2);
    assert.equal(primed.primed, true);
  });

  it("toast an elimination that happens while the page is watching", () => {
    const primed = toastQueue(null, [out(1, "Rook")], 0);
    const now = toastQueue(primed, [out(2, "Tess", { detail: "zone" })], 1000);

    assert.equal(now.toasts.length, 1);
    assert.match(now.toasts[0].text, /Tess/);
  });

  it("name the killer when the death named one", () => {
    const primed = toastQueue(null, [], 0);
    const now = toastQueue(primed, [out(1, "Rook", { actor: "Vela", detail: "knife" })], 0);

    assert.equal(now.toasts[0].text, "Rook — eliminated by Vela");
  });

  it("say what did it when nobody did", () => {
    const primed = toastQueue(null, [], 0);
    const now = toastQueue(primed, [out(1, "Rook", { detail: "zone" })], 0);

    assert.equal(now.toasts[0].text, "Rook — eliminated by the zone");
  });

  it("are the loudest thing on the stream when a match is won", () => {
    const primed = toastQueue(null, [], 0);
    const won = toastQueue(primed, [event(1, "MatchEnded", { player: "Vela" })], 0);

    assert.equal(won.toasts[0].kind, "won");
    assert.equal(won.toasts[0].text, "Vela wins");
  });

  it("still say something when a match ends with nobody left", () => {
    const primed = toastQueue(null, [], 0);
    const ended = toastQueue(primed, [event(1, "MatchEnded")], 0);

    assert.equal(ended.toasts[0].text, "nobody left standing");
  });

  it("ignore everything that is not somebody going out", () => {
    const primed = toastQueue(null, [], 0);
    const quiet = toastQueue(primed, [event(1, "PlayerJoined", { player: "Vela" }),
      event(2, "ZoneBite", { player: "Vela" }), event(3, "PhaseStarted", { phase: 2 })], 0);

    assert.deepEqual(quiet.toasts, []);
  });

  it("go away on their own", () => {
    const primed = toastQueue(null, [], 0);
    const showing = toastQueue(primed, [out(1, "Rook")], 1000);

    assert.equal(toastQueue(showing, [], 1000 + TOAST_MS - 1).toasts.length, 1);
    assert.equal(toastQueue(showing, [], 1000 + TOAST_MS).toasts.length, 0);
  });

  it("do not toast the same elimination twice, because pushes overlap", () => {
    const primed = toastQueue(null, [], 0);
    const first = toastQueue(primed, [out(1, "Rook")], 0);
    const again = toastQueue(first, [out(1, "Rook")], 100);

    assert.equal(again.toasts.length, 1);
  });

  it("show the newest first and never cover the shot", () => {
    const primed = toastQueue(null, [], 0);
    const many = toastQueue(
      primed,
      Array.from({ length: TOAST_LIMIT + 2 }, (_, i) => out(i + 1, `Player${i + 1}`)),
      0,
    );

    assert.equal(many.toasts.length, TOAST_LIMIT);
    assert.match(many.toasts[0].text, new RegExp(`Player${TOAST_LIMIT + 2}`));
  });

  it("say nothing again after a restart rather than replaying the whole log", () => {
    const primed = toastQueue(null, [out(400, "Rook")], 0);
    const restarted = toastQueue(primed, [out(1, "Tess"), out(2, "Bram")], 1000);

    assert.deepEqual(restarted.toasts, []);
    assert.equal(restarted.highest, 2);
  });
});

describe("the match board", () => {
  const board = (players, events) => matchBoard(players, events);
  const names = (rows) => rows.map((row) => row.name);

  it("is empty before anybody has reported", () => {
    assert.deepEqual(board([], []), []);
  });

  it("puts the named winner first, alone", () => {
    const rows = board(
      [player("Vela"), player("Rook"), player("Tess", false)],
      [out(1, "Tess"), event(2, "MatchEnded", { player: "Vela" })],
    );

    assert.equal(rows[0].name, "Vela");
    assert.equal(rows[0].placement, 1);
    assert.equal(rows[1].placement, 2, "the other survivor is second, not joint first");
  });

  it("lets survivors share a placement rather than inventing an order between them", () => {
    const rows = board([player("Vela"), player("Rook"), player("Tess", false)], [out(1, "Tess")]);

    assert.deepEqual(rows.map((row) => row.placement), [1, 1, 3]);
    assert.equal(rows[0].outlasted, rows[1].outlasted);
  });

  it("places the eliminated latest-out-first, which is what last man standing means", () => {
    const rows = board(
      [player("Vela"), player("Rook", false), player("Tess", false), player("Bram", false)],
      [out(1, "Bram"), out(2, "Tess"), out(3, "Rook")],
    );

    assert.deepEqual(names(rows), ["Vela", "Rook", "Tess", "Bram"]);
    assert.deepEqual(rows.map((row) => row.placement), [1, 2, 3, 4]);
  });

  it("pays a point per player outlasted, the way ScoringRules does", () => {
    const rows = board(
      [player("Vela"), player("Rook", false), player("Tess", false), player("Bram", false)],
      [out(1, "Bram"), out(2, "Tess"), out(3, "Rook")],
    );

    assert.deepEqual(rows.map((row) => row.outlasted), [3, 2, 1, 0]);
    assert.equal(POINTS_PER_PLAYER_OUTLASTED, 1);
  });

  it("pays two a kill, counted off the attributions rather than kept as a tally", () => {
    const rows = board(
      [player("Vela"), player("Rook", false), player("Tess", false)],
      [out(1, "Rook"), out(2, "Tess"),
        event(3, "KillAttributed", { player: "Rook", actor: "Vela" }),
        event(4, "KillAttributed", { player: "Tess", actor: "Vela" })],
    );

    assert.equal(POINTS_PER_KILL, 2);
    assert.equal(rows[0].kills, 2);
    assert.equal(rows[0].points, 2 * POINTS_PER_PLAYER_OUTLASTED + 2 * POINTS_PER_KILL);
  });

  it("lets a later attribution correct an earlier one, because the log is append-only", () => {
    const rows = board(
      [player("Vela"), player("Rook"), player("Tess", false)],
      [out(1, "Tess"),
        event(2, "KillAttributed", { player: "Tess", actor: "Vela" }),
        event(3, "KillAttributed", { player: "Tess", actor: "Rook" })],
    );

    assert.equal(rows.find((row) => row.name === "Vela").kills, 0);
    assert.equal(rows.find((row) => row.name === "Rook").kills, 1);
  });

  it("pays nobody for killing themselves, because that is a misclick", () => {
    const rows = board(
      [player("Vela"), player("Tess", false)],
      [out(1, "Tess"), event(2, "KillAttributed", { player: "Tess", actor: "Tess" })],
    );

    assert.equal(rows.find((row) => row.name === "Tess").kills, 0);
  });

  it("credits nobody when an attribution named nobody", () => {
    const rows = board(
      [player("Vela"), player("Tess", false)],
      [out(1, "Tess"), event(2, "KillAttributed", { player: "Tess", actor: null })],
    );

    assert.deepEqual(rows.map((row) => row.kills), [0, 0]);
  });

  it("breaks a tie by name ordinally, the way the ledger does", () => {
    const rows = board([player("Zoe"), player("Adam"), player("adam")], []);

    assert.deepEqual(names(rows), ["Adam", "Zoe", "adam"]);
  });

  it("ignores a winner the match named who is no longer standing", () => {
    const rows = board(
      [player("Vela", false), player("Rook")],
      [event(1, "MatchEnded", { player: "Vela" }), out(2, "Vela")],
    );

    assert.equal(rows[0].name, "Rook");
  });

  it("puts a dead player the feed never saw go out at the back", () => {
    const rows = board(
      [player("Vela"), player("Rook", false), player("Ghost", false)],
      [out(9, "Rook")],
    );

    assert.deepEqual(names(rows), ["Vela", "Rook", "Ghost"]);
  });

  it("says how somebody went out, for the row that shows it", () => {
    const rows = board([player("Rook", false)], [out(1, "Rook", { detail: "zone", actor: "Vela" })]);

    assert.equal(rows[0].by, "Vela");
    assert.equal(rows[0].cause, "zone");
  });

  it("has a row that shows it", () => {
    const killed = board([player("Rook", false)], [out(1, "Rook", { detail: "zone", actor: "Vela" })]);
    assert.equal(exitNote(killed[0]), "by Vela");

    const wall = board([player("Rook", false)], [out(1, "Rook", { detail: "zone" })]);
    assert.equal(exitNote(wall[0]), "zone");
  });

  it("says nothing for the living, so the column is blank rather than reassuring", () => {
    const rows = board([player("Vela")], []);

    assert.equal(exitNote(rows[0]), null);
  });

  it("says nothing for an elimination the window never held", () => {
    const rows = board([player("Ghost", false)], []);

    assert.equal(exitNote(rows[0]), null);
  });

  it("prefers the killer to the cause, because that is what a viewer wants", () => {
    assert.equal(exitNote({ alive: false, by: "Vela", cause: "zone" }), "by Vela");
  });

  it("reads only the newest ending, so a replayed match does not name an old winner", () => {
    const rows = board(
      [player("Vela"), player("Rook")],
      [event(1, "MatchEnded", { player: "Rook" }), event(2, "MatchEnded", { player: "Vela" })],
    );

    assert.equal(rows[0].name, "Vela");
  });
});

describe("what the board remembers", () => {
  it("accumulates across pushes, because one push carries only what is new", () => {
    const first = keepEvents(null, [out(1, "Bram")]);
    const second = keepEvents(first, [out(2, "Marlow")]);

    assert.deepEqual(second.events.map((e) => e.player), ["Bram", "Marlow"]);
  });

  it("keeps only the kinds a board reads, so a match's worth of it stays small", () => {
    const kept = keepEvents(null, [
      out(1, "Bram"),
      event(2, "ZoneBite", { player: "Vela", amount: 3 }),
      event(3, "KillAttributed", { player: "Bram", actor: "Vela" }),
      event(4, "PlayerJoined", { player: "Tess" }),
      event(5, "MatchEnded", { player: "Vela" }),
    ]);

    assert.deepEqual(kept.events.map((e) => e.kind),
      ["PlayerEliminated", "KillAttributed", "MatchEnded"]);
    assert.equal(kept.highest, 5, "the watermark still passes over what was not kept");
    assert.ok(BOARD_KINDS.has("PlayerEliminated"));
  });

  it("ignores a resent event, because pushes overlap on purpose", () => {
    const first = keepEvents(null, [out(1, "Bram")]);
    const again = keepEvents(first, [out(1, "Bram"), out(2, "Marlow")]);

    assert.equal(again.events.length, 2);
  });

  it("starts over when the log does, so a new match's board is not the old one's", () => {
    const before = keepEvents(null, [out(400, "Bram"), out(401, "Marlow")]);
    const after = keepEvents(before, [out(1, "Tess")]);

    assert.deepEqual(after.events.map((e) => e.player), ["Tess"]);
    assert.equal(after.highest, 1);
  });
});

describe("the board and the match it belongs to", () => {
  it("ignores the previous group's eliminations once the next group is up", () => {
    const rows = matchBoard(
      [player("Vela"), player("Rook")],
      [out(1, "Ghost", { group: 3 }), event(2, "KillAttributed", { player: "Ghost", actor: "Vela", group: 3 })],
      { group: 4, generation: 1 },
    );

    assert.deepEqual(rows.map((row) => row.kills), [0, 0]);
    assert.deepEqual(rows.map((row) => row.placement), [1, 1]);
  });

  it("ignores an earlier attempt at the same group", () => {
    const rows = matchBoard(
      [player("Vela")],
      [event(1, "KillAttributed", { player: "Rook", actor: "Vela", group: 4, generation: 1 })],
      { group: 4, generation: 2 },
    );

    assert.equal(rows[0].kills, 0);
  });

  it("reads every event when it is not told which match is running", () => {
    const rows = matchBoard([player("Vela"), player("Tess", false)], [out(1, "Tess")]);

    assert.equal(rows[1].name, "Tess");
  });
});

describe("the board does not care what order it is fed", () => {
  it("takes the highest-sequence attribution as the last word, not the last one it saw", () => {
    const newestFirst = [
      event(3, "KillAttributed", { player: "Tess", actor: "Rook" }),
      event(2, "KillAttributed", { player: "Tess", actor: "Vela" }),
      out(1, "Tess"),
    ];

    const rows = matchBoard([player("Vela"), player("Rook"), player("Tess", false)], newestFirst);

    assert.equal(rows.find((row) => row.name === "Rook").kills, 1);
    assert.equal(rows.find((row) => row.name === "Vela").kills, 0);
  });

  it("takes the highest-sequence ending as the one that names the winner", () => {
    const rows = matchBoard(
      [player("Vela"), player("Rook")],
      [event(2, "MatchEnded", { player: "Vela" }), event(1, "MatchEnded", { player: "Rook" })],
    );

    assert.equal(rows[0].name, "Vela");
  });
});

describe("the tournament table", () => {
  const line = (place, name, points, extra = {}) => ({
    place,
    name,
    points,
    played: 2,
    wins: 0,
    best: 2,
    kills: 0,
    outlasted: points,
    ...extra,
  });

  it("has nothing to draw before a match has finished", () => {
    assert.equal(tournamentTable(null), null);
    assert.equal(tournamentTable(undefined), null);
    assert.equal(tournamentTable({ matches: 0, entries: [] }), null);
  });

  it("draws the rows in the order they arrived, and does not renumber them", () => {
    const table = tournamentTable({
      matches: 2,
      entries: [line(1, "Vela", 9), line(2, "Rook", 4)],
    });

    assert.deepEqual(table.rows.map((row) => [row.place, row.name]), [[1, "Vela"], [2, "Rook"]]);
  });

  it("says how far into the tournament it is, and says one match as one", () => {
    assert.equal(tournamentTable({ matches: 1, entries: [line(1, "Vela", 4)] }).caption, "after 1 match");
    assert.equal(tournamentTable({ matches: 4, entries: [line(1, "Vela", 4)] }).caption, "after 4 matches");
  });

  it("highlights a leader who holds the place alone", () => {
    const table = tournamentTable({
      matches: 2,
      entries: [line(1, "Vela", 9), line(2, "Rook", 4)],
    });

    assert.equal(table.lead, "Vela");
  });

  it("highlights nobody when the lead is shared", () => {
    const table = tournamentTable({
      matches: 2,
      entries: [line(1, "Ada", 4), line(1, "Vela", 4), line(3, "Rook", 1)],
    });

    assert.equal(table.lead, null);
  });

  it("highlights a lone entry", () => {
    assert.equal(tournamentTable({ matches: 1, entries: [line(1, "Vela", 4)] }).lead, "Vela");
  });
});

describe("the addresses an operator is told to paste into OBS", () => {
  it("are under whichever booth the page is on", () => {
    assert.equal(
      overlayURL("board", { origin: "https://sn.caes.ar", pathname: "/b/mzxw6ytb/overlay.html" }, "v"),
      "https://sn.caes.ar/b/mzxw6ytb/overlay.html?panel=board&token=v",
    );
  });

  it("are the bare path on the tournament's own booth, exactly as they always were", () => {
    assert.equal(
      overlayURL("board", { origin: "https://sn.caes.ar", pathname: "/overlay.html" }, "v"),
      "https://sn.caes.ar/overlay.html?panel=board&token=v",
    );
  });

  it("say where the token goes when there is not one to put there yet", () => {
    assert.match(overlayURL("board", { origin: "https://sn.caes.ar", pathname: "/" }, ""),
      /token=YOUR_VIEW_TOKEN$/);
  });
});
