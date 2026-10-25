// pods-shape.js draws the pod marker: a semi-transparent square with the top two corners rounded.
//
// The 3D map rasterises this canvas onto its sprite; the 2D map traces the same shape with its own
// path calls, sharing `POD_CORNER_RADIUS`.

/** Top-corner radius as a fraction of the side; the bottom corners stay square. */
export const POD_CORNER_RADIUS = 0.3;

/**
 * Draws the marker onto a square canvas: the pod's colour inside a dark outline so it stays
 * visible against the terrain, translucent so the player behind it shows through.
 */
export function drawPodSurface(context, size, colourCss) {
  const corner = size * POD_CORNER_RADIUS;

  context.clearRect(0, 0, size, size);

  // Outline first and wider than the fill, so the fill sits inside it.
  context.globalAlpha = 0.9;
  context.strokeStyle = "#02070b";
  context.lineWidth = Math.max(2, size * 0.08);
  outlinePath(context, size, corner);
  context.stroke();

  context.globalAlpha = 0.75;
  context.fillStyle = colourCss;
  outlinePath(context, size, corner);
  context.fill();

  context.globalAlpha = 1;
}

function outlinePath(context, size, corner) {
  const inset = context.lineWidth / 2;
  const left = inset;
  const top = inset;
  const right = size - inset;
  const bottom = size - inset;
  const radius = Math.min(corner, (right - left) / 2);

  context.beginPath();
  context.moveTo(left, bottom);
  context.lineTo(left, top + radius);
  context.arcTo(left, top, left + radius, top, radius);
  context.lineTo(right - radius, top);
  context.arcTo(right, top, right, top + radius, radius);
  context.lineTo(right, bottom);
  context.closePath();
}
