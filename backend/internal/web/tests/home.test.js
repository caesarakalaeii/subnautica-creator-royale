import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { describe, it } from "node:test";
import { fileURLToPath } from "node:url";

import { boothURLs, expiryLine, obsSources, telemetryBlock } from "../static/home.js";
import { PANELS } from "../static/overlays.js";

function created(overrides = {}) {
  return {
    id: "mzxw6ytb",
    ingestToken: "ingest-token",
    viewToken: "view-token",
    operatorToken: "operator-token",
    expiresAt: "2026-11-02T12:00:00Z",
    dashboardUrl: "/b/mzxw6ytb/",
    ingestUrl: "/b/mzxw6ytb/ingest",
    ...overrides,
  };
}

describe("what a player pastes into their own config", () => {
  it("is a telemetry block with the booth's own absolute ingest URL in it", () => {
    const block = telemetryBlock(created(), "https://booth.example");

    assert.match(block, /^telemetry:$/m);
    assert.match(block, /^ {2}url: https:\/\/booth\.example\/b\/mzxw6ytb\/ingest$/m);
  });

  it("carries no token, because the server takes that from its environment", () => {
    const block = telemetryBlock(created(), "https://booth.example");

    assert.doesNotMatch(block, /token:/);
    assert.doesNotMatch(block, /ingest-token/);
  });
});

describe("the URLs a player is handed", () => {
  it("are absolute, so they can be opened and pasted somewhere else", () => {
    const urls = boothURLs(created(), "https://booth.example");

    assert.equal(urls.dashboard, "https://booth.example/b/mzxw6ytb/");
    assert.equal(urls.ingest, "https://booth.example/b/mzxw6ytb/ingest");
  });

  it("carry the view token on the dashboard, because nothing else would let it connect", () => {
    const urls = boothURLs(created(), "https://booth.example");

    assert.ok(urls.dashboardWithToken.startsWith("https://booth.example/b/mzxw6ytb/"));
    assert.match(urls.dashboardWithToken, /[?&]token=view-token\b/);
  });
});

describe("when the booth dies", () => {
  it("is said as a duration, which is the question a player is asking", () => {
    const line = expiryLine(created(), new Date("2026-11-01T12:00:00Z"));

    assert.match(line, /24 hours/);
  });

  it("counts down rather than repeating the day it was created", () => {
    const line = expiryLine(created(), new Date("2026-11-02T09:30:00Z"));

    assert.match(line, /2 hours 30 minutes/);
    assert.doesNotMatch(line, /24 hours/);
  });

  it("says a booth that is over is over rather than counting backwards", () => {
    const line = expiryLine(created(), new Date("2026-11-02T12:00:01Z"));

    assert.match(line, /expired/i);
    assert.doesNotMatch(line, /-/);
  });
});

describe("the OBS sources the front door hands out", () => {
  const atHome = new URL("https://booth.example/home.html");

  it("is every panel the overlay page has, in the overlay's own order", () => {
    assert.deepEqual(
      obsSources(atHome).map((source) => source.name),
      Object.keys(PANELS),
    );
  });

  it("quotes the size the operator has to type into OBS", () => {
    for (const source of obsSources(atHome)) {
      assert.equal(source.size, PANELS[source.name]);
    }
  });

  it("says what each one is for, so the list reads as a menu rather than a parts catalogue", () => {
    for (const source of obsSources(atHome)) {
      assert.ok(source.what?.length > 0, `${source.name} has no purpose`);
    }
  });

  it("never carries a token, because the front door is the page most likely to be shown around", () => {
    for (const source of obsSources(atHome)) {
      assert.match(source.url, /[?&]token=YOUR_VIEW_TOKEN\b/);
    }
  });

  it("points at the booth the page was opened under, not the tournament's", () => {
    const underBooth = new URL("https://booth.example/b/ada/home.html");

    for (const source of obsSources(underBooth)) {
      assert.ok(
        source.url.startsWith("https://booth.example/b/ada/overlay.html"),
        `${source.name} escapes the booth: ${source.url}`,
      );
    }
  });
});

// Read as text: the DOM half is where a localStorage fallback would live, and this page must not have one.
const pageScript = readFileSync(
  fileURLToPath(new URL("../static/home-page.js", import.meta.url)),
  "utf8",
);
const pageLogic = readFileSync(
  fileURLToPath(new URL("../static/home.js", import.meta.url)),
  "utf8",
);

describe("the front door's own files", () => {
  it("never reach for a stored token, so the page cannot leak one", () => {
    for (const [name, source] of [
      ["home-page.js", pageScript],
      ["home.js", pageLogic],
    ]) {
      assert.ok(!source.includes("overlayToken"), `${name} asks for a stored token`);
      assert.ok(!source.includes("window.localStorage"), `${name} reads this browser's storage`);
      assert.doesNotMatch(
        source,
        /localStorage\.(getItem|setItem)|localStorage\[/,
        `${name} touches this browser's storage`,
      );
    }
  });
});

// The page is read as text: there is no build step, so these are the bytes a player reads.
const page = readFileSync(
  fileURLToPath(new URL("../static/home.html", import.meta.url)),
  "utf8",
);

describe("the front door", () => {
  it("says the booth expires after 24 hours before anything is created", () => {
    const beforeTheForm = page.slice(0, page.indexOf("</form>"));

    assert.match(beforeTheForm, /24 hours/);
  });

  it("says a booth is its tokens, because there is no way to recover one", () => {
    assert.match(page, /shown (once|only once)/i);
  });

  it("names every page and the token it is locked behind, because locked is not hidden", () => {
    for (const [path, token] of [
      ["tournament.html", "no token"],
      ["index.html", "view token"],
      ["bracket.html", "view token"],
      ["console.html", "operator token"],
      ["events.html", "view token"],
    ]) {
      const entry = page.match(new RegExp(`<a href="${path}"[\\s\\S]*?</li>`));

      assert.ok(entry, `${path} is not in the directory`);
      assert.match(entry[0], new RegExp(token), `${path} does not name its ${token}`);
    }
  });
});
