import assert from "node:assert/strict";
import { describe, it } from "node:test";

import {
  EXPIRED,
  QUEUED,
  RAN,
  RECORDS_NONE,
  RECORDS_OK,
  RECORDS_UNREACHABLE,
  SENT,
  commandRows,
  controlGroups,
  controls,
  knownNames,
  linkNote,
  people,
  peopleBody,
  peopleText,
  reconcileNote,
  recordsNote,
  roster,
  rosterRead,
  squeeze,
  unanswered,
  verbFor,
  RULES_ADOPTED,
  RULES_QUEUED,
  RULES_REFUSED,
  RULES_SENT,
  RULES_WAITING,
  revisions,
  rulesBody,
  rulesNote,
  rulesProblems,
  rulesText,
  runningRules,
} from "../static/console.js";

/** A console document as `GET /console` sends one. */
function view(over = {}) {
  return {
    report: {
      v: 1,
      at: "2026-10-24T12:00:00.000Z",
      acceptsCommands: true,
      ran: 0,
      connected: [],
      verbs: [],
      answers: [],
      ...(over.report ?? {}),
    },
    reportedAt: "2026-10-24T12:00:00.000Z",
    commands: [],
    intent: { group: 1, ghosts: [], contestants: [], unbound: [], pending: [], reconciling: true, why: "" },
    people: [],
    groups: [],
    overrides: [],
    records: RECORDS_OK,
    ...over,
  };
}

function verb(word, over = {}) {
  return { verb: word, takesSubject: false, mustBeTyped: false, ...over };
}

describe("the controls", () => {
  it("is built from the vocabulary the mod reported, so it cannot drift from the build", () => {
    const drawn = controls({ verbs: [verb("status"), verb("start")] });

    assert.deepEqual(
      drawn.map((one) => one.verb),
      ["status", "start"],
    );
  });

  it("gives a verb about one player a field to put the name in", () => {
    const [ghost] = controls({ verbs: [verb("ghost", { takesSubject: true })] });

    assert.equal(ghost.kind, "subject");
    assert.equal(ghost.placeholder, "player");
  });

  it("gives the camera verb the same field, so it gets a button", () => {
    const [look] = controls({ verbs: [verb("look", { takesSubject: true })] });

    assert.equal(look.kind, "subject");
    assert.equal(look.placeholder, "player");
    assert.equal(verbFor(look, " Vela "), "look Vela");
  });

  it("makes a verb the mod says must be typed a confirmation rather than a button", () => {
    const [sweep] = controls({ verbs: [verb("podclear all", { mustBeTyped: true })] });

    assert.equal(sweep.kind, "typed");
    assert.equal(sweep.confirm, "podclear all");
  });

  it("draws nothing at all before a mod has reported", () => {
    assert.deepEqual(controls(null), []);
  });
});

describe("arming a control", () => {
  it("sends a plain verb whatever is in the box", () => {
    assert.equal(verbFor({ kind: "button", verb: "start" }, ""), "start");
  });

  it("refuses a verb that takes a name with no name in it", () => {
    assert.equal(verbFor({ kind: "subject", verb: "ghost" }, "  "), null);
  });

  it("joins the verb and the name the way the mod parses them", () => {
    assert.equal(verbFor({ kind: "subject", verb: "ghost" }, " Caster "), "ghost Caster");
  });

  it("squeezes whitespace the way the mod does", () => {
    // Nitrox splits a command line on spaces and the mod joins it back up, so a name with a run of spaces must arrive as one.
    assert.equal(verbFor({ kind: "subject", verb: "unghost" }, "Ada   Plays"), "unghost Ada Plays");
    assert.equal(squeeze("  podclear   all "), "podclear all");
  });

  it("will not fire a typed verb until it has been typed", () => {
    const sweep = { kind: "typed", verb: "podclear all", confirm: "podclear all" };

    assert.equal(verbFor(sweep, ""), null);
    assert.equal(verbFor(sweep, "podclear"), null);
    assert.equal(verbFor(sweep, "yes"), null);
    assert.equal(verbFor(sweep, "podclear all"), "podclear all");
  });

  it("takes the confirmation without case, because it is a person typing under pressure", () => {
    const forced = { kind: "typed", verb: "round force", confirm: "round force" };

    assert.equal(verbFor(forced, "ROUND FORCE"), "round force");
  });
});

