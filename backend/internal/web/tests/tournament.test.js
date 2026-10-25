import assert from "node:assert/strict";
import { existsSync, readFileSync } from "node:fs";
import { describe, it } from "node:test";
import { fileURLToPath } from "node:url";

import { rules } from "./stylesheet.js";
import { SCHEDULE, current, dateOf, kindOf, slotState, stages, timeOf, until } from "../static/tournament.js";

const slots = SCHEDULE.flatMap((day) => day.slots);
const games = slots.filter((slot) => slot.group !== undefined);

// 14:00 in Germany on Saturday, the first slot.
const DAY_ONE = 1792843200;

describe("the schedule", () => {
  it("numbers its games 1 to 26 in the order they are played, which is the order /lms next counts", () => {
    assert.deepEqual(games.map((slot) => slot.group), Array.from({ length: 26 }, (_, i) => i + 1));
  });

  it("puts each game where the bracket puts it, so a game's title is its group's name", () => {
    const named = Object.fromEntries(games.map((slot) => [slot.group, slot.title]));

    assert.equal(named[1], "Group 1 · game 1");
    assert.equal(named[12], "Group 4 · game 3");
    assert.equal(named[13], "Losers bracket · game 1");
    assert.equal(named[16], "Winners semi 1 · game 3");
    assert.equal(named[17], "Losers bracket · game 2");
    assert.equal(named[18], "Winners semi 2 · game 1");
    assert.equal(named[23], "Losers bracket · game 5");
    assert.equal(named[26], "Grand final · game 3");
  });

  it("has no gap and no overlap inside a day, so every minute of it says what is on", () => {
    for (const day of SCHEDULE) {
      for (let i = 1; i < day.slots.length; i++) {
        assert.equal(day.slots[i].start, day.slots[i - 1].end, `${day.slots[i].title} on day ${day.day}`);
      }
    }
  });

  it("gives every game the event's 35-minute slot", () => {
    for (const game of games) assert.equal(game.end - game.start, 35 * 60, game.title);
  });
});

describe("what is on", () => {
  const game = games[0];

  it("is nothing yet before a slot starts, live while it runs and done once it ends", () => {
    assert.equal(slotState(game, game.start - 1), "upcoming");
    assert.equal(slotState(game, game.start), "live");
    assert.equal(slotState(game, game.end - 1), "live");
    assert.equal(slotState(game, game.end), "done");
  });

  it("keeps the closing speeches live once they start, because they have no end", () => {
    const closing = slots.at(-1);

    assert.equal(closing.end, null);
    assert.equal(slotState(closing, closing.start + 86_400), "live");
  });

  it("names the first slot as next before the event starts", () => {
    assert.deepEqual(current(DAY_ONE - 3600), { live: null, next: slots[0] });
  });

  it("says an intermission is on and names the game after it", () => {
    const intermission = slots.find((slot) => slot.start === games[0].end);

    assert.deepEqual(current(intermission.start + 60), { live: intermission, next: games[1] });
  });

  it("names the next game as next during a game, not the intermission in between", () => {
    assert.deepEqual(current(games[0].start + 60), { live: games[0], next: games[1] });
  });

  it("calls intermissions and the break a pause, games a game and the rest other", () => {
    assert.deepEqual(
      [slots[0], games[0], slots[2], slots.find((slot) => slot.title === "Break")].map(kindOf),
      ["other", "game", "pause", "pause"],
    );
  });

  it("names day two's first slot as next overnight", () => {
    const overnight = SCHEDULE[0].slots.at(-1).end + 3600;

    assert.deepEqual(current(overnight), { live: null, next: SCHEDULE[1].slots[0] });
  });
});

describe("the time a viewer reads", () => {
  it("is in the viewer's own zone", () => {
    assert.equal(timeOf(DAY_ONE, { locale: "en-GB", timeZone: "UTC" }), "12:00");
    assert.equal(timeOf(DAY_ONE, { locale: "en-GB", timeZone: "Europe/Berlin" }), "14:00");
    assert.equal(timeOf(DAY_ONE, { locale: "en-GB", timeZone: "America/New_York" }), "08:00");
  });

  it("follows the clocks going back between the two days", () => {
    // Europe leaves summer time between them: day two starts an hour later in UTC, at the same 14:00 German wall clock.
    assert.equal(timeOf(SCHEDULE[1].slots[0].start, { locale: "en-GB", timeZone: "UTC" }), "13:00");
    assert.equal(timeOf(SCHEDULE[1].slots[0].start, { locale: "en-GB", timeZone: "Europe/Berlin" }), "14:00");
  });

  it("dates a day where the viewer is, which is not always where it started", () => {
    assert.equal(dateOf(DAY_ONE, { locale: "en-GB", timeZone: "Europe/Berlin" }), "Saturday 24 October");
    assert.equal(dateOf(SCHEDULE[0].slots.at(-1).end, { locale: "en-GB", timeZone: "Pacific/Auckland" }), "Sunday 25 October");
  });
});

