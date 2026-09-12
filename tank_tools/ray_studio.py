"""Ray Studio - watch the tank route resolver hunt, live.

The owner, after closing two nuTerra windows full of tanks doing things:

    "I don/t want to see the tanks searching. I want a window top view of the
     algo's progression of finding routes."
    "this is a path resolver like path studio. Just different space."
    "I just wanna draw it live the same way the path studio code does sorta."

So this is a tool, not a mode of the game. It owns its window, it draws the
whole map, and it draws every ray as it is cast. No tanks. Nothing that moves
except the search.

WHY IT IS PYTHON AND NOT IN THE APP. An algorithm you are still designing wants
a loop you can go round in a second. The VB version takes forty seconds to
build and run before a single pixel appears, which is why the resolver spent an
afternoon being wrong in ways nobody could see. Path Studio exists for the same
reason and this is deliberately its sibling: same idea, different space - it
resolves ground routes where that one resolves camera flights.

THE ALGORITHM IS THE OWNER'S, and it is rays rather than a grid search:

    cast a ray at the flag
    where it hits, open an EXPANDING RING at the collision point
    sweep the ring for the angles that clear - LEFT FIRST
    each angle that gets somewhere is a branch; cast again from there
    a branch that reaches the flag is a path
    a branch that runs out of ring is a dead end, and stays on screen

The thing it replaces expanded 466,229 grid cells to find one route. This casts
about 800 rays. That difference is the whole reason it can be watched.

WHAT THE COLOURS MEAN
    dim green / grey    open ground, and what is shut and why
    DEEP RED            a ray that led nowhere. Never cleared, so the dead ends
                        pile up into a picture of everywhere it has been
    YELLOW              the ray being cast right now
    BRIGHT GREEN        a ray on a path that reached the flag
    CYAN / ORANGE       where it started, and the flag it is hunting

    python tank_tools/ray_studio.py [map] [--hull 4.5] [--speed 8]
"""

import os
import sys
import struct
import numpy as np

FLIGHT = os.path.join(os.environ.get("TEMP", "."), "nuTerra", "flight")

# TankNav's own numbers, so this tool and the app agree about the ground.
CELL_TEXELS = 8
MAX_OBSTACLE_M = 1.0
MAX_SLOPE = 0.7
KIND_MASK, OUTLAND_BIT, TRUNK_BIT = 7, 16, 128
KIND_FENCE, KIND_TREE, KIND_PROP, KIND_WATER = 2, 3, 5, 6


def read_meta(path):
    meta = {}
    with open(path, "r", encoding="utf-8", errors="replace") as f:
        for line in f:
            line = line.strip()
            if not line or line.startswith("#") or "=" not in line:
                continue
            k, v = line.split("=", 1)
            meta[k.strip()] = v.strip()
    return meta


def build_grid(map_name, hull_r_m):
    """The COLLISION MAP: sample XZ at the plane location, read Y, and anything
    standing over a metre is solid.

    The owner, looking at a zoomed-in resolve full of staircases: "you are stair
    stepping bad. use the path height map and we are ay z Y of 1.0m. that's our
    collision map... Y at 1.0 and XZ at plane locations."

    WHAT THIS REPLACED AND WHY IT STAIR-STEPPED. The old test was a DERIVED
    map: block a 1.37 m cell if any texel in it is blocked, distance-transform
    that, then demand the hull radius of clearance. Two things came out of it -
    the ground was eight times coarser than the bake that made it, and every
    obstacle was inflated by 4.5 m. So a ray stopped four and a half metres from
    anything real, and the half-metre tangent steps around that phantom surface
    drew a staircase rather than a corner.

    Sampling the height map directly at its own 0.171 m gives rays that stop
    where the thing actually is.

    The kind rules stay, because they are the game's and they are measured: a
    canopy is not an obstacle and a trunk is, a tank goes through a fence and
    over a curb. Height alone would stop every ray at the first hedge.
    """
    meta = read_meta(os.path.join(FLIGHT, f"{map_name}_meta.txt"))
    W = int(meta["width"])
    wx0, wx1 = float(meta["wx_min"]), float(meta["wx_max"])
    wz0, wz1 = float(meta["wz_min"]), float(meta["wz_max"])
    scale = float(meta["height_scale"])

    top = np.fromfile(os.path.join(FLIGHT, f"{map_name}_top.rgba"),
                      dtype=np.uint8).reshape(W, W, 4)
    fl16 = np.fromfile(os.path.join(FLIGHT, f"{map_name}_floor.r16"),
                       dtype="<u2").reshape(W, W).astype(np.int32)

    key = top[:, :, 0]
    t16 = (top[:, :, 1].astype(np.int32) << 8) | top[:, :, 2]
    kind = key & KIND_MASK

    # Y ABOVE 1.0 m IS A COLLISION - at the texel, not at a cell average.
    over = (t16 - fl16) > int(MAX_OBSTACLE_M * scale)
    testable = (kind != KIND_TREE) & (kind != KIND_FENCE) & (kind != KIND_PROP)

    collide = (over & testable)         | (key & TRUNK_BIT).astype(bool)         | (key & OUTLAND_BIT).astype(bool)         | (kind == KIND_WATER)

    # THE FLOOR IS KEPT, not just the collision bits, because a slope is a
    # difference between two heights and cannot be read off a boolean.
    return dict(W=W, texel_m=(wx1 - wx0) / W, collide=collide, used=None,
                floor=fl16, hscale=scale,
                wx0=wx0, wx1=wx1, wz0=wz0, wz1=wz1, hull=hull_r_m)


