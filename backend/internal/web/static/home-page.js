// home-page.js is the DOM half of the front door; the logic lives in home.js.
//
// Tokens are never stored (no localStorage, no address bar), so they cannot leak into later
// screen shares of this browser.

import { boothURLs, expiryLine, obsSources, telemetryBlock } from "./home.js";

// The "expires in" line has minute resolution.
const TICK_MS = 60_000;

const field = (name) => document.querySelector(`[data-field="${name}"]`);

function say(text, tone) {
  const box = field("said");
  box.textContent = text;
  box.dataset.tone = tone ?? "";
}

/** Fills in everything a player was handed, once. */
function show(booth) {
  const urls = boothURLs(booth, window.location.origin);

  field("ingestToken").value = booth.ingestToken;
  field("viewToken").value = booth.viewToken;
  field("operatorToken").value = booth.operatorToken;

  field("telemetry").value = telemetryBlock(booth, window.location.origin);
  field("dashboard").value = urls.dashboard;

  const dashboard = field("dashboardLink");
  dashboard.href = urls.dashboardWithToken;

  // The service sends UTC; show it in the reader's locale.
  const dies = new Date(booth.expiresAt);
  field("expiresAt").textContent = dies.toLocaleString();
  field("expiresAt").dateTime = booth.expiresAt;

  const tick = () => {
    field("expiry").textContent = expiryLine(booth, new Date());
  };

  tick();
  window.setInterval(tick, TICK_MS);

  field("made").hidden = false;

  // Hide the form so a second booth cannot replace tokens that have not been copied yet.
  field("createForm").hidden = true;
}

async function createBooth(event) {
  event.preventDefault();

  const button = field("create");
  button.disabled = true;
  say("making a booth…");

  try {
    const response = await fetch("/booths", { method: "POST" });
    const body = await response.json().catch(() => ({}));

    if (!response.ok) {
      say(body.error ?? `the booth could not be made (${response.status})`, "bad");
      button.disabled = false;

      return;
    }

    say("");
    show(body);
  } catch (failure) {
    // The request never completed: a network failure.
    say(`the booth could not be made: ${failure.message}`, "bad");
    button.disabled = false;
  }
}

/**
 * Lists the OBS sources from the overlay registry. Addresses carry the `YOUR_VIEW_TOKEN`
 * placeholder, never a real token, since this page is screen-shared.
 */
function drawSources() {
  field("obsList").replaceChildren(
    ...obsSources(window.location).map((source) => {
      const li = document.createElement("li");
      const url = document.createElement("b");
      url.textContent = source.url;
      li.append(`${source.name} · ${source.size} — ${source.what}: `, url);
      return li;
    }),
  );
}

field("createForm").addEventListener("submit", createBooth);
drawSources();