describe("how long until", () => {
  it("is minutes when it is under an hour", () => {
    assert.equal(until(1000, 1000 + 59), "in 1 min");
    assert.equal(until(1000, 1000 + 45 * 60), "in 45 min");
  });

  it("is hours and minutes when it is under a day", () => {
    assert.equal(until(0, 2 * 3600 + 5 * 60), "in 2 h 5 min");
  });

  it("is days and hours beyond that", () => {
    assert.equal(until(0, 2 * 86_400 + 3 * 3600 + 59), "in 2 d 3 h");
  });
});

describe("the stages of the bracket", () => {
  const group = (number, members) => ({ number, name: "", members });

  it("are the four groups, both semi-finals, the losers bracket and the final, with no booth to ask", () => {
    assert.deepEqual(
      stages(null).map((stage) => [stage.name, stage.day, stage.members]),
      [
        ["Group 1", 1, []],
        ["Group 2", 1, []],
        ["Group 3", 1, []],
        ["Group 4", 1, []],
        ["Losers bracket", 2, []],
        ["Winners semi 1", 2, []],
        ["Winners semi 2", 2, []],
        ["Grand final", 2, []],
      ],
    );
  });

  it("take their players from the booth's groups, everybody who plays any of the stage's games once", () => {
    const bracket = {
      groups: [
        group(1, ["Viscerface", "Lixon", "Aci", "SubZero"]),
        group(2, ["Viscerface", "Lixon", "Aci", "SubZero"]),
        group(13, ["Aci", "Queue"]),
        group(17, ["Queue", "Spud"]),
      ],
    };

    const named = Object.fromEntries(stages(bracket).map((stage) => [stage.name, stage.members]));

    assert.deepEqual(named["Group 1"], ["Viscerface", "Lixon", "Aci", "SubZero"]);
    assert.deepEqual(named["Losers bracket"], ["Aci", "Queue", "Spud"]);
    assert.deepEqual(named["Group 2"], []);
  });

  it("seat the final the booth computed, saying which series sent each finalist", () => {
    const bracket = {
      groups: [group(24, ["Typed", "By", "Hand"])],
      final: { from: [{ name: "Lixon", series: "winners semi 1" }, { name: "Spud", series: "losers final" }] },
    };

    const final = stages(bracket).at(-1);

    assert.deepEqual(final.members, ["Lixon", "Spud"]);
    assert.deepEqual(final.notes, ["winners semi 1", "losers final"]);
  });
});

describe("the tournament page", () => {
  // Read as text.
  const html = readFileSync(fileURLToPath(new URL("../static/tournament.html", import.meta.url)), "utf8");
  const brand = (html.match(/<span class="bar__title">([\s\S]*?)<\/span>\s*<span class="bar__sub"/)?.[1] ?? "")
    .replace(/<[^>]+>/g, "")
    .replace(/\s+/g, " ")
    .trim();

  it("names the event the organisers announced, in the tab and in the bar", () => {
    assert.match(html, /<title>Subnautica Creator Royale[^<]*<\/title>/);
    assert.equal(brand, "Subnautica Creator Royale");
  });

  it("shows the organisers' logo, named for a reader who cannot see it", () => {
    const logo = html.match(/<img[^>]*class="royale__logo"[^>]*>/)?.[0] ?? "";

    assert.match(logo, /alt="Subnautica Creator Royale"/);
    const src = logo.match(/src="([^"]+)"/)?.[1];
    assert.ok(existsSync(fileURLToPath(new URL(`../static/${src}`, import.meta.url))), `${src} is not in static/`);
  });

  it("unfurls a shared link as a card with the event's name and its picture", () => {
    const meta = (key) => html.match(new RegExp(`<meta (?:property|name)="${key}" content="([^"]*)"`))?.[1];

    assert.equal(meta("og:title"), "Subnautica Creator Royale");
    assert.equal(meta("twitter:card"), "summary_large_image");
    assert.match(meta("og:image") ?? "", /^https:\/\/sn\.caes\.ar\/[\w.-]+\.png$/);
    const picture = new URL(meta("og:image")).pathname.slice(1);
    assert.ok(existsSync(fileURLToPath(new URL(`../static/${picture}`, import.meta.url))), `${picture} is not in static/`);
  });

  it("sends a viewer to the cast, on Twitch and on YouTube, in a tab of its own", () => {
    const links = [...html.matchAll(/<a [^>]*class="watch__link[^"]*"[^>]*>/g)].map((match) => match[0]);
    const to = (href) => links.find((link) => link.includes(`href="${href}"`)) ?? "";

    for (const href of ["https://www.twitch.tv/goldstuffonyt", "https://www.youtube.com/@Goldstuffnotonyt"]) {
      assert.match(to(href), /target="_blank"/, `${href} is not linked, or replaces the schedule`);
      assert.match(to(href), /rel="noopener"/, `${href} can reach back into this page`);
    }
  });

  it("wears the event's colours on this page only, not on the booth's other sheets", () => {
    assert.match(html, /<body class="bracket royale">/);
  });
});

describe("the bracket's columns", () => {
  const css = readFileSync(fileURLToPath(new URL("../static/bracket.css", import.meta.url)), "utf8");

  it("step down toward the final: every column after the groups sits level with their middle", () => {
    const centred = rules(css).find((rule) => rule.selector === ".flow__col + .flow__col");

    assert.match(centred?.body ?? "", /justify-content:\s*center/);
  });
});