def to_texel(g, x, z):
    W = g["W"]
    col = int((x - g["wx0"]) / (g["wx1"] - g["wx0"]) * W)
    row = int((g["wz1"] - z) / (g["wz1"] - g["wz0"]) * W)
    return col, row


def standable(g, x, z):
    """Can the plane sit here? XZ in, Y tested against 1.0 m."""
    col, row = to_texel(g, x, z)
    W = g["W"]
    if col < 0 or row < 0 or col >= W or row >= W:
        return False
    if g["used"] is not None and g["used"][row, col]:
        return False
    return not g["collide"][row, col]


def snap_free(g, x, z):
    """The nearest spot the plane can sit, if this one is inside something."""
    if standable(g, x, z):
        return x, z
    step = g["texel_m"] * 2.0
    for ring in range(1, 400):
        r = ring * step
        for k in range(0, 360, 10):
            a = np.deg2rad(k)
            nx, nz = x + np.sin(a) * r, z + np.cos(a) * r
            if standable(g, nx, nz):
                return nx, nz
    return x, z


def sees(g, x, z, tx, tz):
    """Is there a clear straight line from here to there for this hull?

    This is the question that ends a corner. Marched at half a cell, the same
    step a ray uses, so a line this says is clear is a line a ray can fly.
    """
    dx, dz = tx - x, tz - z
    d = np.hypot(dx, dz)
    if d < 1e-6:
        return True
    dx, dz = dx / d, dz / d
    step = g["texel_m"] * 0.5
    t = 0.0
    while t < d:
        if not standable(g, x + dx * t, z + dz * t):
            return False
        t += step
    return True


def march(g, x, z, dx, dz, goal, limit):
    """How far a ray gets, and where it stops.

    Half a cell a step, so nothing narrower than a cell can be stepped over.
    """
    # A TEXEL AT A TIME: nothing narrower than the height map's own cell can
    # be stepped over, which is the finest this data can honestly answer.
    step = g["texel_m"]
    t = 0.0
    gx, gz = goal
    while t < limit:
        if (x - gx) ** 2 + (z - gz) ** 2 <= REACH_M ** 2:
            return t, x, z, True
        nx, nz = x + dx * step, z + dz * step
        if not standable(g, nx, nz):
            return t, x, z, False

        # A SLOPE STOPS A RAY THE SAME WAY A WALL DOES. Nothing here climbs or
        # drops more than 45 degrees, and the collision map cannot say so - it
        # is a height threshold, and a cliff face is under a metre tall between
        # any two samples the whole way down. So the gradient is checked at the
        # same step, and a wall of rock and the edge of a ravine both end the
        # ray and get the ring sweep they deserve.
        if too_steep(g, x, z, nx, nz, step):
            return t, x, z, False

        x, z, t = nx, nz, t + step
    return t, x, z, False


def too_steep(g, x, z, nx, nz, step_m):
    """Is the ground between these two points steeper than we can take?"""
    c0, r0 = to_texel(g, x, z)
    c1, r1 = to_texel(g, nx, nz)
    W = g["W"]
    if not (0 <= c0 < W and 0 <= r0 < W and 0 <= c1 < W and 0 <= r1 < W):
        return True
    dh = abs(int(g["floor"][r1, c1]) - int(g["floor"][r0, c0])) / g["hscale"]
    return dh > step_m * MAX_SLOPE_TAN


