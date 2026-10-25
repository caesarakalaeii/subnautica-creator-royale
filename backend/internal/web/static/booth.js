// booth.js works out which booth a page is showing from the path it was opened at.
//
// Pages are served both bare and under `/b/{booth}/`, and must fetch from the booth they were
// served under. The path is the only source: the pages are static files, not templates.

// Must match `boothPrefix` in internal/caster/booth.go and `validBoothID` in internal/config/booths.go.
const PREFIX = "/b/";
const ID = /^[a-z0-9-]{1,40}$/;

/**
 * The route prefix for this page: `/b/{booth}`, or "" for the bare path (the `tournament` booth).
 * An invalid id reads as the bare path, since the service answers an unknown booth with a 401.
 */
export function boothBase(location) {
  const path = location?.pathname ?? "";
  if (!path.startsWith(PREFIX)) return "";

  const id = path.slice(PREFIX.length).split("/")[0];

  return ID.test(id) ? PREFIX + id : "";
}

/** One of the service's routes, under whichever booth this page is on. */
export function boothPath(route, location) {
  return boothBase(location) + route;
}

/**
 * The browser-storage key for a token, scoped to the booth so booths on one origin do not share
 * tokens. The bare key stays the `tournament` booth's.
 */
export function storageKey(name, location) {
  const base = boothBase(location);

  return base === "" ? name : `${name}:${base.slice(PREFIX.length)}`;
}
