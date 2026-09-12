"""Render a found route, with the rings that shaped it, to a PNG.

WHY THIS EXISTS SEPARATELY FROM THE VIEWER. The viewer draws the live search
and the owner watches it; this draws ONE FINISHED ROUTE, zoomed to fit, so it
can be looked at closely - by him, and by me, which is the part that was
missing. I had been shipping drawing changes and asking him whether they were
right. A tool that renders the same thing offscreen means I can check my own
work before it reaches him.

    python tank_tools/draw_path.py [map] [--to X,Z] [--out file.png]

Draws, in order: the ground in nuTerra's own kind palette, the 1 m block layer
over it, then the route - every point, every ring at the radius it actually
grew to, and which hand each tangent took.
"""

import os
import sys

import numpy as np
from PIL import Image, ImageDraw

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from tank_tools import ray_studio as rs


def render(g, squares, pts, rings, out_png, pad_m=40.0, px=1400):
    """Ground, blocks and one route, fitted to the route's own extent."""
    if not pts:
        raise ValueError("no route to draw")

    xs = [p[0] for p in pts] + [r[0] for r in rings]
    zs = [p[1] for p in pts] + [r[1] for r in rings]
    x0, x1 = min(xs) - pad_m, max(xs) + pad_m
    z0, z1 = min(zs) - pad_m, max(zs) + pad_m
    # Square, so nothing is stretched and a ring stays a ring.
    span = max(x1 - x0, z1 - z0)
    cx, cz = (x0 + x1) * 0.5, (z0 + z1) * 0.5
    x0, x1 = cx - span * 0.5, cx + span * 0.5
    z0, z1 = cz - span * 0.5, cz + span * 0.5
    mpp = span / px                      # metres per pixel

    def to_px(x, z):
        return ((x - x0) / mpp, (z1 - z) / mpp)

    # THE GROUND, sampled by WORLD COORDINATE rather than by an integer stride.
    # Striding the texel array is what put the ground 200 m out of step with
    # the block layer the first time this was drawn.
    ix = np.arange(px)
    wx = x0 + (ix + 0.5) * mpp
    wz = z1 - (ix + 0.5) * mpp
    WX, WZ = np.meshgrid(wx, wz)
    tc = ((WX - g["wx0"]) / (g["wx1"] - g["wx0"]) * g["W"]).astype(int)
    tr = ((g["wz1"] - WZ) / (g["wz1"] - g["wz0"]) * g["W"]).astype(int)
    ok = (tc >= 0) & (tc < g["W"]) & (tr >= 0) & (tr < g["W"])
    tc = np.clip(tc, 0, g["W"] - 1)
    tr = np.clip(tr, 0, g["W"] - 1)
    kind = g["kind"][tr, tc]
    img = np.zeros((px, px, 3), dtype=np.uint8)
    for k, c in g["palette"].items():
        img[(kind == k) & ok] = c

    # THE BLOCK LAYER over it, same sampling, so the two cannot drift.
    if squares is not None:
        sr = ((squares.wz1 - WZ) / squares.cell_m).astype(int)
        sc = ((WX - squares.wx0) / squares.cell_m).astype(int)
        inb = (sr >= 0) & (sr < squares.n) & (sc >= 0) & (sc < squares.n)
        sr = np.clip(sr, 0, squares.n - 1)
        sc = np.clip(sc, 0, squares.n - 1)
        was = (squares.base[sr, sc] != 0) & inb
        now = (squares.grid[sr, sc] != 0) & ~was & inb
        img[was] = (img[was] * 0.22).astype(np.uint8)
        img[now] = (230, 60, 60)

    im = Image.fromarray(img)
    d = ImageDraw.Draw(im, "RGBA")

    # THE RINGS FIRST, under the route, at the radius they actually grew to.
    for r in rings:
        rx, rz, rr, side, tx, tz = r[:6]
        c = to_px(rx, rz)
        rp = rr / mpp
        d.ellipse([c[0] - rp, c[1] - rp, c[0] + rp, c[1] + rp],
                  outline=(255, 215, 80, 200), width=2)
        # THE HOP IS FROM THE ANCHOR, not from the ring centre. The centre is
        # where the RAY stopped; the tank is back at the previous point and
        # drives one straight line to the tangent.
        if len(r) >= 8:
            d.line([to_px(r[6], r[7]), to_px(tx, tz)],
                   fill=(120, 255, 170, 220), width=3)
        d.line([c, to_px(tx, tz)], fill=(150, 140, 70, 120), width=1)

    # THE ROUTE over them.
    d.line([to_px(p[0], p[1]) for p in pts], fill=(120, 255, 170), width=4)
    for r in rings:
        rx, rz, rr, side, tx, tz = r[:6]
        t = to_px(tx, tz)
        col = (90, 255, 235) if side > 0 else (255, 150, 90)
        d.ellipse([t[0] - 5, t[1] - 5, t[0] + 5, t[1] + 5], fill=col)
    for p, col, lab in ((pts[0], (0, 220, 255), "START"),
                        (pts[-1], (255, 150, 0), "IN THE BASE RING")):
        q = to_px(p[0], p[1])
        d.ellipse([q[0] - 10, q[1] - 10, q[0] + 10, q[1] + 10],
                  outline=col, width=3)
        d.text((q[0] + 14, q[1] - 6), lab, fill=col)

    length = sum(np.hypot(pts[k + 1][0] - pts[k][0], pts[k + 1][1] - pts[k][1])
                 for k in range(len(pts) - 1))
    d.text((10, 10), "%.0f m   %d points   %d rings   %.0f m across"
           % (length, len(pts), len(rings), span), fill=(255, 255, 255))
    im.save(out_png)
    return length, span


def main():
    map_name = "19_monastery"
    to = None
    out = "route.png"
    for i, a in enumerate(sys.argv):
        if a == "--to" and i + 1 < len(sys.argv):
            to = tuple(float(v) for v in sys.argv[i + 1].split(","))
        if a == "--out" and i + 1 < len(sys.argv):
            out = sys.argv[i + 1]
        if i == 1 and not a.startswith("-"):
            map_name = a

    g = rs.build_grid(map_name, 4.5)
    S = rs.snap_free(g, -20.1, -387.8)
    G = rs.snap_free(g, *(to if to else (0.4, 397.4)))
    try:
        squares = rs.Squares(map_name)
    except Exception:
        squares = None

    t = rs.BranchTree(g, S, G, squares=squares)
    n = 0
    while not t.halted and n < 40000:
        t.step()
        n += 1
    if not t.halted:
        d = min(np.hypot(G[0] - p["pos"][0], G[1] - p["pos"][1]) for p in t.points)
        print("no route in %d steps; closest %.0f m of %.0f"
              % (n, d, np.hypot(G[0] - S[0], G[1] - S[1])))
        return 1
    length, span = render(g, squares, t.win_pts, t.win_rings, out)
    print("route %.0f m, %d points, %d rings, %d casts -> %s (%.0f m across)"
          % (length, len(t.win_pts), len(t.win_rings), t.casts, out, span))
    return 0


if __name__ == "__main__":
    sys.exit(main())