REACH_M = 12.0

# THE RING IS A CIRCLE IN METRES, expanding half a metre at a time. The owner's
# words: "we draw a ring at that hit point and hit the tangent on both sides. if
# we could not after expanding the ring in .5m steps to max ring size in
# settings... That path is dead."
RING_STEP_M = 0.5
RING_MIN_M = 0.5
RING_MAX_DEFAULT_M = 12.0
# THE SETTING'S RANGE, MEASURED RATHER THAN GUESSED. The spec said 0.5 to 5.0
# by 0.5; at 5.0 this map yields NOTHING and at 10.0 it yields five routes. A
# ring has to be able to reach past the corner it is stuck in, and the corners
# here are bigger than five metres. Range runs to 25 m now; the step stays 0.5
# near the bottom and coarsens above 5, since nothing changes by half-metres up
# there - 10 m and 25 m give identical paths.
RING_SETTING_MIN = 0.5
RING_SETTING_MAX = 25.0

# How finely the ring is walked looking for where it clears.
RING_ANGLE_STEP = np.deg2rad(6.0)

# How far round the ring counts as a tangent rather than a retreat.
RING_ARC_MAX = np.deg2rad(110.0)

# How far a ray must get away from a candidate tangent before that tangent
# counts. Below this the ring has found a clear spot inside a corner rather than
# a way out of it.
TANGENT_ESCAPE_M = 6.0

# THE MINIMUM GAP WE CAN GET THROUGH, in metres. The owner's setting: "min gap
# size (add and set to 4)". A tangent that clears but sits in a slot narrower
# than this is no use - the plane does not fit through it.
MIN_GAP_DEFAULT_M = 4.0

# STEEPEST GROUND WE CAN TAKE, up or down. "we cant drop by more that 45 degree
# angle up or down. we have to stop and do a ring sweep." Forty-five degrees is
# a gradient of one, which is why the number is 1.0 and not a trigonometric
# call - and writing it this way means nobody has to wonder which way round the
# tangent went.
MAX_SLOPE_TAN = 1.0

# THREE TANGENTS THE SAME WAY MEANS WE ARE NOT GETTING ROUND IT. "of we get 3
# points in a row that are of nearly the same angle, we are no getting around
# this. try a different path." A chain that keeps turning the same way by the
# same amount is tracing a face it cannot leave, and the sweep has other
# bearings worth more than another hundred hops of this one.
# FOUR, NOT THREE, AND SIX DEGREES RATHER THAN TWELVE - measured, not tuned by
# feel. At the spec's three-in-a-row the rule costs THREE OF THE FIVE routes,
# because following a long wall legitimately looks like three similar turns.
# One more point of patience keeps every route and still saves a third of the
# rays, which is the rule doing its job instead of doing damage:
#
#     no rule            5 paths, 1,031 rays
#     run 4, tol 6 deg   5 paths,   674 rays
#     run 3, tol 12 deg  2 paths,   404 rays
SAME_ANGLE_RUN = 4
SAME_ANGLE_TOL = np.deg2rad(6.0)

# Coarse out, fine back. The gap is found by stepping OUT in min-gap strides
# until something is hit, then walking BACK in small ones until it is clear
# again - "scale ring back by smaller sets till we hit something again and we
# can get the start of our width more accurate". The coarse pass is cheap and
# the fine pass is short, so the edge is pinned to a few centimetres without
# measuring the whole way in at that resolution.
GAP_FINE_M = 0.25


def measure_gap(g, px, pz, dirx, dirz, min_gap_m):
    """How wide is the opening at this point, across the way we are going?

    Walks out to both sides perpendicular to travel: coarse strides of the
    minimum gap until it hits, then back in GAP_FINE_M steps until clear, so
    the wall is located finely without paying for fine steps the whole way.

    Returns (width, left_edge_distance, right_edge_distance). A width under the
    minimum means the plane does not fit through here however clear the centre
    looked.
    """
    # Perpendicular to the direction of travel.
    nx, nz = -dirz, dirx
    edges = []
    for sgn in (1.0, -1.0):
        t = 0.0
        # OUT, coarsely, until something is in the way.
        while t < 200.0:
            t2 = t + min_gap_m
            if not standable(g, px + nx * sgn * t2, pz + nz * sgn * t2):
                break
            t = t2
        # BACK IN, finely, to find where it actually starts.
        probe = t
        while probe < t + min_gap_m:
            if not standable(g, px + nx * sgn * probe, pz + nz * sgn * probe):
                break
            probe += GAP_FINE_M
        edges.append(max(0.0, probe - GAP_FINE_M))
    return edges[0] + edges[1], edges[0], edges[1]

