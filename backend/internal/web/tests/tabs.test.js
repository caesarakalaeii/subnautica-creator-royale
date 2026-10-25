import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { describe, it } from "node:test";
import { fileURLToPath } from "node:url";

// The "no page is hidden" rule: every page of a booth is one tab away from every other.
// The pages are read as text.

// The pages the strip is on, and which tab each marks as current. The overlay has no strip: it is composited onto a stream.
const PAGES = {
  "index.html": "view",
  "home.html": "home",
  "tournament.html": "tournament",
  "bracket.html": "bracket",
  "console.html": "operator",
  "events.html": "events",
};

// Every tab, in the order the strip shows them.
const TABS = [
  { tab: "home", href: "home.html" },
  { tab: "tournament", href: "tournament.html" },
  { tab: "view", href: "index.html" },
  { tab: "bracket", href: "bracket.html" },
  { tab: "operator", href: "console.html" },
  { tab: "events", href: "events.html" },
];

function page(name) {
  return readFileSync(fileURLToPath(new URL(`../static/${name}`, import.meta.url)), "utf8");
}

/** The strip's markup, or nothing when the page has none. */
function strip(name) {
  const match = page(name).match(/<nav class="tabs"[^>]*>[\s\S]*?<\/nav>/);

  return match?.[0] ?? "";
}

describe("the tab strip", () => {
  for (const [name, own] of Object.entries(PAGES)) {
    describe(`on ${name}`, () => {
      it("is there, so the page is one click from every other", () => {
        assert.notEqual(strip(name), "", `${name} has no tab strip`);
      });

      it("links to every page, because no page is hidden", () => {
        for (const { href } of TABS) {
          assert.match(strip(name), new RegExp(`href="${href}"`), `${name} does not link to ${href}`);
        }
      });

      it("marks its own tab as the current page", () => {
        const current = [...strip(name).matchAll(/aria-current="page"[^>]*>([^<]+)</g)].map((m) =>
          m[1].trim(),
        );

        assert.deepEqual(current, [own]);
      });

      it("links relatively, so a booth's strip stays inside the booth", () => {
        const hrefs = [...strip(name).matchAll(/href="([^"]+)"/g)].map((m) => m[1]);

        assert.ok(hrefs.length > 0);
        for (const href of hrefs) {
          assert.ok(!href.startsWith("/"), `${name} links absolutely to ${href}`);
        }
      });
    });
  }

  it("is not on the overlay, which is a browser source and has no furniture", () => {
    assert.doesNotMatch(page("overlay.html"), /<nav class="tabs"/);
  });
});
