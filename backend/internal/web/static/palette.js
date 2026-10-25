// palette.js decides which colour a player is, shared by both maps so they always agree.
//
// Colours are indexed by roster position, not a hash of the name, so a rejoin does not recolour
// a player. Each player's pin and pod share a colour.

import { orderPlayers } from "./view.js";

// Chosen to stay distinct against the teal terrain. Each entry carries a hex number (Three.js
// materials) and a CSS string (2D canvas).
export const PALETTE = [
  { hex: 0xf2b544, css: "#f2b544" }, // amber
  { hex: 0x4dc3e8, css: "#4dc3e8" }, // sky
  { hex: 0xf26d9e, css: "#f26d9e" }, // pink
  { hex: 0x9ee849, css: "#9ee849" }, // lime
  { hex: 0xb285f2, css: "#b285f2" }, // violet
  { hex: 0xf2703d, css: "#f2703d" }, // orange
  { hex: 0x49d6b5, css: "#49d6b5" }, // mint
  { hex: 0xe8e15a, css: "#e8e15a" }, // yellow
];

/**
 * A colour per player name, by seat in the roster as sorted by `orderPlayers` (wire order is
 * ignored). The palette wraps past eight players; the dead keep their seat colour.
 */
export function playerColours(players) {
  if (!players) return new Map();

  const colours = new Map();
  orderPlayers(players).forEach((player, seat) => {
    colours.set(player.name, PALETTE[seat % PALETTE.length]);
  });

  return colours;
}

/** The palette entry for one seat. */
export function seatColour(seat) {
  return PALETTE[seat % PALETTE.length];
}