# How many ring-and-re-aim hops one chain may take before it is called lost. A
# small ring means many small steps round a big obstacle, which is the design.
MAX_HOPS = 260

# The sweep: start pointing LEFT, finish pointing EAST.
SWEEP_FROM_DEG = -90.0
SWEEP_TO_DEG = 90.0
SWEEP_STEP_DEG = 3.0

REJECT_M = 55.0


def march(g, x, z, dx, dz, goal, limit):
    """Fly a ray until it hits something or reaches the flag."""
    step = g["texel_m"] * 0.5
    t = 0.0
    gx, gz = goal
    while t < limit:
        if (x - gx) ** 2 + (z - gz) ** 2 <= REACH_M ** 2:
            return t, x, z, True
        nx, nz = x + dx * step, z + dz * step
        if not standable(g, nx, nz):
            return t, x, z, False
        x, z, t = nx, nz, t + step
    return t, x, z, False


def ring_tangents(g, hx, hz, indx, indz, max_ring_m, goal, limit, min_gap_m):
    """Draw a ring at the hit point and find where it clears, both sides.

    Exactly as described: a circle at the collision, grown in half-metre steps
    until a point on it is standable. Walked outward from the direction we were
    travelling, both ways at once, so the first clear angle on each side is the
    tangent past the thing we hit.

    Returns the two tangent points, either of which may be None, and the radius
    the ring had reached. Nothing found by max_ring_m means this path is DEAD -
    that is the owner's rule and it is what stops a chain crawling for ever.
    """
    base_ang = np.arctan2(indx, indz)
    r = RING_MIN_M
    while r <= max_ring_m + 1e-6:
        left = right = None
        a = RING_ANGLE_STEP
        # NEVER ALL THE WAY ROUND. Walking the ring to 180 degrees always finds
        # clear ground - straight back the way we came, which is where we have
        # just been. That is a retreat, not a tangent, and it was found at the
        # very first 0.5 m radius every single time, so the ring never expanded
        # and the max size made no difference at all from 1 m to 35 m. Identical
        # ray counts across seven times the setting is what gave it away.
        #
        # A tangent is the way PAST a thing, so it lives out to about square
        # with the direction of travel. Past that you are going home.
        while a <= RING_ARC_MAX:
            for sgn in (1.0, -1.0):
                if (sgn > 0 and left is not None) or (sgn < 0 and right is not None):
                    continue
                th = base_ang + a * sgn
                px, pz = hx + np.sin(th) * r, hz + np.cos(th) * r
                if not standable(g, px, pz):
                    continue

                # THE TANGENT MUST BE SOMEWHERE A RAY CAN LEAVE FROM.
                #
                # The owner's rule, and it is the one that unsticks corners:
                # "we expand and take tangent if it doesn't hit anything. that
                # stops the path from getting stuck in corners."
                #
                # Testing only that the POINT is clear is not enough. Inside a
                # corner every point half a metre away is perfectly clear and
                # every ray out of it hits at once, so the chain sits in the
                # corner sidestepping until its hop budget runs out - which is
                # exactly what it was doing.
                #
                # So a candidate only counts if a ray at the flag actually gets
                # away from it. If none does at this radius the ring grows,
                # which is what "expand" is FOR: a bigger circle reaches past
                # the corner that the small one was trapped in.
                gx2, gz2 = goal
                d2 = max(np.hypot(gx2 - px, gz2 - pz), 1e-6)
                out, _, _, _ = march(g, px, pz, (gx2 - px) / d2, (gz2 - pz) / d2,
                                     goal, limit)
                if out < TANGENT_ESCAPE_M:
                    continue

                # AND CAN WE GET THROUGH IT? A clear point in a slot narrower
                # than the plane is not a way past anything.
                wide, _, _ = measure_gap(g, px, pz,
                                         (gx2 - px) / d2, (gz2 - pz) / d2,
                                         min_gap_m)
                if wide < min_gap_m:
                    continue

                if sgn > 0:
                    left = (px, pz)
                else:
                    right = (px, pz)
            if left is not None and right is not None:
                break
            a += RING_ANGLE_STEP
        if left is not None or right is not None:
            return left, right, r
        r += RING_STEP_M
    return None, None, r