describe("the queue", () => {
  it("tells a command nobody has collected from one that failed", () => {
    const [waiting, gone] = commandRows(
      view({
        commands: [
          { id: 2, verb: "start", by: "ops", state: QUEUED, queuedAt: "2026-10-24T12:00:00.000Z" },
          { id: 1, verb: "start", by: "ops", state: EXPIRED, queuedAt: "2026-10-24T11:00:00.000Z" },
        ],
      }),
    );

    assert.match(waiting.said, /waiting for the game server/);
    assert.equal(waiting.tone, "");

    assert.match(gone.said, /given up on/);
    assert.equal(gone.tone, "bad");
  });

  it("shows the mod's own sentence word for word", () => {
    const refusal = "a group is 5 and this counted 4: Ada, Bea, Cy, Dov. Run `lms round force` to prepare it anyway.";

    const [row] = commandRows(
      view({
        commands: [
          { id: 1, verb: "round", by: "ops", state: RAN, answer: refusal, queuedAt: "x", ranAt: "y" },
        ],
      }),
    );

    assert.equal(row.answer, refusal, "on the day that sentence is the help system");
    assert.equal(row.tone, "ok");
  });

  it("marks a command the game server has taken but not answered for", () => {
    const [row] = commandRows(
      view({ commands: [{ id: 1, verb: "skip", by: "ops", state: SENT, queuedAt: "x", sentAt: "y" }] }),
    );

    assert.equal(row.tone, "warn");
    assert.match(row.said, /no answer yet/);
  });
});

describe("a control that has fired", () => {
  const fired = (state) =>
    view({ commands: [{ id: 7, verb: "skip", by: "ops", state, queuedAt: "2026-10-24T12:00:00.000Z" }] });

  it("stays disarmed while nothing has collected the command", () => {
    assert.equal(unanswered(fired(QUEUED), 7), true);
  });

  it("stays disarmed while the game server has it and has not answered", () => {
    assert.equal(unanswered(fired(SENT), 7), true);
  });

  it("stays disarmed while the press is still in flight and has no row to point at", () => {
    assert.equal(unanswered(view({ commands: [] }), null, true), true);
  });

  it("arms again the moment the command has been answered", () => {
    assert.equal(unanswered(fired(RAN), 7), false);
    assert.equal(unanswered(fired(EXPIRED), 7), false);
  });

  it("arms again when the booth has no such row, rather than stranding the button", () => {
    assert.equal(unanswered(view({ commands: [] }), 7), false);
    assert.equal(unanswered(view(), null), false, "a control that has never fired is armed");
  });
});

describe("the roster", () => {
  it("draws what the booth intends beside what the mod believes", () => {
    const rows = roster(
      view({
        report: { connected: [{ name: "Ada", inMatch: true }, { name: "Caster", inMatch: true }] },
        intent: { group: 1, ghosts: ["Caster"], contestants: ["Ada"], unbound: [], pending: ["ghost Caster"], reconciling: true },
      }),
    );

    assert.deepEqual(rows[0], {
      name: "Ada",
      believed: "contestant",
      intended: "contestant",
      unbound: false,
      pending: false,
    });

    assert.equal(rows[1].believed, "contestant");
    assert.equal(rows[1].intended, "ghost");
    assert.equal(rows[1].pending, true, "the two disagree until the tick that applies it");
  });

  it("says when nobody wrote a joiner down rather than guessing at them", () => {
    const [row] = roster(
      view({
        report: { connected: [{ name: "WhoIsThis", inMatch: true }] },
        intent: { group: 1, ghosts: [], contestants: [], unbound: ["WhoIsThis"], pending: [], reconciling: true },
      }),
    );

    assert.equal(row.unbound, true);
    assert.equal(row.intended, "", "the booth has no opinion, and drawing one would be a guess");
    assert.equal(row.pending, false);
  });

  it("matches a name without case, the way the mod and the roster do", () => {
    const [row] = roster(
      view({
        report: { connected: [{ name: "adaplays", inMatch: true }] },
        intent: { group: 1, ghosts: [], contestants: ["AdaPlays"], unbound: [], pending: [], reconciling: true },
      }),
    );

    assert.equal(row.intended, "contestant");
    assert.equal(row.pending, false);
  });
});

