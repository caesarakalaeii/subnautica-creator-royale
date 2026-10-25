import assert from "node:assert/strict";
import { describe, it } from "node:test";

import { boothBase, boothPath, storageKey } from "../static/booth.js";

describe("which booth a page is on", () => {
  it("is the id in the address, so a page served under one booth asks that booth", () => {
    assert.equal(boothBase({ pathname: "/b/mzxw6ytb/" }), "/b/mzxw6ytb");
    assert.equal(boothBase({ pathname: "/b/mzxw6ytb/bracket.html" }), "/b/mzxw6ytb");
    assert.equal(boothBase({ pathname: "/b/mzxw6ytb/overlay.html" }), "/b/mzxw6ytb");
  });

  it("is nothing at all on a bare path, which is the tournament's own booth", () => {
    assert.equal(boothBase({ pathname: "/" }), "");
    assert.equal(boothBase({ pathname: "/bracket.html" }), "");
    assert.equal(boothBase({ pathname: "/console.html" }), "");
  });

  it("ignores anything that is not a booth id, rather than inventing one", () => {
    // `/b/` with no id, or an id containing a slash, is not a booth: both read as the bare path.
    assert.equal(boothBase({ pathname: "/b/" }), "");
    assert.equal(boothBase({ pathname: "/b" }), "");
    assert.equal(boothBase({ pathname: "/b/NOT AN ID/" }), "");
  });

  it("builds a route under whichever booth the page is on", () => {
    assert.equal(boothPath("/bracket", { pathname: "/b/mzxw6ytb/" }), "/b/mzxw6ytb/bracket");
    assert.equal(boothPath("/bracket", { pathname: "/" }), "/bracket");
  });
});

describe("the key a token is kept under", () => {
  it("is the name itself on a bare path, so the tournament's tokens do not move", () => {
    assert.equal(storageKey("lms.viewToken", { pathname: "/" }), "lms.viewToken");
    assert.equal(storageKey("lms.viewToken", { pathname: "/bracket.html" }), "lms.viewToken");
  });

  it("carries the booth under /b/{booth}/, so two booths never read each other's token", () => {
    assert.equal(
      storageKey("lms.viewToken", { pathname: "/b/mzxw6ytb/" }),
      "lms.viewToken:mzxw6ytb",
    );
    assert.equal(
      storageKey("lms.operatorToken", { pathname: "/b/ada/console.html" }),
      "lms.operatorToken:ada",
    );
  });

  it("is the bare key again for an address that is not a booth, rather than inventing one", () => {
    assert.equal(storageKey("lms.viewToken", { pathname: "/b/" }), "lms.viewToken");
    assert.equal(storageKey("lms.viewToken", { pathname: "/b/NOT AN ID/" }), "lms.viewToken");
  });
});