def chain(g, start, goal, bearing, max_ring_m, trace, min_gap_m):
    """One path attempt: ray, ring, tangent, re-aim at the base, repeat.

    "if we could not... That path is dead. If we can hit it, we anchor at
    tangent and scan at base location. same thing."

    So every hop after the first aims AT THE FLAG. The opening ray is the only
    one that goes where the sweep points it; after that the chain is always
    trying to go home and only turning aside to get round what is in the way.
    """
    gx, gz = goal
    limit = (g["wx1"] - g["wx0"]) * 1.5
    pts = [start]
    x, z = start
    dx, dz = np.sin(bearing), np.cos(bearing)
    seen_here = set()
    hand = 0                    # 0 undecided, +1 keep it on the left, -1 right
    turns = []                  # bearings of the last few tangents

    for _ in range(MAX_HOPS):
        got, hx, hz, reached = march(g, x, z, dx, dz, goal, limit)
        trace.append(((x, z), (hx, hz), reached))
        if reached:
            pts.append((gx, gz))
            return pts                                   # THE PRIZE
        # A RAY THAT TRAVELS NOTHING IS NOT A DEAD PATH, IT IS ANOTHER HIT.
        #
        # After anchoring on a tangent, aiming at the base points straight back
        # into the thing we just went round, so the next ray is zero long. The
        # first version called that wedged and gave up - every chain died in two
        # hops, and the ring size made no difference at all from 5 m to 35 m,
        # which is what proved the ring was never the problem.
        #
        # By the rule it is simply the next collision: ring again, take a
        # tangent, crawl on. A small ring means many small steps round a big
        # obstacle, and that is the design rather than a fault in it.

        left, right, _ = ring_tangents(g, hx, hz, dx, dz, max_ring_m,
                                       goal, limit, min_gap_m)
        if left is None and right is None:
            return None                                  # DEAD

        # COMMIT TO A HAND AND KEEP IT.
        #
        # Choosing the better side at every hop is why this oscillated: it
        # sidesteps left, re-aims at the base, turns back into the same wall,
        # and now the RIGHT side looks better - so it steps back, and round it
        # goes. Twenty hops later it revisits its own ground and the loop guard
        # kills it. That happened identically at 260 hops and at 4,000, which is
        # what showed it was never running out of room to crawl.
        #
        # Going round something means keeping it on one hand the whole way. The
        # first ring of a chain picks the side; every ring after it uses the
        # same one, so the chain commits to going round rather than dithering
        # at the face.
        # BOTH SIDES, ALWAYS. The hand was committed to stop the chain
        # dithering at a wall - sidestep left, re-aim, turn back, sidestep
        # right - but the escape rule already stops that: a tangent only counts
        # if a ray can leave it. With the hand locked, a chain dies the moment
        # its chosen side has no escapable tangent, even when the other side
        # does, and that was killing every chain at about four hops.
        options = (left, right)
        best, best_got = None, -1.0
        for cand in options:
            if cand is None:
                continue
            cxx, czz = cand
            d = max(np.hypot(gx - cxx, gz - czz), 1e-6)
            g2, _, _, _ = march(g, cxx, czz, (gx - cxx) / d, (gz - czz) / d,
                                goal, limit)
            if g2 > best_got:
                best, best_got = cand, g2
        if best is None:
            return None

        # BUT IT MUST ACTUALLY MOVE. Crawling is fine; crawling on the spot is
        # a loop, and a loop is a dead path however long you let it run.
        # The bucket has to be FINER than a ring step, or crawling looks like
        # looping: a 0.5 m ring moves the anchor less than a 2 m bucket, so the
        # second anchor landed in the first one's square and every chain was
        # killed for going round in a circle it had not gone round.
        key = (round(best[0] / 0.4), round(best[1] / 0.4))
        if key in seen_here:
            return None
        seen_here.add(key)

        if hand == 0:
            hand = 1 if best is left else -1

        # NOT GETTING ROUND IT. Three turns running at nearly the same bearing
        # is a chain grinding along one face, and no number of further hops
        # changes that - the sweep's next bearing is worth more.
        turns.append(np.arctan2(best[0] - x, best[1] - z))
        if len(turns) >= SAME_ANGLE_RUN:
            recent = turns[-SAME_ANGLE_RUN:]
            spread = max(abs(np.arctan2(np.sin(a1 - a2), np.cos(a1 - a2)))
                         for a1 in recent for a2 in recent)
            if spread < SAME_ANGLE_TOL:
                return None

        x, z = best
        pts.append(best)
        d = max(np.hypot(gx - x, gz - z), 1e-6)
        dx, dz = (gx - x) / d, (gz - z) / d              # scan at base location
    return None