describe("the people", () => {
  it("shows the label and the Nitrox name when they differ", () => {
    const [ada, bea] = people(
      view({
        people: [
          { id: 1, name: "AdaPlays", label: "Ada" },
          { id: 2, name: "Bea", label: "Bea" },
        ],
      }),
    );

    assert.equal(ada.drawn, "Ada · AdaPlays", "a roster showing one of them is one you bind wrong");
    assert.equal(bea.drawn, "Bea");
  });

  it("offers every name it knows to a verb that takes one", () => {
    const names = knownNames(
      view({
        report: { connected: [{ name: "WhoIsThis", inMatch: true }] },
        people: [{ id: 1, name: "AdaPlays", label: "Ada" }],
      }),
    );

    assert.deepEqual(names, ["AdaPlays", "WhoIsThis"]);
  });

  it("carries the id through an edit, because that is what makes a rename a rename", () => {
    const body = peopleBody(
      JSON.stringify({ people: [{ id: 7, name: " AdaPlays ", label: "Ada" }, { name: "Bea" }] }),
    );

    assert.equal(body.people[0].id, 7);
    assert.equal(body.people[0].name, "AdaPlays");
    assert.equal(body.people[1].id, 0, "somebody new has no id yet");
  });

  it("round-trips the roster it was given", () => {
    const original = view({ people: [{ id: 1, name: "AdaPlays", label: "Ada", notes: "group 2" }] });

    assert.deepEqual(peopleBody(peopleText(original)).people, [
      { id: 1, name: "AdaPlays", label: "Ada", notes: "group 2" },
    ]);
  });

  it("sends the removal count the operator typed, so a write that takes somebody off can be confirmed", () => {
    const body = peopleBody(JSON.stringify({ people: [{ id: 1, name: "AdaPlays" }], removing: 1 }));

    assert.equal(body.removing, 1);
  });

  it("sends no removal count the operator did not type, because absent is not zero to the booth", () => {
    const body = peopleBody(JSON.stringify({ people: [{ id: 1, name: "AdaPlays" }] }));

    assert.equal("removing" in body, false);
  });

  it("does not count a roster it could not read as one it read", () => {
    assert.equal(rosterRead(view({ roster: "ok" })), true);
    assert.equal(rosterRead(view({ roster: "unreachable" })), false);
    assert.equal(rosterRead(view({ roster: "none" })), false);
  });
});

describe("what the booth says it is doing", () => {
  it("says why it is doing nothing rather than looking idle", () => {
    const note = reconcileNote(
      view({
        intent: {
          group: 1, ghosts: [], contestants: [], unbound: [], pending: [],
          reconciling: false,
          why: "group 1 has nobody assigned to it, so the rule is not applied",
        },
      }),
    );

    assert.match(note.text, /nobody assigned/);
    assert.equal(note.tone, "warn");
  });

  it("says what it has asked for and not seen applied", () => {
    const note = reconcileNote(
      view({
        intent: {
          group: 2, ghosts: ["Caster"], contestants: ["Ada"], unbound: [],
          pending: ["ghost Caster"], reconciling: true, why: "",
        },
      }),
    );

    assert.match(note.text, /ghost Caster/);
    assert.match(note.text, /not applied yet/);
  });

  it("counts the two sides when everything agrees", () => {
    const note = reconcileNote(
      view({
        intent: {
          group: 3, ghosts: ["Caster", "Bea"], contestants: ["Ada"], unbound: [],
          pending: [], reconciling: true, why: "",
        },
      }),
    );

    assert.equal(note.tone, "ok");
    assert.match(note.text, /1 in the match, 2 ghosts/);
  });
});

describe("whether the game server is still there", () => {
  it("says so before anything has reported", () => {
    assert.equal(linkNote({ report: null }).tone, "bad");
  });

  it("is content while reports keep arriving", () => {
    const note = linkNote(view(), Date.parse("2026-10-24T12:00:10.000Z"));

    assert.equal(note.tone, "ok");
  });

  it("notices a game server that has gone quiet", () => {
    const note = linkNote(view(), Date.parse("2026-10-24T12:02:00.000Z"));

    assert.equal(note.tone, "bad");
    assert.match(note.text, /quiet for 120s/);
  });

  it("says when a server has been told to refuse commands", () => {
    const note = linkNote(
      view({ report: { acceptsCommands: false, connected: [], verbs: [], answers: [] } }),
      Date.parse("2026-10-24T12:00:05.000Z"),
    );

    assert.equal(note.tone, "warn");
    assert.match(note.text, /acceptsCommands/);
  });
});

