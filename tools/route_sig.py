"""Route identity by what was passed, and on which side.

The owner (2026-09-12): "we need a test to find out when a path was a
winner so we can stop trying it over and over. Make a list of all objects
hit in the path." Two routes are the SAME route when they pass the same
obstacles on the same sides - deformable into each other without crossing
anything, the homotopy class - and a distance test cannot say that: two
flights 20 m apart on opposite sides of a wall read as agreeing. The Tank AI
session built the same test on the ground the same day and its count of
distinct routes agreed with a search that shares no code with it (8 and 8).

OBJECTS are connected components of the navigator's blocked mask at the
planners' 2048 grid, split two ways so a blob says what it is: by KIND
first (the key byte is per texel, so a fence touching the cliff separates
where the kind changes), and every trunk stamp is its own object whatever
canopy it sits in. Render ids from the bake would be better and are asked
for; this is what the key byte gives today.

A SIGNATURE is {object: side} for every object within `reach` metres of the
route, side being which hand the object was on as the route went by: the
sign of (route direction x vector to the object cell), counted over every
cell of the object seen from every sample of the route. An object seen on
both hands in earnest (the route went round it) is "both". The camera also
passes things OVER - the gate flies over foliage under 3 m - but those
cells are not in the blocked mask at all, so over never appears here; for
the camera, a signature is over the things it had to go round.

compare() lists the objects the two routes took on different sides - the
places where they are genuinely different routes - with a point to ring.
"""
import math

import numpy as np
from scipy import ndimage

LEFT, RIGHT, BOTH = "L", "R", "both"


class Objects:
    """The labelled obstacle map: lab (int32, 0 = free), one row per object."""

    def __init__(self, lab, kind_of, centroid, area_m2):
        self.lab = lab
        self.kind_of = kind_of        # object id -> kind byte (0 when unkeyed)
        self.centroid = centroid      # object id -> (row, col)
        self.area_m2 = area_m2        # object id -> square metres
        self.n = len(kind_of)


def label_objects(bake, raw, min_cells=1):
    """Connected components of `raw` (the navigator's blocked mask, the
    bake's grid), split by kind, trunk stamps on their own."""
    lab = np.zeros(raw.shape, dtype=np.int32)
    kind = getattr(bake, "kind", None)
    trunk = getattr(bake, "trunk", None)
    kind_of = {}
    next_id = 1
    groups = []
    if kind is None:
        groups.append((0, raw))
    else:
        for k in np.unique(kind[raw]):
            groups.append((int(k), raw & (kind == k)))
    if trunk is not None:
        # stamps first, so a trunk keeps its identity inside its canopy blob
        t = raw & trunk
        for k, m in groups:
            m &= ~t
        groups.insert(0, (-1, t))
    for k, m in groups:
        if not m.any():
            continue
        l, n = ndimage.label(m, structure=np.ones((3, 3), dtype=bool))
        if n == 0:
            continue
        sizes = ndimage.sum(np.ones_like(l, dtype=np.float32), l, np.arange(1, n + 1))
        keep = np.nonzero(sizes >= min_cells)[0] + 1
        remap = np.zeros(n + 1, dtype=np.int32)
        remap[keep] = np.arange(next_id, next_id + len(keep))
        lab[m] = remap[l[m]]
        for i in keep:
            kind_of[next_id + int(np.where(keep == i)[0][0])] = k
        next_id += len(keep)
    n = next_id - 1
    idx = np.arange(1, n + 1)
    cent = ndimage.center_of_mass(np.ones_like(lab, dtype=np.float32), lab, idx) if n else []
    area = ndimage.sum(np.ones_like(lab, dtype=np.float32), lab, idx) * bake.mx * bake.mx if n else []
    return Objects(lab, kind_of, {i + 1: c for i, c in enumerate(cent)}, {i + 1: float(a) for i, a in enumerate(area)})


def signature(path, objects, bake, reach_m, step_m=None):
    """{object id: side} for the objects within reach_m of the route.

    The route is resampled every step_m (default half the reach) and each
    sample looks at a disc of reach_m; every labelled cell in it votes for
    the hand it lies on relative to the route's direction at that sample.
    """
    if not path or len(path) < 2 or objects.n == 0:
        return {}
    lab = objects.lab
    H, W = lab.shape
    R = max(1, int(round(reach_m / bake.mx)))
    yy, xx = np.ogrid[-R:R + 1, -R:R + 1]
    disc = (xx * xx + yy * yy) <= R * R
    step = step_m or reach_m * 0.5
    votes = {}
    pts = np.asarray([(p[0], p[1]) for p in path], dtype=float)
    seg = np.roll(pts, -1, axis=0) - pts
    seglen = np.hypot(seg[:, 0], seg[:, 1])
    closed = True
    for i in range(len(pts) - (0 if closed else 1)):
        L = seglen[i]
        if L < 1e-6:
            continue
        ux, uz = seg[i] / L
        k = max(1, int(math.ceil(L / step)))
        for j in range(k):
            t = (j + 0.5) / k
            x, z = pts[i] + seg[i] * t
            c, r = bake.texel_of(x, z)
            c, r = int(round(c)), int(round(r))
            if not (R <= c < W - R and R <= r < H - R):
                continue
            win = lab[r - R:r + R + 1, c - R:c + R + 1]
            hit = win[disc]
            if not hit.any():
                continue
            # cell offsets in world axes: +col is +x, +row is -z
            rows, cols = np.nonzero(disc)
            dx = (cols - R) * bake.mx
            dz = -(rows - R) * bake.mz
            cross = ux * dz - uz * dx          # > 0: object on the LEFT of travel
            ids = hit
            for o, cr in zip(ids, cross):
                if o == 0:
                    continue
                v = votes.setdefault(int(o), [0, 0])
                v[0 if cr > 0 else 1] += 1
    sig = {}
    for o, (l, r) in votes.items():
        tot = l + r
        if tot == 0:
            continue
        if min(l, r) > 0.25 * tot:
            sig[o] = BOTH
        else:
            sig[o] = LEFT if l >= r else RIGHT
    return sig


def compare(sig_a, sig_b, objects, bake, min_area_m2=2.0):
    """Objects the two routes took differently. Returns a list of
    (x, z, object id, kind byte, side_a, side_b, area_m2) with (x, z) the
    object's centroid in world metres, largest object first. Objects under
    min_area_m2 (a single dot) are ignored - they are rasteriser noise."""
    out = []
    for o in set(sig_a) | set(sig_b):
        if objects.area_m2.get(o, 0.0) < min_area_m2:
            continue
        a, b = sig_a.get(o), sig_b.get(o)
        if a == b:
            continue
        if a is None or b is None:
            # passed by one route only: near-miss, not a different way round
            continue
        r, c = objects.centroid[o]
        x, z = bake.world_of(c, r)
        out.append((x, z, o, objects.kind_of.get(o, 0), a, b, objects.area_m2[o]))
    out.sort(key=lambda t: -t[6])
    return out


def flipped(sig):
    """The same route flown the other way: every hand swaps. Compare a
    forward route with a reverse one through this, or every object differs."""
    return {o: (RIGHT if s == LEFT else LEFT if s == RIGHT else s) for o, s in sig.items()}


def same_route(sig_a, sig_b, objects, min_area_m2=2.0):
    """True when every object both routes passed was passed on the same side."""
    return all(sig_a[o] == sig_b[o] for o in (set(sig_a) & set(sig_b))
               if objects.area_m2.get(o, 0.0) >= min_area_m2)