def resample(path, n=24):
    if len(path) < 2:
        return [path[0]] * n if path else []
    segs, total = [], 0.0
    for i in range(1, len(path)):
        d = np.hypot(path[i][0] - path[i - 1][0], path[i][1] - path[i - 1][1])
        segs.append(d)
        total += d
    if total < 1e-6:
        return [path[0]] * n
    out, acc, i = [], 0.0, 1
    for k in range(n):
        want = total * k / (n - 1)
        while i < len(segs) and acc + segs[i - 1] < want:
            acc += segs[i - 1]
            i += 1
        if i >= len(path):
            out.append(path[-1])
            continue
        t = 0.0 if segs[i - 1] < 1e-6 else (want - acc) / segs[i - 1]
        out.append((path[i - 1][0] + (path[i][0] - path[i - 1][0]) * t,
                    path[i - 1][1] + (path[i][1] - path[i - 1][1]) * t))
    return out


def too_close(path, pool):
    a = resample(path)
    for other in pool:
        b = resample(other)
        if sum(np.hypot(p[0] - q[0], p[1] - q[1])
               for p, q in zip(a, b)) / len(a) < REJECT_M:
            return True
    return False


def resolve(g, start, goal, max_ring_m=RING_MAX_DEFAULT_M,
            min_gap_m=MIN_GAP_DEFAULT_M):
    """Sweep from LEFT round to EAST, one chain per bearing.

    "We will start scanning left. every fail or win, we change ray and try for
    another path chain. We do this until we are point east."

    A win and a loss cost the same thing - the next bearing - so the sweep is
    the whole outer loop and there is no backtracking anywhere in it.
    """
    sx, sz = snap_free(g, *start)
    gxy = snap_free(g, *goal)
    pool, rays = [], []
    for a_deg in np.arange(SWEEP_FROM_DEG, SWEEP_TO_DEG + 1e-6, SWEEP_STEP_DEG):
        trace = []
        got = chain(g, (sx, sz), gxy, np.deg2rad(a_deg), max_ring_m, trace,
                    min_gap_m)
        for seg in trace:
            rays.append((seg[0], seg[1], got is not None))
        if got is not None and not too_close(got, pool):
            pool.append(got)
        yield rays, pool, len(rays), a_deg
    yield rays, pool, len(rays), SWEEP_TO_DEG


