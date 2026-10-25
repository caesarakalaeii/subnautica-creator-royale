// home.js is the front door's pure logic (no DOM): the telemetry YAML to paste, the booth URLs,
// the time left, and the OBS sources.

import { PANELS, overlayURL } from "./overlays.js";

/**
 * The `telemetry:` block for the player's `lms-tournament.yaml`, with an absolute URL built
 * against this page's origin. It never carries a token: the config is shared with every player.
 */
export function telemetryBlock(booth, origin) {
  return [
    "telemetry:",
    `  url: ${absolute(booth.ingestUrl, origin)}`,
    "  interval: 1s",
    "  timeout: 3s",
    "  acceptsCommands: true",
  ].join("\n");
}

/**
 * The booth's absolute URLs. `dashboardWithToken` carries the view token in the query; the
 * dashboard moves it into browser storage on load.
 */
export function boothURLs(booth, origin) {
  const dashboard = absolute(booth.dashboardUrl, origin);

  return {
    dashboard,
    ingest: absolute(booth.ingestUrl, origin),
    dashboardWithToken: `${dashboard}?token=${encodeURIComponent(booth.viewToken)}`,
  };
}

/** How long this booth has left, in words; says so once it has expired. */
export function expiryLine(booth, now) {
  const left = Date.parse(booth.expiresAt) - now.getTime();
  if (!Number.isFinite(left)) return "this booth's expiry is not known";
  if (left <= 0) return "this booth has expired";

  return `this booth expires in ${remaining(left)}`;
}

// One line per panel, keyed by panel name; the home tests assert every panel has one.
const PANEL_PURPOSES = {
  status: "the status strip — what a viewer needs at a glance",
  toasts: "elimination cards, as players go out",
  board: "the match board — who is where, and what it is worth",
  table: "the tournament table, for the gap between matches",
  map: "the solid map — the shot a stream cuts to, full frame",
};

/**
 * Every OBS browser source this booth offers, read from the overlay registry, with a booth-aware
 * address. Never includes a token, since this page is screen-shared.
 */
export function obsSources(location) {
  return Object.entries(PANELS).map(([name, size]) => ({
    name,
    size,
    what: PANEL_PURPOSES[name],
    url: overlayURL(name, location, ""),
  }));
}

/** Hours and minutes; under an hour shows at least "1 minute", never "0 hours". */
function remaining(milliseconds) {
  const minutes = Math.floor(milliseconds / 60000);
  const hours = Math.floor(minutes / 60);

  if (hours === 0) return plural(Math.max(1, minutes), "minute");
  if (minutes % 60 === 0) return plural(hours, "hour");

  return `${plural(hours, "hour")} ${plural(minutes % 60, "minute")}`;
}

function plural(count, word) {
  return `${count} ${word}${count === 1 ? "" : "s"}`;
}

/** A path the service sent, against the origin this page was served from. */
function absolute(path, origin) {
  return new URL(path, origin).toString();
}
