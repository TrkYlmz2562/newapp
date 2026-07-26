/**
 * Deterministic generative illustrations for stories without a real image.
 *
 * The tile's colour comes from a CSS gradient behind the canvas; these functions
 * only paint the white/translucent motif on top, so the same drawing works over
 * any category gradient. Everything is seeded by the story slug, so a given story
 * always renders the exact same picture (a fresh random seed would reshuffle it
 * on every scroll).
 */

/** Small, fast string-seeded PRNG (FNV-1a hash → mulberry32). */
export function seededRandom(seed: string): () => number {
  let a = 2166136261;
  for (let i = 0; i < seed.length; i += 1) {
    a ^= seed.charCodeAt(i);
    a = Math.imul(a, 16777619);
  }
  let state = a >>> 0;
  return () => {
    state |= 0;
    state = (state + 0x6d2b79f5) | 0;
    let t = Math.imul(state ^ (state >>> 15), 1 | state);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

/**
 * A circuit-board motif: right-angle traces with pads and vias over a faint grid.
 * White on transparent, sized in CSS pixels (the caller applies the DPR transform).
 */
export function drawCircuit(
  ctx: CanvasRenderingContext2D,
  width: number,
  height: number,
  rand: () => number,
): void {
  const step = Math.max(16, width / 12);

  ctx.strokeStyle = 'rgba(255,255,255,0.09)';
  ctx.lineWidth = 1;
  for (let x = step; x < width; x += step) {
    ctx.beginPath();
    ctx.moveTo(x, 0);
    ctx.lineTo(x, height);
    ctx.stroke();
  }
  for (let y = step; y < height; y += step) {
    ctx.beginPath();
    ctx.moveTo(0, y);
    ctx.lineTo(width, y);
    ctx.stroke();
  }

  const trace = (bright: boolean) => {
    let cx = rand() < 0.5 ? 0 : Math.round(rand() * width);
    let cy = cx === 0 ? Math.round(rand() * height) : 0;
    const steps = 4 + Math.floor(rand() * 4);

    ctx.strokeStyle = bright ? 'rgba(255,255,255,0.85)' : 'rgba(255,255,255,0.4)';
    ctx.lineWidth = bright ? 2 : 1.4;
    ctx.beginPath();
    ctx.moveTo(cx, cy);
    for (let k = 0; k < steps; k += 1) {
      if (k % 2 === 0) {
        cx += step * (1 + Math.floor(rand() * 2));
      } else {
        cy += step * (rand() < 0.5 ? 1 : -1) * (1 + Math.floor(rand() * 2));
      }
      cx = Math.max(5, Math.min(width - 5, cx));
      cy = Math.max(5, Math.min(height - 5, cy));
      ctx.lineTo(cx, cy);
    }
    ctx.stroke();

    // End pad + via ring.
    ctx.fillStyle = bright ? 'rgba(255,255,255,0.95)' : 'rgba(255,255,255,0.6)';
    ctx.fillRect(cx - 2.5, cy - 2.5, 5, 5);
    ctx.beginPath();
    ctx.arc(cx, cy, 4.5, 0, Math.PI * 2);
    ctx.strokeStyle = 'rgba(255,255,255,0.5)';
    ctx.lineWidth = 1;
    ctx.stroke();
  };

  for (let t = 0; t < 7; t += 1) {
    trace(false);
  }
  trace(true);
  trace(true);
}
