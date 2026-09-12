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

    Walks the collision map texel by texel - the SAME exact traversal that
    clear_line uses - so a line this calls clear and a later check of that line
    can no longer disagree. They did: a fixed stride skips a texel it only
    clips, and the two tests skipped different ones.
    """
    gx, gz = goal
    tex = g["texel_m"]
    ex, ez = x + dx * limit, z + dz * limit
    t_prev = 0.0
    h_prev = None
    for col, row, t in walk_texels(g, x, z, ex, ez):
        if t > limit:
            break
        px, pz = x + dx * t, z + dz * t
        if (px - gx) ** 2 + (pz - gz) ** 2 <= REACH_M ** 2:
            return t, px, pz, True, False              # arrived

        if blocked_at(g, col, row):
            # STOP JUST SHORT OF THE TEXEL WE CANNOT ENTER. Backing off a
            # quarter of a texel keeps the hit point inside the last clear one,
            # which is what the ring is then centred on.
            tb = max(0.0, t - tex * 0.25)
            return tb, x + dx * tb, z + dz * tb, False, True

        # A SLOPE STOPS A RAY THE SAME WAY A WALL DOES. Nothing here climbs or
        # drops more than 45 degrees, and the collision map cannot say so - it
        # is a height threshold, and a cliff face is under a metre tall between
        # any two samples the whole way down. So the gradient is checked from
        # texel to texel, and a wall of rock and the edge of a ravine both end
        # the ray and get the ring sweep they deserve.
        h = int(g["floor"][row, col])
        if h_prev is not None:
            run = max(t - t_prev, 1e-6)
            if abs(h - h_prev) / g["hscale"] > run * MAX_SLOPE_TAN:
                tb = max(0.0, t_prev)
                return tb, x + dx * tb, z + dz * tb, False, True
        h_prev, t_prev = h, t

    # RAN OUT OF ALLOWED LENGTH, which is not a collision. "nothing move."
    return limit, ex, ez, False, False


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
RING_ARC_MAX = np.deg2rad(150.0)

# How far a ray must get away from a candidate tangent before that tangent
# counts. Below this the ring has found a clear spot inside a corner rather than
# a way out of it.
TANGENT_ESCAPE_M = 6.0

# THE MINIMUM GAP WE CAN GET THROUGH, in metres. The owner's setting: "min gap
# size (add and set to 4)". A tangent that clears but sits in a slot narrower
# than this is no use - the plane does not fit through it.
MIN_GAP_DEFAULT_M = 4.0

# THE LONGEST A SINGLE RAY MAY FLY, on top of never passing the flag.
#
# A ray was free to run the map's whole diagonal - 2,100 m - so one that missed
# simply kept going, drawing across ground it had no business in and anchoring
# its tangent hundreds of metres from anything relevant. The owner, watching:
# "you rays are way to long."
#
# Two caps, and they answer different cases. Never flying past the flag handles
# every ray that is aimed at it, which is all of them after the first. This one
# handles the sweep's OPENING rays, which point away from the target on purpose
# and are not bounded by the distance to it at all.
# FIVE YARDS. The owner: "you should ne looking like 5 yards. nothing move" -
# cast a short ray, and if nothing is in the way, move there.
#
# This is a WALKER, not a long-range caster. I had rays flying the length of the
# map and anchoring their tangents hundreds of metres from anything, which is
# why the ring size never made sense: a 5 m circle is absurd next to an 800 m
# ray and exactly right next to a 4.5 m step. At this scale the whole spec fits
# together - small step, hit, small ring, tangent, step on.
RAY_CAP_DEFAULT_M = 3.0

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
# WITHDRAWN, and replaced by STALL_HOPS below. The angle rule was tuned on a
# ray marcher that sampled its lines at a fixed stride; once the marching
# became exact it started killing chains that were crawling round a wall
# correctly - 44 of 60 deaths, the top cause by a distance. Turn shape was
# never the question. Getting nowhere was, so that is what is measured now.

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
# 800 m at five yards a step is 178 steps before a single detour, so the hop
# budget has to be an order up from what a long-ray version needed.
MAX_HOPS = 2400

# The sweep: start pointing LEFT, finish pointing EAST.
SWEEP_FROM_DEG = -90.0
SWEEP_TO_DEG = 90.0
# How many hops a chain may go without beating its closest approach to the
# flag. Generous on purpose: going round a big building is a long detour that
# gets worse before it gets better, and a tight budget kills the chain halfway
# round - which is the difference between a wall being an obstacle and a wall
# being the end of the search.
STALL_HOPS = 120

SWEEP_STEP_DEG = 3.0

REJECT_M = 55.0


def walk_texels(g, ax, az, bx, bz):
    """Every texel the segment crosses, in order, with the distance it starts.

    Amanatides & Woo. Point sampling a line - at a texel, at half a texel, at
    any fixed stride - can always miss a texel the line only clips, because the
    samples land either side of it. That is not a tuning problem, it is what
    sampling IS, and it showed up as two honest tests of the same line
    disagreeing: clear_line said clear and a denser re-sample found solid.

    Stepping cell boundary to cell boundary instead visits every texel the
    segment actually touches, exactly once, and cannot skip one. It is also
    cheaper than the 8.5 cm stride it replaces.

    Yields (col, row, t_enter) in metres along the segment.
    """
    W = g["W"]
    span = g["wx1"] - g["wx0"]
    # Texel coordinates as floats, same mapping to_texel() floors.
    fx0 = (ax - g["wx0"]) / span * W
    fz0 = (g["wz1"] - az) / (g["wz1"] - g["wz0"]) * W
    fx1 = (bx - g["wx0"]) / span * W
    fz1 = (g["wz1"] - bz) / (g["wz1"] - g["wz0"]) * W

    dxc, dzc = fx1 - fx0, fz1 - fz0
    n_cells = np.hypot(dxc, dzc)
    if n_cells < 1e-9:
        yield int(fx0), int(fz0), 0.0
        return
    metres_per_cell = g["texel_m"]

    col, row = int(np.floor(fx0)), int(np.floor(fz0))
    col1, row1 = int(np.floor(fx1)), int(np.floor(fz1))
    step_c = 1 if dxc > 0 else -1
    step_r = 1 if dzc > 0 else -1

    # Distance, in CELLS along the segment, to the next boundary in each axis
    # and between successive boundaries.
    if abs(dxc) < 1e-12:
        t_max_c, t_delta_c = np.inf, np.inf
    else:
        nxt = (col + 1) if dxc > 0 else col
        t_max_c = (nxt - fx0) / dxc * n_cells
        t_delta_c = abs(1.0 / dxc) * n_cells
    if abs(dzc) < 1e-12:
        t_max_r, t_delta_r = np.inf, np.inf
    else:
        nxt = (row + 1) if dzc > 0 else row
        t_max_r = (nxt - fz0) / dzc * n_cells
        t_delta_r = abs(1.0 / dzc) * n_cells

    t = 0.0
    for _ in range(int(n_cells * 2) + 4):
        yield col, row, t * metres_per_cell
        if col == col1 and row == row1:
            return
        if t_max_c < t_max_r:
            t, col, t_max_c = t_max_c, col + step_c, t_max_c + t_delta_c
        else:
            t, row, t_max_r = t_max_r, row + step_r, t_max_r + t_delta_r
        if t > n_cells:
            return


def blocked_at(g, col, row):
    """Is this texel one the hull cannot occupy?"""
    W = g["W"]
    if col < 0 or row < 0 or col >= W or row >= W:
        return True
    if g["used"] is not None and g["used"][row, col]:
        return True
    return bool(g["collide"][row, col])


def clear_line(g, ax, az, bx, bz):
    """Is the straight line between two points free the whole way?

    Stepped at HALF a texel rather than a whole one. A ray stepping a full
    texel along a diagonal can pass between two solid cells that share only a
    corner, so two samplings of the same line can disagree about it - which is
    how a march and a check of the same path end up with different answers.
    """
    for col, row, _t in walk_texels(g, ax, az, bx, bz):
        if blocked_at(g, col, row):
            return False
    return True


# Why a probe on the ring was thrown away. Recorded per point so the window
# can show the SHAPE of a refusal: a rim of SOLID is a wall, a rim of NARROW is
# a gap too tight for the hull, and a rim of NOESCAPE is a corner - three very
# different reasons that all used to look identical, which is to say invisible.
REJ_SOLID, REJ_CHORD, REJ_NOESCAPE, REJ_NARROW = 0, 1, 2, 3


def ring_tangents(g, hx, hz, indx, indz, max_ring_m, goal, limit, min_gap_m,
                  rings=None):
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
        rej = []                 # what this radius refused, and why
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
                    rej.append((px, pz, REJ_SOLID))
                    continue

                # AND THE WAY TO IT MUST BE CLEAR TOO.
                #
                # The tangent sits on the far side of the ring, so the straight
                # line from the hit point to it is a CHORD - and a chord across
                # a circle drawn at a corner cuts through the corner. Checking
                # only that the tangent POINT is clear let finished paths run
                # through solid ground: 0.4%, 2.3% and 3.3% of their samples
                # inside obstacles, which is the owner watching and saying "we
                # are traveling through stuff".
                #
                # Validating the point and not the way to it is the same class
                # of mistake as the grazing string-pull earlier: the thing that
                # was checked is not the thing that gets driven.
                if not clear_line(g, hx, hz, px, pz):
                    rej.append((px, pz, REJ_CHORD))
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
                out, _, _, _, _ = march(g, px, pz, (gx2 - px) / d2, (gz2 - pz) / d2,
                                     goal, min(d2 + REACH_M, TANGENT_ESCAPE_M * 3.0))
                if out < TANGENT_ESCAPE_M:
                    rej.append((px, pz, REJ_NOESCAPE))
                    continue

                # AND CAN WE GET THROUGH IT? A clear point in a slot narrower
                # than the plane is not a way past anything.
                wide, _, _ = measure_gap(g, px, pz,
                                         (gx2 - px) / d2, (gz2 - pz) / d2,
                                         min_gap_m)
                if wide < min_gap_m:
                    rej.append((px, pz, REJ_NARROW))
                    continue

                if sgn > 0:
                    left = (px, pz)
                else:
                    right = (px, pz)
            if left is not None and right is not None:
                break
            a += RING_ANGLE_STEP
        # EVERY RADIUS IS RECORDED, won or lost. The expanding ring is the
        # part of this algorithm the owner cannot otherwise see, and a ring
        # that is never drawn is a ring nobody can tell is too small.
        if rings is not None:
            rings.append((hx, hz, r, left, right, rej, base_ang))
        if left is not None or right is not None:
            return left, right, r
        r += RING_STEP_M
    return None, None, r


def dead(why, reason, where):
    """Record how a chain died and return the failure.

    "you are giving up" - and there was no way to tell WHERE it gave up or on
    what, because every one of the five exits returned a bare None. A chain
    that dies at the first wall and one that crawls four hundred hops into a
    dead end were the same red line on screen.
    """
    if why is not None:
        why.append((reason, where))
    return None


def chain(g, start, goal, bearing, max_ring_m, trace, min_gap_m,
          ray_cap_m, rings=None, why=None):
    """One path attempt: ray, ring, tangent, re-aim at the base, repeat.

    "if we could not... That path is dead. If we can hit it, we anchor at
    tangent and scan at base location. same thing."

    So every hop after the first aims AT THE FLAG. The opening ray is the only
    one that goes where the sweep points it; after that the chain is always
    trying to go home and only turning aside to get round what is in the way.
    """
    gx, gz = goal
    pts = [start]
    x, z = start
    dx, dz = np.sin(bearing), np.cos(bearing)
    seen_here = set()
    hand = 0                    # 0 undecided, +1 keep it on the left, -1 right
    best_d = np.hypot(gx - x, gz - z)   # closest to the flag we have ever been
    stall = 0                           # hops since that got better

    for _ in range(MAX_HOPS):
        # STILL GETTING SOMEWHERE? The old test asked whether the last few
        # turns pointed the same way, and killed the chain if they did - but
        # four turns within six degrees is exactly what crawling round a long
        # wall looks like, so it executed chains for doing the right thing. It
        # became the top cause of death the moment the marching got accurate:
        # 44 of 60.
        #
        # Grinding is not about the shape of the turns, it is about getting
        # nowhere, so that is what gets measured. A chain may wander away from
        # the flag for as long as it likes - going round a building means
        # exactly that - but it has to beat its own record eventually.
        d_now = np.hypot(gx - x, gz - z)
        if d_now < best_d - 1.0:
            best_d, stall = d_now, 0
        else:
            stall += 1
            if stall > STALL_HOPS:
                return dead(why, "no progress toward the flag", (x, z))

        # NEVER FURTHER THAN THE FLAG, AND NEVER MORE THAN A STEP.
        #
        # Two caps answering two cases. A ray aimed at the flag has no business
        # flying past it. And a STEP is five yards - the owner's scale - so the
        # walker takes a short look, moves if it is clear, and only ring-sweeps
        # where it actually stops. Rays running the map's whole diagonal is what
        # made the ring size meaningless: a 5 m circle is absurd beside an 800 m
        # ray and exactly right beside a 4.5 m step.
        limit = min(np.hypot(gx - x, gz - z) + REACH_M, ray_cap_m)
        got, hx, hz, reached, blocked = march(g, x, z, dx, dz, goal, limit)
        trace.append(((x, z), (hx, hz), reached))
        if reached:
            # THE LAST HOP IS A HOP LIKE ANY OTHER. march() calls it arrived
            # anywhere within REACH_M of the flag, and the chain then jumped
            # straight to the flag - up to twelve metres, across whatever was
            # in between, with nothing testing it. Third instance of the same
            # mistake: a recorded hop no test ever passed.
            #
            # If that last run home is blocked, this is not an arrival, it is
            # a collision like any other and gets the ring it deserves.
            if clear_line(g, hx, hz, gx, gz):
                if np.hypot(hx - pts[-1][0], hz - pts[-1][1]) > 1e-3:
                    pts.append((hx, hz))
                pts.append((gx, gz))
                return pts                               # THE PRIZE
            blocked = True

        # NOTHING IN THE WAY - MOVE THERE. A step that runs its full five yards
        # without hitting anything is not a collision and must not be treated
        # as one. Ring-sweeping at the end of every clear step was why the
        # walker never went anywhere: it stopped to look for a way round open
        # ground, every stride, all the way.
        if not blocked:
            # KEEP THE HEADING. Re-aiming at the flag after every clear step
            # meant the swept bearing survived exactly one stride, so all
            # sixty-one chains folded onto the same greedy line within five
            # yards and walked into the same cul-de-sac - all of them ending at
            # (-12, -121), 519 m short, whatever bearing they set off on.
            #
            # "nothing move" means carry on the way you were going. A heading is
            # only given up when something stops it, and then the ring decides
            # the new one. That is what makes the sweep mean anything: each
            # bearing explores its own ground instead of the first corner.
            x, z = hx, hz
            pts.append((x, z))
            continue
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
                                       goal, limit, min_gap_m, rings)
        if left is None and right is None:
            return dead(why, "ring reached max with no tangent", (hx, hz))

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
            g2, _, _, _, _ = march(g, cxx, czz, (gx - cxx) / d, (gz - czz) / d,
                                   goal, limit)
            if g2 > best_got:
                best, best_got = cand, g2
        if best is None:
            return dead(why, "no tangent a ray could leave", (hx, hz))

        # BUT IT MUST ACTUALLY MOVE. Crawling is fine; crawling on the spot is
        # a loop, and a loop is a dead path however long you let it run.
        # The bucket has to be FINER than a ring step, or crawling looks like
        # looping: a 0.5 m ring moves the anchor less than a 2 m bucket, so the
        # second anchor landed in the first one's square and every chain was
        # killed for going round in a circle it had not gone round.
        key = (round(best[0] / 0.4), round(best[1] / 0.4))
        if key in seen_here:
            return dead(why, "looped back onto its own ground", best)
        seen_here.add(key)

        if hand == 0:
            hand = 1 if best is left else -1

        # THE HIT POINT IS PART OF THE PATH.
        #
        # It was being dropped: on a blocked stride the chain appended only the
        # tangent, so the recorded hop ran from where the ray STARTED straight
        # to a tangent on a ring centred where it STOPPED - a line nothing ever
        # tested, and one that cuts off the very corner the ring was drawn to
        # go round. A 155 m stride ending in a 9 m sidestep was recorded as one
        # 155 m diagonal through the obstacle.
        #
        # The ray is only known clear as far as (hx, hz), so that is where the
        # path goes before it steps aside. Two hops, both tested: the stride
        # the march proved, then the radius clear_line proved.
        if np.hypot(hx - pts[-1][0], hz - pts[-1][1]) > 1e-3:
            pts.append((hx, hz))
        x, z = best
        pts.append(best)
        d = max(np.hypot(gx - x, gz - z), 1e-6)
        dx, dz = (gx - x) / d, (gz - z) / d              # scan at base location
    return dead(why, "ran out of hops", (x, z))


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
            min_gap_m=MIN_GAP_DEFAULT_M, ray_cap_m=RAY_CAP_DEFAULT_M):
    """Sweep from LEFT round to EAST, one chain per bearing.

    "We will start scanning left. every fail or win, we change ray and try for
    another path chain. We do this until we are point east."

    A win and a loss cost the same thing - the next bearing - so the sweep is
    the whole outer loop and there is no backtracking anywhere in it.
    """
    sx, sz = snap_free(g, *start)
    gxy = snap_free(g, *goal)
    pool, rays, deaths, rings = [], [], [], []
    for a_deg in np.arange(SWEEP_FROM_DEG, SWEEP_TO_DEG + 1e-6, SWEEP_STEP_DEG):
        # RINGS ARE PER CHAIN, NOT PER SWEEP. Sixty-one bearings' worth of
        # every radius ever tried is tens of thousands of circles, which is a
        # slideshow and a smear. The bearing being worked shows all of its
        # rings; the ones behind it leave only their collision points.
        trace, rings, why = [], [], []
        got = chain(g, (sx, sz), gxy, np.deg2rad(a_deg), max_ring_m, trace,
                    min_gap_m, ray_cap_m, rings, why)
        for seg in trace:
            rays.append((seg[0], seg[1], got is not None))
        if got is not None and not too_close(got, pool):
            pool.append(got)
        if why:
            deaths.append((a_deg,) + why[-1])
        yield rays, pool, len(rays), a_deg, rings, deaths
    # THE LAST CHAIN'S RINGS STAY UP. Blanking them on the closing yield meant
    # the picture the owner was watching wiped itself the instant it finished.
    yield rays, pool, len(rays), SWEEP_TO_DEG, rings, deaths


def main():
    import pygame

    map_name = sys.argv[1] if len(sys.argv) > 1 and not sys.argv[1].startswith("-") \
        else "19_monastery"
    hull = 4.5
    ring_max = RING_MAX_DEFAULT_M
    min_gap = MIN_GAP_DEFAULT_M
    ray_cap = RAY_CAP_DEFAULT_M
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

    gen = resolve(g, start, goal, ring_max, min_gap, ray_cap)
    nodes, paths, rays, bearing = [], [], 0, SWEEP_FROM_DEG
    rings, deaths = [], []
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

    def m_to_px(m, w):
        # Metres to pixels THROUGH THE VIEW, so a ring drawn at 3.5 m is 3.5 m
        # wide on the map at any zoom. A radius in screen units would have lied
        # about the one number the ring exists to show.
        return m / (g["wx1"] - g["wx0"]) * N / view_cells * w

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
                    gen = resolve(g, start, goal, ring_max, min_gap, ray_cap)
                    nodes, paths, rays, done = [], [], 0, False
                    rings, deaths = [], []
                elif e.key == pygame.K_f:
                    view_cx, view_cz, view_cells = 0.0, 0.0, float(N)
                elif e.key == pygame.K_TAB:
                    start, goal = goal, start
                    gen = resolve(g, start, goal, ring_max, min_gap, ray_cap)
                    nodes, paths, rays, done = [], [], 0, False
                    rings, deaths = [], []
                elif e.key in (pygame.K_COMMA, pygame.K_PERIOD):
                    # STEP LENGTH - how far one ray may fly before it counts as
                    # a move. 4.5 m is the owner's five yards, a walker; wind it
                    # up past a hundred and it becomes a long-range caster,
                    # which is a different algorithm with different answers.
                    # Both are worth being able to see from the same window.
                    ray_cap *= 1.6 if e.key == pygame.K_PERIOD else 1.0 / 1.6
                    ray_cap = min(2000.0, max(2.0, ray_cap))
                    gen = resolve(g, start, goal, ring_max, min_gap, ray_cap)
                    nodes, paths, rays, done = [], [], 0, False
                    rings, deaths = [], []
                elif e.key in (pygame.K_MINUS, pygame.K_EQUALS):
                    # MIN GAP: the narrowest opening the plane will go through.
                    min_gap += 0.5 if e.key == pygame.K_EQUALS else -0.5
                    min_gap = min(20.0, max(0.5, min_gap))
                    gen = resolve(g, start, goal, ring_max, min_gap, ray_cap)
                    nodes, paths, rays, done = [], [], 0, False
                    rings, deaths = [], []
                elif e.key in (pygame.K_LEFTBRACKET, pygame.K_RIGHTBRACKET):
                    # THE MAX RING SIZE, 0.5 to 5.0 by 0.5 - the owner's
                    # setting. Changing it restarts the sweep, because half a
                    # resolve at one ring size and half at another is a picture
                    # of nothing.
                    stepv = 0.5 if ring_max < 5.0 else 2.5
                    ring_max += stepv if e.key == pygame.K_RIGHTBRACKET else -stepv
                    ring_max = min(RING_SETTING_MAX, max(RING_SETTING_MIN, ring_max))
                    gen = resolve(g, start, goal, ring_max, min_gap, ray_cap)
                    nodes, paths, rays, done = [], [], 0, False
                    rings, deaths = [], []

        if not done and not paused:
            for _ in range(speed):
                try:
                    nodes, paths, rays, bearing, rings, deaths = next(gen)
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

        # THE EXPANDING RINGS. The heart of the algorithm and, until now, the
        # one part of it with no picture at all: "i want to see the expanding
        # rings on your mapping."
        #
        # Every radius the ring grew through is drawn, faint for the ones that
        # found nothing and bright for the one that did, so a ring that dies at
        # its limit looks like a full dartboard and a ring that succeeds at once
        # looks like a single circle. Each refused probe is a dot in the colour
        # of its REASON - a rim of red is a wall, a rim of purple is a corner,
        # a rim of blue is a gap too narrow for the hull. Same refusal to the
        # algorithm, completely different thing to look at.
        REJ_COL = {REJ_SOLID: (190, 55, 45), REJ_CHORD: (210, 120, 45),
                   REJ_NOESCAPE: (165, 90, 215), REJ_NARROW: (70, 140, 230)}
        for (hx_, hz_, r_, lf, rt, rej, bang) in rings:
            cpx = to_px(hx_, hz_, w)
            r_px = int(m_to_px(r_, w))
            won = lf is not None or rt is not None
            if r_px >= 2:
                pygame.draw.circle(screen, (255, 225, 90) if won else (96, 82, 40),
                                   cpx, r_px, 2 if won else 1)
            if r_px >= 3:
                for (rx, rz, code) in rej:
                    pygame.draw.circle(screen, REJ_COL[code],
                                       to_px(rx, rz, w), 2)
            for t_ in (lf, rt):
                if t_ is None:
                    continue
                tpx = to_px(t_[0], t_[1], w)
                pygame.draw.line(screen, (90, 255, 235), cpx, tpx, 2)
                pygame.draw.circle(screen, (90, 255, 235), tpx, 4)

        # WHERE THE CHAINS GAVE UP, and on what. A cross per dead chain in the
        # colour of its reason: the picture says at a glance whether the sweep
        # is dying at one wall or in sixty different corners.
        DEATH_COL = {"ring reached max with no tangent": (235, 60, 50),
                     "no tangent a ray could leave": (175, 95, 225),
                     "looped back onto its own ground": (240, 170, 60),
                     "no progress toward the flag": (235, 235, 120),
                     "ran out of hops": (120, 200, 255)}
        for (_a, reason, wh) in deaths:
            dx_, dz_ = to_px(wh[0], wh[1], w)
            c = DEATH_COL.get(reason, (255, 255, 255))
            pygame.draw.line(screen, c, (dx_ - 5, dz_ - 5), (dx_ + 5, dz_ + 5), 2)
            pygame.draw.line(screen, c, (dx_ - 5, dz_ + 5), (dx_ + 5, dz_ - 5), 2)

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
               f"   step {ray_cap:.0f} m   ring {ring_max:.1f} m   gap {min_gap:.1f} m   bearing {bearing:+.0f}"
               f"   {'DONE' if done else ('PAUSED' if paused else 'sweeping')}"
               f"    zoom/drag  [f] fit  , . step  [ ] ring  - = gap  [space] pause  [r] reset  [tab] swap  [q] quit")
        screen.blit(font.render(msg, True, (255, 255, 255)), (8, 8))

        # WHAT THE COLOURS MEAN, and how many chains died of each. The tally is
        # the answer to "it gives up easy": if every chain dies of one reason,
        # that reason is the algorithm's real limit and the rest is tuning noise.
        tally = {}
        for (_a, reason, _w) in deaths:
            tally[reason] = tally.get(reason, 0) + 1
        legend = [((190, 55, 45), "ring probe: solid"),
                  ((210, 120, 45), "ring probe: chord blocked"),
                  ((165, 90, 215), "ring probe: no escape (corner)"),
                  ((70, 140, 230), f"ring probe: gap under {min_gap:.1f} m"),
                  ((90, 255, 235), "tangent taken"),
                  ((255, 225, 90), "ring that found one")]
        yy = 30
        for col, lab in legend:
            pygame.draw.circle(screen, col, (14, yy + 6), 4)
            screen.blit(font.render(lab, True, col), (26, yy))
            yy += 17
        yy += 6
        screen.blit(font.render(f"chains dead: {len(deaths)}", True,
                                (235, 235, 235)), (8, yy))
        yy += 17
        for reason, cnt in sorted(tally.items(), key=lambda kv: -kv[1]):
            c = DEATH_COL.get(reason, (255, 255, 255))
            screen.blit(font.render(f"  {cnt:3d}  x  {reason}", True, c), (8, yy))
            yy += 17
        pygame.display.flip()
        pygame.time.wait(16 if (done or paused) else delay)

    pygame.quit()


if __name__ == "__main__":
    main()