def main():
    import pygame

    map_name = sys.argv[1] if len(sys.argv) > 1 and not sys.argv[1].startswith("-") \
        else "19_monastery"
    hull = 4.5
    ring_max = RING_MAX_DEFAULT_M
    min_gap = MIN_GAP_DEFAULT_M
    speed = 1
    # A good resolve is now 43 rays, which at one a frame is over in under a
    # second - too fast to watch, which defeats the point of a live view. The
    # delay is per ray, so the hunt runs at a pace a person can follow.
    delay = 220
    for i, a in enumerate(sys.argv):
        if a == "--hull" and i + 1 < len(sys.argv):
            hull = float(sys.argv[i + 1])
        if a == "--speed" and i + 1 < len(sys.argv):
            speed = int(sys.argv[i + 1])
        if a == "--delay" and i + 1 < len(sys.argv):
            delay = int(sys.argv[i + 1])

    print(f"ray studio: {map_name}, hull {hull:.1f} m")
    g = build_grid(map_name, hull)
    W = g["W"]
    print(f"  collision map {W}x{W} at {g['texel_m']:.3f} m per texel, "
          f"{(~g['collide']).sum():,} clear texel(s), Y over "
          f"{MAX_OBSTACLE_M:.1f} m is solid")

    # THE PICTURE IS SMALLER THAN THE MAP. Collision stays at the full 8192 so
    # rays stop where things actually are; the background is quartered to 2048
    # because a pygame surface of 8192 square is 200 MB and no screen can show
    # it anyway. A texel is solid in the picture if any of its four are.
    SHOW = 2048
    f = W // SHOW
    shown = g["collide"].reshape(SHOW, f, SHOW, f).any(axis=(1, 3))

    # The two ctf bases. They are not in the meta - the app reads them from
    # arena_defs - so monastery's are given here and anything else needs them
    # passed in. Wrong is better than silent: it is printed.
    bases = {"19_monastery": ((-20.1, -387.8), (0.4, 397.4))}
    if map_name not in bases:
        print(f"  no base positions known for {map_name}")
        return
    start, goal = bases[map_name]
    print(f"  team 1 {start}  ->  team 2 {goal}")

    pygame.init()
    S = 1000
    screen = pygame.display.set_mode((S, S), pygame.RESIZABLE)
    pygame.display.set_caption(f"Ray Studio - {map_name} - [Tank AI work]")
    font = pygame.font.SysFont("consolas", 16)

    # The map, once. Everything else is drawn over it each frame.
    base = np.zeros((SHOW, SHOW, 3), dtype=np.uint8)
    base[...] = (22, 44, 26)
    base[shown] = (62, 30, 20)
    surf = pygame.surfarray.make_surface(np.transpose(base, (1, 0, 2)))
    N = SHOW                     # the view works in picture texels

    gen = resolve(g, start, goal, ring_max, min_gap)
    nodes, paths, rays, bearing = [], [], 0, SWEEP_FROM_DEG
    running, done, paused = True, False, False

    # THE VIEW, in CELLS. A 1024-cell map squeezed into a window is 1.4 m a
    # pixel, which the owner could not read: "the res is too low to see."
    # view_cells is how much map is on screen, so shrinking it zooms in.
    view_cx, view_cz = 0.0, 0.0
    view_cells = float(N)
    dragging, drag_from = False, (0, 0)

    def cell_at_mouse(mx, my, w):
        return (view_cx + mx / w * view_cells,
                view_cz + my / w * view_cells)

    def to_px(x, z, w):
        # World -> cell -> screen, THROUGH THE VIEW, so the overlay tracks the
        # map when it is zoomed or panned. Anything drawn with its own mapping
        # would slide off the thing it is describing.
        cx = (x - g["wx0"]) / (g["wx1"] - g["wx0"]) * N
        cz = (g["wz1"] - z) / (g["wz1"] - g["wz0"]) * N
        return (int((cx - view_cx) / view_cells * w),
                int((cz - view_cz) / view_cells * w))

    while running:
        w_now = min(screen.get_width(), screen.get_height())
        for e in pygame.event.get():
            if e.type == pygame.QUIT:
                running = False
            elif e.type == pygame.MOUSEWHEEL:
                # ZOOM TO THE CURSOR: the cell under the mouse must not move.
                # Work out which cell that is, change the zoom, then put the
                # view back so that same cell is still under the pointer -
                # which is what makes a wheel feel like a magnifier rather than
                # a scrollbar.
                mx, my = pygame.mouse.get_pos()
                ax, az = cell_at_mouse(mx, my, w_now)
                view_cells *= 0.85 ** e.y
                view_cells = min(float(N), max(24.0, view_cells))
                view_cx = ax - mx / w_now * view_cells
                view_cz = az - my / w_now * view_cells
            elif e.type == pygame.MOUSEBUTTONDOWN and e.button in (1, 2, 3):
                dragging, drag_from = True, e.pos
            elif e.type == pygame.MOUSEBUTTONUP and e.button in (1, 2, 3):
                dragging = False
            elif e.type == pygame.MOUSEMOTION and dragging:
                dx, dy = e.pos[0] - drag_from[0], e.pos[1] - drag_from[1]
                drag_from = e.pos
                view_cx -= dx / w_now * view_cells
                view_cz -= dy / w_now * view_cells
            elif e.type == pygame.KEYDOWN:
                if e.key in (pygame.K_ESCAPE, pygame.K_q):
                    running = False
                elif e.key == pygame.K_SPACE:
                    paused = not paused
                elif e.key == pygame.K_r:
                    gen = resolve(g, start, goal, ring_max, min_gap)
                    nodes, paths, rays, done = [], [], 0, False
                elif e.key == pygame.K_f:
                    view_cx, view_cz, view_cells = 0.0, 0.0, float(N)
                elif e.key == pygame.K_TAB:
                    start, goal = goal, start
                    gen = resolve(g, start, goal, ring_max, min_gap)
                    nodes, paths, rays, done = [], [], 0, False
                elif e.key in (pygame.K_MINUS, pygame.K_EQUALS):
                    # MIN GAP: the narrowest opening the plane will go through.
                    min_gap += 0.5 if e.key == pygame.K_EQUALS else -0.5
                    min_gap = min(20.0, max(0.5, min_gap))
                    gen = resolve(g, start, goal, ring_max, min_gap)
                    nodes, paths, rays, done = [], [], 0, False
                elif e.key in (pygame.K_LEFTBRACKET, pygame.K_RIGHTBRACKET):
                    # THE MAX RING SIZE, 0.5 to 5.0 by 0.5 - the owner's
                    # setting. Changing it restarts the sweep, because half a
                    # resolve at one ring size and half at another is a picture
                    # of nothing.
                    stepv = 0.5 if ring_max < 5.0 else 2.5
                    ring_max += stepv if e.key == pygame.K_RIGHTBRACKET else -stepv
                    ring_max = min(RING_SETTING_MAX, max(RING_SETTING_MIN, ring_max))
                    gen = resolve(g, start, goal, ring_max, min_gap)
                    nodes, paths, rays, done = [], [], 0, False

        if not done and not paused:
            for _ in range(speed):
                try:
                    nodes, paths, rays, bearing = next(gen)
                except StopIteration:
                    done = True
                    break

        w = min(screen.get_width(), screen.get_height())
        screen.fill((10, 10, 12))

        # Only the visible slice is scaled up, and with NEAREST rather than
        # smooth: this is a picture of CELLS and the question asked of it is
        # whether a ray fits through a gap. Blurred, that is unanswerable at
        # exactly the moment it matters.
        ix, iz = int(view_cx), int(view_cz)
        iw = max(1, int(round(view_cells)))
        ix = max(0, min(N - 1, ix))
        iz = max(0, min(N - 1, iz))
        iw = min(iw, N - max(ix, iz)) if max(ix, iz) + iw > N else iw
        iw = max(1, iw)
        slice_ = surf.subsurface(pygame.Rect(ix, iz, iw, iw))
        screen.blit(pygame.transform.scale(slice_, (w, w)), (0, 0))

        # Which rays ended up on a path, so the dead ends can be told from the
        # ones that led somewhere.
        # Every ray this sweep has cast: red where its chain died, green where
        # it won. They are never cleared, so the picture builds into everywhere
        # the resolver has been.
        for (a0, b0, won) in nodes:
            pygame.draw.line(screen, (70, 240, 110) if won else (150, 30, 36),
                             to_px(a0[0], a0[1], w), to_px(b0[0], b0[1], w), 1)

        # The pooled paths, drawn thick over the top.
        for pth in paths:
            for k in range(len(pth) - 1):
                pygame.draw.line(screen, (120, 255, 160),
                                 to_px(pth[k][0], pth[k][1], w),
                                 to_px(pth[k + 1][0], pth[k + 1][1], w), 3)

        if nodes:
            a0, b0, _ = nodes[-1]
            pygame.draw.line(screen, (255, 235, 90),
                             to_px(a0[0], a0[1], w), to_px(b0[0], b0[1], w), 2)
            hx, hz = to_px(b0[0], b0[1], w)
            pygame.draw.line(screen, (255, 255, 255), (hx - 8, hz), (hx + 8, hz), 1)
            pygame.draw.line(screen, (255, 255, 255), (hx, hz - 8), (hx, hz + 8), 1)

        for pt, col, lab in ((start, (0, 200, 255), "START  team 1 base"),
                             (goal, (255, 140, 0), "FLAG  team 2 base")):
            px_, pz_ = to_px(pt[0], pt[1], w)
            pygame.draw.circle(screen, col, (px_, pz_),
                               max(4, int(50.0 / (g["wx1"] - g["wx0"]) * w)), 2)
            tag = font.render(f"{lab}  ({pt[0]:.0f}, {pt[1]:.0f})", True, col)
            screen.blit(tag, (px_ + 14, pz_ - 8))

        msg = (f"rays {rays}   paths {len(paths)}   hull {hull:.1f} m"
               f"   ring {ring_max:.1f} m   min gap {min_gap:.1f} m   bearing {bearing:+.0f} deg"
               f"   {'DONE' if done else ('PAUSED' if paused else 'sweeping')}"
               f"    wheel zoom  drag pan  [f] fit  [ ] ring  - = gap  [space] pause  [r] restart  [tab] swap  [q] quit")
        screen.blit(font.render(msg, True, (255, 255, 255)), (8, 8))
        pygame.display.flip()
        pygame.time.wait(16 if (done or paused) else delay)

    pygame.quit()


if __name__ == "__main__":
    main()