describe("the rules", () => {
  const written = (over = {}) => ({
    revision: 4,
    yaml: "match:\n  cycleLength: 45s\n",
    by: "ops",
    note: "a shorter cycle",
    state: RULES_ADOPTED,
    answer: "the rules are revision 4",
    problems: [],
    ...over,
  });

  it("says nothing has been written rather than drawing an empty config", () => {
    const note = rulesNote(view());

    assert.match(note.text, /running its own file/);
  });

  it("says the game server's own file is what is running, rather than inventing a queue", () => {
    const note = rulesNote(view({ rules: written({ revision: 0, state: "", answer: "", by: "the game server" }) }));

    assert.doesNotMatch(note.text, /waiting for the game server to collect/);
    assert.match(note.text, /the game server's own file/);
    assert.equal(note.tone, "");
  });

  it("still says a revision nobody has collected is waiting", () => {
    const note = rulesNote(view({ rules: written({ state: RULES_QUEUED }) }));

    assert.match(note.text, /waiting for the game server to collect it/);
  });

  it("says it does not know a state rather than calling it queued", () => {
    const note = rulesNote(view({ rules: written({ state: "collected" }) }));

    assert.doesNotMatch(note.text, /waiting for the game server to collect it/);
    assert.match(note.text, /does not know/);
    assert.equal(note.tone, "warn");
  });

  it("tells written-and-waiting from refused", () => {
    const waiting = rulesNote(view({ rules: written({ state: RULES_WAITING, answer: "a match is running" }) }));
    const refused = rulesNote(view({ rules: written({ state: RULES_REFUSED, answer: "2 problems" }) }));

    assert.equal(waiting.tone, "warn");
    assert.match(waiting.text, /waiting for the lobby/);

    assert.equal(refused.tone, "bad");
    assert.match(refused.text, /refused/);
  });

  it("says when the rules are running", () => {
    const note = rulesNote(view({ rules: written() }));

    assert.equal(note.tone, "ok");
    assert.match(note.text, /revision 4 is running/);
    assert.match(note.text, /the rules are revision 4/);
  });

  it("says when the game server has it and has not answered", () => {
    assert.equal(rulesNote(view({ rules: written({ state: RULES_SENT }) })).tone, "warn");
  });

  it("shows the loader's own field paths word for word", () => {
    const problems = rulesProblems(
      view({
        rules: written({
          state: RULES_REFUSED,
          problems: ["zone.phases[1].radius: must be a number of metres", "match.cycleLength: is required"],
        }),
      }),
    );

    assert.deepEqual(problems, [
      "zone.phases[1].radius: must be a number of metres",
      "match.cycleLength: is required",
    ]);
  });

  it("prefills the editor with the file the game server is running, not the booth's last revision", () => {
    const both = view({
      rules: written(),
      running: { revision: 0, yaml: "match:\n  cycleLength: 10m\n", by: "the game server" },
    });

    assert.equal(rulesText(both), "match:\n  cycleLength: 10m\n");
  });

  it("keeps a revision still in flight in the editor, rather than the older running file", () => {
    for (const state of [RULES_QUEUED, RULES_SENT, RULES_WAITING]) {
      const inFlight = view({
        rules: written({ state }),
        running: { revision: 0, yaml: "match:\n  cycleLength: 10m\n", by: "the game server" },
      });

      assert.equal(rulesText(inFlight), "match:\n  cycleLength: 45s\n", state);
    }
  });

  it("falls back to the revision the booth holds when the game server has not named a file", () => {
    assert.equal(rulesText(view({ rules: written() })), "match:\n  cycleLength: 45s\n");
  });

  it("prefills with nothing before the game server has reported and nothing was written", () => {
    assert.equal(rulesText(view()), "");
  });

  it("sends the document exactly as it was typed", () => {
    const typed = "match:\n  cycleLength: 45s   # deliberate trailing spaces\n";

    assert.deepEqual(rulesBody(typed, "ops", "  a note  "), {
      yaml: typed,
      by: "ops",
      note: "a note",
    });
  });

  it("lists the recent revisions so one can be taken back", () => {
    const list = revisions(
      view({
        revisions: [
          { revision: 5, by: "ops", note: "a mistake", state: RULES_REFUSED, answer: "no" },
          { revision: 4, by: "ops", note: "the good one", state: RULES_ADOPTED, answer: "ok" },
        ],
      }),
    );

    assert.equal(list.length, 2);
    assert.equal(list[0].revision, 5, "newest first");
    assert.equal(list[0].tone, "bad");
    assert.equal(list[1].tone, "ok");
  });

  it("keys every row on the number a control has to ask the booth for", () => {
    const list = revisions(
      view({
        revisions: [
          { revision: 5, by: "ops", note: "", state: RULES_REFUSED, answer: "" },
          { revision: 4, by: "ops", note: "", state: RULES_ADOPTED, answer: "" },
        ],
      }),
    );

    for (const row of list) {
      assert.ok(Number.isInteger(row.revision) && row.revision > 0, `row ${JSON.stringify(row)}`);
      assert.equal(row.yaml, undefined, "the list is a list; the document comes from /rules/{n}");
    }
  });

  it("offers the file the game server is running as a row of its own", () => {
    const row = runningRules(
      view({ running: { revision: 0, yaml: "match:\n  cycleLength: 10m\n", by: "the game server" } }),
    );

    assert.equal(row.revision, 0);
    assert.equal(row.yaml, "match:\n  cycleLength: 10m\n");
    assert.equal(row.by, "the game server");
  });

  it("says nothing about a running file the game server has not named", () => {
    assert.equal(runningRules(view()), null);
    assert.equal(runningRules(view({ running: { revision: 0, yaml: "", by: "the game server" } })), null);
  });
});

describe("the verb rail's groups", () => {
  it("buckets by kind and nothing else, so a verb the mod adds lands in a group on its own", () => {
    const groups = controlGroups(
      controls({
        verbs: [
          verb("start"),
          verb("ghost", { takesSubject: true }),
          verb("podclear all", { mustBeTyped: true }),
          verb("brandnewverb"),
        ],
      }),
    );

    assert.deepEqual(
      groups.map((group) => group.key),
      ["buttons", "subjects", "typed"],
    );
    assert.deepEqual(
      groups[0].controls.map((one) => one.verb),
      ["start", "brandnewverb"],
    );
    assert.deepEqual(
      groups[1].controls.map((one) => one.verb),
      ["ghost"],
    );
    assert.deepEqual(
      groups[2].controls.map((one) => one.verb),
      ["podclear all"],
    );
  });

  it("keeps the mod's own order inside each group", () => {
    const groups = controlGroups(
      controls({ verbs: [verb("start"), verb("status"), verb("skip")] }),
    );

    assert.deepEqual(
      groups[0].controls.map((one) => one.verb),
      ["start", "status", "skip"],
    );
  });

  it("omits a group with nothing in it rather than drawing an empty shelf", () => {
    const groups = controlGroups(controls({ verbs: [verb("status")] }));

    assert.deepEqual(
      groups.map((group) => group.key),
      ["buttons"],
    );
  });

  it("draws no groups before a mod has reported", () => {
    assert.deepEqual(controlGroups([]), []);
    assert.deepEqual(controlGroups(controls(null)), []);
  });

  it("names the groups for the operator, in words the hint under them can stand by", () => {
    const groups = controlGroups(
      controls({
        verbs: [verb("start"), verb("ghost", { takesSubject: true }), verb("podclear all", { mustBeTyped: true })],
      }),
    );

    assert.deepEqual(
      groups.map((group) => group.title),
      ["press it", "about a player", "type it"],
    );
  });
});

describe("the database", () => {
  it("says nothing at all when it is there", () => {
    assert.equal(recordsNote(RECORDS_OK), null);
  });

  it("tells a booth with no database from one that cannot reach its own", () => {
    assert.equal(recordsNote(RECORDS_NONE).tone, "warn");
    assert.equal(recordsNote(RECORDS_UNREACHABLE).tone, "bad");
  });
});
