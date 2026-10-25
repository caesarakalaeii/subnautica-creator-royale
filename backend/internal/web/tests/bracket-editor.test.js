import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { describe, it } from "node:test";
import { fileURLToPath } from "node:url";

import {
  groupsFormBody,
  groupsFormReady,
  groupsFromBracket,
} from "../static/bracket.js";

describe("what the bracket editor starts from", () => {
  it("takes each group apart into a row of the form", () => {
    const rows = groupsFromBracket({
      groups: [{ number: 1, name: "Group 1", members: ["Vela", "Rook"] }],
    });

    assert.deepEqual(rows, [{ number: 1, name: "Group 1", series: "", members: ["Vela", "Rook"] }]);
  });

  it("keeps the series a game counts toward, so a save does not drop it", () => {
    const rows = groupsFromBracket({
      groups: [{ number: 14, name: "Semi", series: "winners semi 1", members: [] }],
    });

    assert.equal(rows[0].series, "winners semi 1");
  });

  it("fills an empty bracket with one blank row, so there is somewhere to type", () => {
    const rows = groupsFromBracket({});

    assert.deepEqual(rows, [{ number: 1, name: "", series: "", members: [] }]);
  });

  it("gives a group with no members an empty list rather than an undefined one", () => {
    const rows = groupsFromBracket({
      groups: [{ number: 2, name: "Group 2" }],
    });

    assert.deepEqual(rows[0].members, []);
  });

  it("leaves the names exactly as the booth holds them, because it does not judge them", () => {
    const rows = groupsFromBracket({
      groups: [{ number: 1, name: " Group 1 ", members: [" vela "] }],
    });

    assert.deepEqual(rows[0], { number: 1, name: " Group 1 ", series: "", members: [" vela "] });
  });
});

describe("what the bracket editor sends", () => {
  it("sends the rows as the groups the path takes", () => {
    const body = groupsFormBody([{ number: 1, name: "Group 1", members: ["Vela", "Rook"] }]);

    assert.deepEqual(body, {
      groups: [{ number: 1, name: "Group 1", series: "", members: ["Vela", "Rook"] }],
      final: [],
    });
  });

  it("turns what was typed into numbers and trims the names", () => {
    const body = groupsFormBody([
      { number: "3", name: " Group 3 ", series: " semi ", members: [" Vela ", " Rook "] },
    ]);

    assert.deepEqual(body.groups[0], { number: 3, name: "Group 3", series: "semi", members: ["Vela", "Rook"] });
  });

  // Saving the groups sends back the booth's final spec; omitting it would clear it, as the path replaces the bracket whole.
  it("sends back the final's spec it was given", () => {
    const spec = [{ series: "semi", seats: 2 }];

    assert.deepEqual(groupsFormBody([{ number: 1, name: "a", members: [] }], spec).final, spec);
  });

  it("keeps a group nobody is in, because one is written down before it is filled", () => {
    const body = groupsFormBody([{ number: 5, name: "Group 5", members: [] }]);

    assert.deepEqual(body.groups[0].members, []);
  });
});

describe("whether the bracket editor is ready to send", () => {
  const fiveGroups = [
    { number: 1, name: "Group 1", members: ["Vela", "Rook", "Ada", "Bea", "Cy"] },
    { number: 2, name: "Group 2", members: ["Dov", "Eve", "Fay", "Gus", "Hal"] },
    { number: 3, name: "Group 3", members: ["Ida", "Jon", "Kit", "Lou", "Max"] },
    { number: 4, name: "Group 4", members: ["Nel", "Ola", "Pia", "Quin", "Rex"] },
    { number: 5, name: "Group 5", members: ["Sam", "Tia", "Uma", "Vic", "Wes"] },
  ];

  it("sends the five groups and a final whose seats are computed", () => {
    assert.equal(groupsFormReady(fiveGroups), true);
  });

  it("sends a group nobody is in yet", () => {
    assert.equal(groupsFormReady([{ number: 1, name: "Group 1", members: [] }]), true);
  });

  it("holds a bracket with no name at all", () => {
    assert.equal(groupsFormReady([{ number: 1, name: "  ", members: [] }]), false);
  });

  it("holds a bracket numbered below one", () => {
    assert.equal(groupsFormReady([{ number: 0, name: "Group 1", members: [] }]), false);
  });

  it("holds a bracket whose number arrived as words", () => {
    assert.equal(groupsFormReady([{ number: "two", name: "Group 2", members: [] }]), false);
  });

  it("holds a group with a blank member in it", () => {
    assert.equal(
      groupsFormReady([{ number: 1, name: "Group 1", members: ["Vela", "   "] }]),
      false,
    );
  });

  it("holds a group that names one player twice", () => {
    assert.equal(
      groupsFormReady([{ number: 1, name: "Group 1", members: ["Vela", "Vela"] }]),
      false,
    );
  });

  it("lets one player sit in two different groups, because the service is the one that judges", () => {
    assert.equal(
      groupsFormReady([
        { number: 1, name: "Group 1", members: ["Vela"] },
        { number: 2, name: "Group 2", members: ["Vela"] },
      ]),
      true,
    );
  });
});

// Read as text: the browser gets these bytes, and an unimported identifier is a ReferenceError that the write's catch reports as a network failure.
const page = readFileSync(
  fileURLToPath(new URL("../static/bracket-page.js", import.meta.url)),
  "utf8",
);

describe("the page the editor ships on", () => {
  it("imports the boothPath its writes go through", () => {
    assert.match(page, /import\s*\{[^}]*\bboothPath\b[^}]*\}\s*from\s*["']\.\/booth\.js["']/);
  });
});
