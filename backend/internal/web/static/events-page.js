// events-page.js is the DOM half of the events archive: the booth's whole event window, newest
// first, live. The logic lives in events.js and feedview.js.

import { storageKey } from "./booth.js";
import { mergeEvents } from "./events.js";
import { WINDOW_LIMIT, createFeedView } from "./feedview.js";
import { createFeed, liveBase } from "./feed.js";

// Same key the dashboard and bracket use, so a token pasted on one works on all of them.
const VIEW_TOKEN_KEY = storageKey("lms.viewToken", window.location);

const field = (name) => document.querySelector(`[data-field="${name}"]`);

const view = createFeedView({
  list: field("feedList"),
  count: field("feedCount"),
  empty: field("feedEmpty"),
});

let timeline = null;
let feed = null;

// Shows the feed's (state, detail) on the connection pill.
function link(state, detail) {
  const box = field("link");
  box.dataset.state = state;
  box.querySelector(".link__text").textContent = detail || state;
  box.title = detail || state;
}

function draw(document) {
  const before = timeline;
  timeline = mergeEvents(timeline, document.events ?? [], WINDOW_LIMIT);

  if (before === timeline) return;

  view.draw(timeline.entries);
}

function start(value) {
  feed = createFeed({
    base: liveBase(window.location),
    token: value,
    connect: (url) => new WebSocket(url),
    onSnapshot: draw,
    onStatus: link,
  });

  feed.start();
}

/**
 * The view token from a `?token=` link (stored, then stripped from the address bar so it does not
 * show in screen shares), otherwise from browser storage.
 */
function viewToken() {
  const fromLink = new URLSearchParams(window.location.search).get("token");

  if (fromLink) {
    localStorage.setItem(VIEW_TOKEN_KEY, fromLink);
    window.history.replaceState({}, "", window.location.pathname);

    return fromLink;
  }

  return localStorage.getItem(VIEW_TOKEN_KEY) ?? "";
}

function wireViewToken() {
  const box = field("viewToken");
  box.value = viewToken();

  // A feed holds the token it was made with, so a new token means a new feed.
  box.addEventListener("change", () => {
    localStorage.setItem(VIEW_TOKEN_KEY, box.value.trim());
    feed?.stop();
    start(box.value.trim());
  });
}

wireViewToken();
start(viewToken());
