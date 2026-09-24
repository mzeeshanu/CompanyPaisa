// Circle packing that grows into a wide oval instead of a circle.
//
// Adapted from d3-hierarchy's packSiblings (front-chain packing, Wang et al. 2006), ISC licence, copyright Mike Bostock.
// The one change is how the next spot is chosen: d3 picks the gap on the pack's edge nearest the middle; here sideways
// distance counts for less (divided by `stretch`), so the pack grows sideways. Circles never overlap, and it is fast
// enough for thousands (3,800 circles in about 50 ms), unlike a force simulation.

export interface PackCircle { r: number; x: number; y: number }

interface ChainNode { c: PackCircle; next: ChainNode; previous: ChainNode }

/**
 * Places the circles (their `r` is read, `x` and `y` are written) touching, biggest first when they're sorted that way,
 * in an oval about `stretch` times wider than tall, centred on 0,0.
 */
export function packWide(circles: PackCircle[], stretch: number): void {
  const n = circles.length;
  if (n === 0) return;
  const e2 = stretch * stretch;
  const score = (node: ChainNode) => {
    const a = node.c, b = node.next.c, ab = a.r + b.r;
    const dx = (a.x * b.r + b.x * a.r) / ab, dy = (a.y * b.r + b.y * a.r) / ab;
    return dx * dx / e2 + dy * dy;
  };

  const first = circles[0];
  first.x = 0; first.y = 0;
  if (n < 2) return;
  const second = circles[1];
  first.x = -second.r; second.x = first.r; second.y = 0;
  if (n < 3) { centre(circles); return; }
  place(second, first, circles[2]);

  const node = (c: PackCircle) => ({ c } as ChainNode);
  let a = node(first), b = node(second), c = node(circles[2]);
  a.next = c.previous = b;
  b.next = a.previous = c;
  c.next = b.previous = a;

  pack: for (let i = 3; i < n; ++i) {
    place(a.c, b.c, circles[i]);
    c = node(circles[i]);
    // Walk the chain both ways from a–b; if the new circle overlaps one, that one becomes a or b and we try again.
    let j = b.next, k = a.previous, sj = b.c.r, sk = a.c.r;
    do {
      if (sj <= sk) {
        if (intersects(j.c, c.c)) { b = j; a.next = b; b.previous = a; --i; continue pack; }
        sj += j.c.r; j = j.next;
      } else {
        if (intersects(k.c, c.c)) { a = k; a.next = b; b.previous = a; --i; continue pack; }
        sk += k.c.r; k = k.previous;
      }
    } while (j !== k.next);

    // It fits: insert it into the chain, then continue from the gap nearest the middle (on the stretched scale).
    c.previous = a; c.next = b; a.next = b.previous = b = c;
    let best = score(a);
    while ((c = c.next) !== b) {
      const s = score(c);
      if (s < best) { a = c; best = s; }
    }
    b = a.next;
  }
  centre(circles);
}

/** Puts circle c touching both a and b. */
function place(b: PackCircle, a: PackCircle, c: PackCircle) {
  const dx = b.x - a.x, dy = b.y - a.y, d2 = dx * dx + dy * dy;
  if (!d2) { c.x = a.x + c.r; c.y = a.y; return; }
  let a2 = a.r + c.r; a2 *= a2;
  let b2 = b.r + c.r; b2 *= b2;
  if (a2 > b2) {
    const x = (d2 + b2 - a2) / (2 * d2), y = Math.sqrt(Math.max(0, b2 / d2 - x * x));
    c.x = b.x - x * dx - y * dy;
    c.y = b.y - x * dy + y * dx;
  } else {
    const x = (d2 + a2 - b2) / (2 * d2), y = Math.sqrt(Math.max(0, a2 / d2 - x * x));
    c.x = a.x + x * dx - y * dy;
    c.y = a.y + x * dy + y * dx;
  }
}

function intersects(a: PackCircle, b: PackCircle) {
  const dr = a.r + b.r - 1e-6, dx = b.x - a.x, dy = b.y - a.y;
  return dr > 0 && dr * dr > dx * dx + dy * dy;
}

/** Moves the pack so its bounding box is centred on 0,0. */
function centre(circles: PackCircle[]) {
  let left = Infinity, right = -Infinity, top = Infinity, bottom = -Infinity;
  for (const c of circles) {
    left = Math.min(left, c.x - c.r); right = Math.max(right, c.x + c.r);
    top = Math.min(top, c.y - c.r); bottom = Math.max(bottom, c.y + c.r);
  }
  const mx = (left + right) / 2, my = (top + bottom) / 2;
  for (const c of circles) { c.x -= mx; c.y -= my; }
}
