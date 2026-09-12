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
import time
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

# Bucket for the been-here-before test, or 0 to switch it off entirely.
#
# OFF, because it was not detecting loops, it was detecting crawling. A 0.4 m
# bucket on position alone kills a chain for passing near its own track, which
# is what going round a building looks like from the inside, and it took team 2
# to team 1 from one route to NONE on its own - 49 of 61 chains died in it.
# STALL_HOPS already covers the case it was meant for: a chain going in circles
# never beats its own closest approach to the flag either, and it judges that
# by progress rather than by proximity. Where two guards answer one question,
# the blunter one does the damage.
LOOP_BUCKET_M = 0.0

# The outcome of a chain is CHAOTIC in its opening bearing - a 1.5 degree shift
# took one direction from 11 winning chains to 5 - because a chain is a long
# deterministic crawl and a tiny change at the start cascades. That is inherent
# to the method, and the answer to sampling a spiky function is to sample it
# more finely rather than to trust any one reading of it:
#
#     3.0 deg,  61 bearings   5 wins, 1 route
#     1.5 deg, 121 bearings  10 wins, 2 routes
#     1.0 deg, 181 bearings  18 wins, 2 routes
SWEEP_STEP_DEG = 1.5

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
                  rings=None, from_xz=None):
    """Draw a ring at the hit point and find where it clears, both sides.

    Exactly as described: a circle at the collision, grown in half-metre steps
    until a point on it is standable. Walked outward from the direction we were
    travelling, both ways at once, so the first clear angle on each side is the
    tangent past the thing we hit.

    Returns the two tangent points, either of which may be None, and the radius
    the ring had reached. Nothing found by max_ring_m means this path is DEAD -
    that is the owner's rule and it is what stops a chain crawling for ever.
    """
    # WHERE THE HOP ACTUALLY STARTS. The ring is drawn at the hit point - the
    # owner's rule, and it is what makes the tangent mean anything - but the
    # tank never stands at the hit point. It is back at the last anchor, and it
    # drives ONE straight line from there to the tangent: "we dont draw from
    # the hit point. we draw from previous point to tangent."
    #
    # So the line that has to be clear is the line that gets driven. Testing
    # the radius from the centre instead passed tangents whose real approach
    # was blocked, and put a dogleg into the path that nothing ever drives.
    fx, fz = from_xz if from_xz is not None else (hx, hz)
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
                if not clear_line(g, fx, fz, px, pz):
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
            # THE ANCHOR GOES IN THE RECORD TOO, because the hop is drawn
            # from there and not from the ring centre.
            rings.append((hx, hz, r, left, right, rej, base_ang, fx, fz))
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
    has_turned = False                  # has anything stopped us yet?

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
            if clear_line(g, x, z, gx, gz):
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

            # AND THE LEAVE CONDITION, or it never comes home.
            #
            # Holding the bearing is right - re-aiming every stride made the
            # tangent survive one step and turn straight back into the wall -
            # but holding it ALONE is fatal the other way: the chain runs off
            # in a straight line and 114 of 121 die having made no progress,
            # 0 routes either direction. A bug walk needs a rule for when to
            # stop following and head for the target again, and that is what
            # this is: we go back to aiming at the base only when the base has
            # actually opened up, not every stride on principle.
            # NOT UNTIL SOMETHING HAS STOPPED US. The opening ray is the
            # sweep's whole point - it is the direction this chain exists to
            # explore - and letting it re-aim before it has hit anything threw
            # that away on the first stride: every chain snapped onto the
            # northward line within nine metres of the base and died there,
            # whatever bearing it was given. "our rays should run the same ray
            # direction on till we hit something."
            if not has_turned:
                continue
            dg = max(np.hypot(gx - x, gz - z), 1e-6)
            agx, agz = (gx - x) / dg, (gz - z) / dg
            _t, _hx, _hz, _r, gblocked = march(g, x, z, agx, agz, goal,
                                               min(ray_cap_m, dg + REACH_M))
            if not gblocked:
                dx, dz = agx, agz
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
                                       goal, limit, min_gap_m, rings, (x, z))
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
        if LOOP_BUCKET_M > 0.0:
            key = (round(best[0] / LOOP_BUCKET_M), round(best[1] / LOOP_BUCKET_M))
            if key in seen_here:
                return dead(why, "looped back onto its own ground", best)
            seen_here.add(key)

        if hand == 0:
            hand = 1 if best is left else -1

        # STRAIGHT FROM HERE TO THE TANGENT. One hop, not two: the tank is
        # standing at (x, z) and drives to the tangent, so that is the line
        # recorded and - now that ring_tangents tests it from here - the line
        # that was proved clear. Routing it via the hit point drew a dogleg
        # into the wall and back out that nothing ever drives.
        has_turned = True
        prev_x, prev_z = x, z
        x, z = best
        pts.append(best)

        # THE RAY KEEPS ITS DIRECTION UNTIL SOMETHING STOPS IT.
        #
        # "our rays should run the same ray direction on till we hit something.
        #  keep moving to next ray point. we move on and try and go to base..
        #  wrong method."
        #
        # Re-aiming at the flag the instant a tangent was taken meant the
        # tangent survived for exactly one stride: the chain stepped aside,
        # turned straight back into the face it had just got round, and did it
        # again. Going round something means CARRYING ON round it, so the
        # direction that reached the tangent becomes the heading and is held
        # until the next thing blocks it. Aiming at the base is what the ring
        # does when it scores its tangents; it is not what every stride does.
        ddx, ddz = x - prev_x, z - prev_z
        dl = np.hypot(ddx, ddz)
        if dl > 1e-6:
            dx, dz = ddx / dl, ddz / dl
        else:
            d = max(np.hypot(gx - x, gz - z), 1e-6)
            dx, dz = (gx - x) / d, (gz - z) / d
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
    # LEFT TO EAST OF THE WAY WE ARE GOING, not of the compass.
    #
    # The sweep angles were absolute: -90 due west, 0 due north, +90 due east.
    # Team 1 sits in the south and its flag is north, so that swept the half
    # circle facing the goal and worked. Team 2 sits in the north and its flag
    # is SOUTH - so every opening ray was aimed at the opposite half of the map
    # from where it was going, and all 61 chains had to turn round before they
    # could start. That is why team 2 to team 1 found nothing at all while the
    # same map the other way found three.
    #
    # The sweep is relative to the flag now, so "start scanning left... until
    # we are point east" means left and east OF THE GOAL, both directions alike.
    to_goal = np.arctan2(gxy[0] - sx, gxy[1] - sz)
    for a_deg in np.arange(SWEEP_FROM_DEG, SWEEP_TO_DEG + 1e-6, SWEEP_STEP_DEG):
        # RINGS ARE PER CHAIN, NOT PER SWEEP. Sixty-one bearings' worth of
        # every radius ever tried is tens of thousands of circles, which is a
        # slideshow and a smear. The bearing being worked shows all of its
        # rings; the ones behind it leave only their collision points.
        trace, rings, why = [], [], []
        got = chain(g, (sx, sz), gxy, to_goal + np.deg2rad(a_deg), max_ring_m,
                    trace, min_gap_m, ray_cap_m, rings, why)
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
    astar_paths, astar_msg = [], ""
    # The search results are a different KIND of answer from the rays, so they
    # get their own family of colour and can be read apart at a glance.
    A_COLS = [(90, 170, 255), (120, 220, 255), (80, 140, 235), (150, 200, 255),
              (60, 190, 245), (110, 160, 240), (140, 230, 250), (70, 120, 220)]
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
                elif e.key == pygame.K_a:
                    # THE SEARCH, on the same picture as the rays. The whole
                    # catalogue, not one route: search, tag the corridor spent,
                    # search again - the owner's rule 3 with a search where the
                    # ray used to be.
                    screen.blit(font.render("searching...", True, (255, 255, 0)),
                                (8, 8))
                    pygame.display.flip()
                    t_a = time.time()
                    astar_paths = catalogue(g, start, goal)
                    lens = [sum(np.hypot(q[k + 1][0] - q[k][0],
                                         q[k + 1][1] - q[k][1])
                                for k in range(len(q) - 1))
                            for q in astar_paths]
                    astar_msg = ("A*: %d routes, shortest %.0f m, %.1f s"
                                 % (len(astar_paths), min(lens, default=0),
                                    time.time() - t_a))
                    print(astar_msg)
                elif e.key == pygame.K_f:
                    view_cx, view_cz, view_cells = 0.0, 0.0, float(N)
                elif e.key == pygame.K_TAB:
                    start, goal = goal, start
                    astar_paths, astar_msg = [], ""
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
        for (hx_, hz_, r_, lf, rt, rej, bang, fx_, fz_) in rings:
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
            # THE HOP IS DRAWN FROM WHERE THE TANK STANDS, not from the ring
            # centre. The centre is where the RAY stopped; the tank is still
            # back at the last anchor and drives one straight line from there.
            # Drawing it from the centre made the picture disagree with the
            # path - the owner spotted it in the window: "looks like we are
            # connecting chains at the center of the rings to get the tangent
            # and not previous point before ring center?" The path was right;
            # the picture was lying about it.
            apx = to_px(fx_, fz_, w)
            for t_ in (lf, rt):
                if t_ is None:
                    continue
                tpx = to_px(t_[0], t_[1], w)
                pygame.draw.line(screen, (90, 255, 235), apx, tpx, 2)
                pygame.draw.circle(screen, (90, 255, 235), tpx, 4)
                # and a dim spur to the ring centre, so it stays clear WHICH
                # ring produced this tangent without implying the tank drove
                # through its middle.
                pygame.draw.line(screen, (70, 110, 105), cpx, tpx, 1)

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

        # THE SEARCH RESULTS, under the ray paths so neither hides the other.
        for i, pth in enumerate(astar_paths):
            col = A_COLS[i % len(A_COLS)]
            for k in range(len(pth) - 1):
                pygame.draw.line(screen, col,
                                 to_px(pth[k][0], pth[k][1], w),
                                 to_px(pth[k + 1][0], pth[k + 1][1], w), 2)
            for (qx, qz) in pth:
                pygame.draw.circle(screen, col, to_px(qx, qz, w), 3)

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
               f"    zoom/drag  [f] fit  , . step  [ ] ring  - = gap  [a] A* catalogue  [space] pause  [r] reset  [tab] swap  [q] quit")
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
        if astar_msg:
            screen.blit(font.render(astar_msg, True, (120, 220, 255)), (8, yy))
            yy += 20
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


# ==========================================================================
# THE FAN NAVIGATOR
# ==========================================================================
#
# A second resolver beside the ring one, borrowed from the shape of Path
# Studio's tools/radar_tangent.py, which solves the same problem in the air.
# Its layer-3 docstring names the ring resolver's dominant failure exactly:
#
#     "A ring fitted round the blocker would be hopeless here; a circle round
#      a 200 m wall has a 100 m radius and its tangents mean nothing."
#
# That is 45 to 52 of every 61 chains here, dying on "ring reached max with no
# tangent". A circle is the wrong primitive for a long wall.
#
# Three things are different, and the third is the one that matters most:
#
#   THE TANGENT COMES FROM A FAN, not from an expanding circle. Cast a fan of
#   rays; the blocked bearings form one contiguous run, and the first CLEAR
#   bearing on each side of that run is the obstacle's silhouette edge. Exact,
#   for any shape, at the cost of one fan instead of 864 ring probes.
#
#   A LAYER RETURNS A BEARING, NOT A PLACE. The walker then steps its own short
#   distance along that bearing. The ring resolver anchors AT the tangent
#   point, which is a jump of up to the ring radius and is why its paths are
#   three hundred points of zigzag. Stepping decouples how far we LOOK from how
#   far we MOVE - and it has to, because a fan that only reaches 3 m can never
#   see past a 40 m building, however short the owner wants the steps.
#
#   ONE RUN, NOT A SWEEP OF 121. The bearing sweep exists because a ring chain
#   is unreliable, so it fires a hundred and hopes. A navigator that answers
#   properly is run ONCE per route, and further routes come from the owner's
#   own rule 3: tag what we used and go again.

LOOK_M = 90.0                      # how far the fan sees, independent of STEP
FAN_HALF = np.deg2rad(75.0)        # half the fan width; 150 degrees total
FAN_RAYS = 121


def fan_scan(g, x, z, goal, look_m):
    """Bearing, range and verdict for every ray in the fan.

    Centred on the flag rather than on the current heading: the silhouette we
    care about is the one hiding the flag, so that is where the fan is pointed.
    """
    gx, gz = goal
    a_t = np.arctan2(gx - x, gz - z)
    out = []
    for i in range(FAN_RAYS):
        a = a_t - FAN_HALF + 2.0 * FAN_HALF * i / (FAN_RAYS - 1)
        t, hx, hz, reached, blocked = march(g, x, z, np.sin(a), np.cos(a),
                                            goal, look_m)
        out.append((a, t, blocked, reached))
    return out, a_t


def fan_heading(g, x, z, goal, look_m, fan):
    """Which way to go from here: straight at the flag, or round the edge.

    Returns (bearing, layer) or (None, why) when neither answers - which is
    where a bounded A* belongs behind this, and does not exist here yet.
    """
    gx, gz = goal
    d = np.hypot(gx - x, gz - z)
    near = min(d, look_m)
    i_t = FAN_RAYS // 2

    # 1 DIRECT. The ray at the flag is clear, so fly it.
    a0, t0, b0, r0 = fan[i_t]
    if r0 or not b0:
        return a0, "direct"

    # 2 TANGENT. Walk out of the blocked run, each way. The first clear bearing
    # is the silhouette; take whichever corner gets us to the flag in less.
    cands = []
    for direction in (+1, -1):
        i = i_t
        while 0 <= i < len(fan) and fan[i][2]:
            i += direction
        if not (0 <= i < len(fan)):
            continue
        a2, t2, b2, r2 = fan[i]
        corner = min(t2, near)
        cx, cz = x + np.sin(a2) * corner, z + np.cos(a2) * corner
        dd = np.hypot(gx - cx, gz - cz)
        aa = np.arctan2(gx - cx, gz - cz)
        _t, _hx, _hz, rr, bb = march(g, cx, cz, np.sin(aa), np.cos(aa), goal,
                                     min(dd + REACH_M, look_m))
        # CAN WE GET AWAY FROM THAT CORNER? Path Studio asks whether the TARGET
        # is visible from it, which is right for camera waypoints a few tens of
        # metres apart. Our flag is 800 m away and the fan sees 90, so it can
        # never be visible and every tangent was refused - the navigator died
        # on its first hop at every look distance from 12 m to 90 m.
        #
        # The local form of the same question is whether a ray LEAVES the
        # corner heading for the flag. Six metres, the distance already
        # measured as load-bearing on the ring resolver: loosening it to the
        # step length there cost two of three routes.
        if rr or not bb or _t >= TANGENT_ESCAPE_M:
            cands.append((corner + dd, a2))
    if cands:
        cands.sort()
        return cands[0][1], "tangent"
    return None, "no silhouette leads anywhere"


def route_fan(g, start, goal, look_m=LOOK_M, step_m=None, why=None,
              trace=None):
    """One point-to-point run. The whole route, or None.

    Deterministic: no sweep, no luck. Run it again after painting what it used
    to get the next route, which is the owner's rule 3.
    """
    step_m = step_m or RAY_CAP_DEFAULT_M
    gx, gz = goal
    x, z = start
    pts = [(x, z)]
    best_d = np.hypot(gx - x, gz - z)
    stall = 0

    for _ in range(MAX_HOPS):
        d = np.hypot(gx - x, gz - z)
        if d <= REACH_M and clear_line(g, x, z, gx, gz):
            pts.append((gx, gz))
            return pts
        if d < best_d - 1.0:
            best_d, stall = d, 0
        else:
            stall += 1
            if stall > STALL_HOPS:
                return dead(why, "no progress toward the flag", (x, z))

        fan, _a_t = fan_scan(g, x, z, goal, look_m)
        if trace is not None:
            for (a, t, b, r) in fan:
                trace.append(((x, z), (x + np.sin(a) * t, z + np.cos(a) * t),
                              False))
        a, layer = fan_heading(g, x, z, goal, look_m, fan)
        if a is None:
            return dead(why, layer, (x, z))

        # MOVE OUR OWN SHORT DISTANCE along the bearing a layer proved. Never
        # further than the ray actually got, or we walk into the thing it
        # stopped at.
        got, hx, hz, reached, blocked = march(g, x, z, np.sin(a), np.cos(a),
                                              goal, min(step_m, d + REACH_M))
        if got < 1e-3:
            return dead(why, "wedged: the proved bearing goes nowhere", (x, z))
        x, z = hx, hz
        pts.append((x, z))
    return dead(why, "ran out of hops", (x, z))


# --------------------------------------------------------------------------
# LAYER 4: a bounded search, because we can see the map
# --------------------------------------------------------------------------
#
# Path Studio's radar_tangent.py again, and its reasoning is the whole point:
#
#     "Wall-following exists because a robot cannot see the map. This one can -
#      the whole occupancy grid is in memory - so the way round is a search,
#      not a guess. Optimal, and when it returns nothing that is a PROOF the
#      target is unreachable, not a timeout."
#
# Every ray method here - ring or fan - guesses. Measured on 19_monastery:
#
#   * At team 1's base, ALL 121 fan rays are blocked at a 90 m look. There is
#     no silhouette to walk round because the tank is standing IN the clutter,
#     not flying above it.
#   * 11% of points along a route already known good have no clear bearing at
#     all in any direction at 90 m. A fan navigator dies at every one of them.
#   * A fan offers TWO candidates per hop. The ring offers 864 and survives on
#     brute force alone, which is why it needs 121 opening bearings to find two
#     routes and why a 1.5 degree change swings it from 11 wins to 5.
#
# A search does not guess, and it does not care how cluttered the ground is.

def coarse_grid(g, cell_m=1.37):
    """A blocked/clear grid at driving scale, eroded by the hull.

    The collision map is 8192 square at 17 cm - 67 million cells, far too fine
    to search per hop. Coarsened by block-any (a cell holding anything solid is
    solid) and then grown by the hull radius, so a path of free cells is a path
    the whole tank fits down rather than one its centre line does.
    """
    key = "coarse_%.2f" % cell_m
    if key in g:
        return g[key]
    f = max(1, int(round(cell_m / g["texel_m"])))
    W = g["W"] // f * f
    solid = g["collide"]
    if g["used"] is not None:
        # SPENT CORRIDORS COUNT AS BLOCKED. Without this the search cannot see
        # what rule 3 tagged and returns the same route every time - the
        # catalogue came back eight identical copies of one road.
        solid = solid | g["used"]
    blocked = solid[:W, :W].reshape(W // f, f, W // f, f).any(axis=(1, 3))

    # GROW IT BY THE HULL. Chebyshev dilation by the hull radius in cells: a
    # centre this close to something solid is a tank overlapping it.
    rad = int(np.ceil((g["hull"] * 0.5) / (f * g["texel_m"])))
    if rad > 0:
        grown = blocked.copy()
        for dr in range(-rad, rad + 1):
            for dc in range(-rad, rad + 1):
                if dr == 0 and dc == 0:
                    continue
                grown |= np.roll(np.roll(blocked, dr, 0), dc, 1)
        blocked = grown
    g[key] = (blocked, f)
    return g[key]


def astar_route(g, start, goal, cell_m=1.37):
    """The whole way from A to B on the coarse grid, or None if there is none.

    Eight-connected with the true diagonal cost, so the heuristic is octile and
    matches the cost exactly. A heuristic that does not match its cost is not a
    faster A*, it is Dijkstra wearing a hat - that mistake cost 466,229
    expansions on this map once already.
    """
    import heapq
    blocked, f = coarse_grid(g, cell_m)
    n = blocked.shape[0]
    span = g["wx1"] - g["wx0"]

    def to_cell(x, z):
        return (int((g["wz1"] - z) / span * g["W"]) // f,
                int((x - g["wx0"]) / span * g["W"]) // f)

    def to_world(r, c):
        return (g["wx0"] + (c + 0.5) * f * g["texel_m"],
                g["wz1"] - (r + 0.5) * f * g["texel_m"])

    s, t = to_cell(*start), to_cell(*goal)
    for cell in (s, t):
        if not (0 <= cell[0] < n and 0 <= cell[1] < n):
            return None
    # A base can sit a cell inside something once the hull erosion is applied;
    # take the nearest free cell rather than declaring the map unsolvable.
    def nearest_free(cell):
        if not blocked[cell]:
            return cell
        for rad in range(1, 60):
            r0, c0 = cell
            for dr in range(-rad, rad + 1):
                for dc in (-rad, rad) if abs(dr) < rad else range(-rad, rad + 1):
                    r, c = r0 + dr, c0 + dc
                    if 0 <= r < n and 0 <= c < n and not blocked[r, c]:
                        return (r, c)
        return None
    s, t = nearest_free(s), nearest_free(t)
    if s is None or t is None:
        return None

    D, D2 = 1.0, np.sqrt(2.0)

    def h(a):
        dr, dc = abs(a[0] - t[0]), abs(a[1] - t[1])
        return D * (dr + dc) + (D2 - 2 * D) * min(dr, dc)

    open_h = [(h(s), 0.0, s)]
    came, gsc = {}, {s: 0.0}
    seen = set()
    while open_h:
        _f, gc, cur = heapq.heappop(open_h)
        if cur in seen:
            continue
        seen.add(cur)
        if cur == t:
            out, k = [], cur
            while k in came:
                out.append(to_world(*k))
                k = came[k]
            out.append(to_world(*s))
            out.reverse()
            return out
        r, c = cur
        for dr in (-1, 0, 1):
            for dc in (-1, 0, 1):
                if dr == 0 and dc == 0:
                    continue
                nr, nc = r + dr, c + dc
                if not (0 <= nr < n and 0 <= nc < n) or blocked[nr, nc]:
                    continue
                # NO CUTTING CORNERS DIAGONALLY. Both orthogonal neighbours
                # must be free or the tank clips the corner it is squeezing
                # past - the diagonal fits on the grid and not on the ground.
                if dr and dc and (blocked[r, nc] or blocked[nr, c]):
                    continue
                step = D2 if (dr and dc) else D
                ng = gc + step
                nb = (nr, nc)
                if ng < gsc.get(nb, 1e18):
                    gsc[nb] = ng
                    came[nb] = cur
                    heapq.heappush(open_h, (ng + h(nb), ng, nb))
    return None


def coarse_clear(g, ax, az, bx, bz, cell_m=1.37):
    """Is the straight line hull-safe, judged in the space the route was planned in?

    Deliberately NOT clear_line. That tests the fine collision map along the
    centre line, which says nothing about whether the hull's shoulders clear -
    string-pulling against it is how an earlier smoother produced an 8.5 m gap
    for a 9 m hull. The coarse grid is already grown by the hull radius, so a
    line through free coarse cells is a line the whole tank fits down.
    """
    blocked, f = coarse_grid(g, cell_m)
    n = blocked.shape[0]
    span = g["wx1"] - g["wx0"]
    scale = g["W"] / span / f
    fx0, fz0 = (ax - g["wx0"]) * scale, (g["wz1"] - az) * scale
    fx1, fz1 = (bx - g["wx0"]) * scale, (g["wz1"] - bz) * scale
    dxc, dzc = fx1 - fx0, fz1 - fz0
    steps = int(max(abs(dxc), abs(dzc)) * 2) + 2
    for i in range(steps + 1):
        u = i / steps
        c, r = int(fx0 + dxc * u), int(fz0 + dzc * u)
        if not (0 <= c < n and 0 <= r < n) or blocked[r, c]:
            return False
    return True


def thin(g, path, cell_m=1.37):
    """String-pull: keep only the corners a driver actually has to turn at.

    Furthest-visible rather than by distance. Thinning by distance cuts corners
    - it drops the point that made a turn safe and leaves a chord through the
    thing being turned round, which stuck three of four hulls once.
    """
    if not path:
        return path
    out, i = [path[0]], 0
    while i < len(path) - 1:
        j = len(path) - 1
        while j > i + 1 and not coarse_clear(g, path[i][0], path[i][1],
                                             path[j][0], path[j][1], cell_m):
            j -= 1
        out.append(path[j])
        i = j
    return out


# Every route has to leave the same base and reach the same flag, so the ground
# right around each is common to all of them and must never be tagged spent -
# paint it and the second search finds its own start walled in.
GUARD_M = 45.0


def paint_used(g, path, width_m=26.0, guard=None):
    """Mark a route's corridor spent, so the next search must find another way.

    The owner's rule 3: "Tag that path as used and try path. When we cant find
    a way there, we are done."
    """
    if g["used"] is None:
        g["used"] = np.zeros_like(g["collide"])
    used = g["used"]
    W = g["W"]
    rad = int(width_m * 0.5 / g["texel_m"])
    ends = guard if guard is not None else (path[0], path[-1])
    for k in range(len(path) - 1):
        ax, az = path[k]
        bx, bz = path[k + 1]
        d = np.hypot(bx - ax, bz - az)
        n = max(2, int(d / g["texel_m"]))
        for j in range(n + 1):
            u = j / n
            px, pz = ax + (bx - ax) * u, az + (bz - az) * u
            if any(np.hypot(px - e[0], pz - e[1]) < GUARD_M for e in ends):
                continue
            c, r = to_texel(g, px, pz)
            r0, r1 = max(0, r - rad), min(W, r + rad + 1)
            c0, c1 = max(0, c - rad), min(W, c + rad + 1)
            used[r0:r1, c0:c1] = True
    for k in [k for k in list(g.keys()) if k.startswith("coarse_")]:
        del g[k]        # the corridor changed; the coarse grid must be rebuilt


def catalogue(g, start, goal, max_routes=8, cell_m=1.37):
    """Every distinct way from A to B: search, tag what it used, search again.

    Exactly the owner's three rules, with a search where the ray used to be.
    It ends when no way is left, and because A* returning nothing is a proof
    rather than a timeout, "we are done" actually means done.
    """
    g["used"] = None
    out = []
    for _ in range(max_routes):
        # LAZY THETA*, then a string-pull to drop the collinear runs it leaves.
        # Measured against plain A* + string-pull on this map: 1.4 to 2.0%
        # shorter for two to three times the time. The catalogue is baked once
        # and held in memory, so a fraction of a second is worth nothing and
        # twenty metres over eight hundred is worth having in a race.
        #
        #   A* + string-pull      829.3 / 826.2 m,  7 / 10 pts, 0.3 s
        #   Lazy Theta*           812.6 / 814.6 m, 19 / 16 pts, 0.6 / 0.9 s
        #   Lazy Theta* + pull    812.2 / 814.4 m, 13 / 11 pts, same
        raw = theta_route(g, start, goal, cell_m)
        if raw is None:
            break
        out.append(thin(g, raw, cell_m))
        paint_used(g, raw, guard=(start, goal))
    g["used"] = None
    for k in [k for k in list(g.keys()) if k.startswith("coarse_")]:
        del g[k]
    return out


# --------------------------------------------------------------------------
# ANY-ANGLE: Lazy Theta*
# --------------------------------------------------------------------------
#
# Path Studio's recommendation, and the published answer for a grid we can see
# all of: Theta* (Nash, Daniel, Koenig, Felner, JAIR 2010) is A* whose parent
# pointer skips to the furthest ancestor still in line of sight, so the path
# comes out taut through the corners instead of zig-zagging along grid edges.
# Lazy Theta* (AAAI 2010) defers the sight check to expansion, one per vertex
# rather than one per neighbour.
#
# Worth measuring against A*-then-string-pull rather than assuming, because the
# two are supposed to land in nearly the same place and one of them is already
# built.

def _los(blocked, r0, c0, r1, c1):
    """Line of sight between two cells, refusing to slip through a corner."""
    n = blocked.shape[0]
    dr, dc = abs(r1 - r0), abs(c1 - c0)
    sr = 1 if r1 > r0 else -1
    sc = 1 if c1 > c0 else -1
    err = dr - dc
    r, c = r0, c0
    while True:
        if not (0 <= r < n and 0 <= c < n) or blocked[r, c]:
            return False
        if r == r1 and c == c1:
            return True
        e2 = 2 * err
        mr = mc = False
        if e2 > -dc:
            err -= dc
            r += sr
            mr = True
        if e2 < dr:
            err += dr
            c += sc
            mc = True
        if mr and mc:
            # A diagonal that clips two solids meeting at a corner is not a
            # line of sight - the tank does not fit through the corner even
            # though the line does.
            if blocked[r - sr, c] or blocked[r, c - sc]:
                return False


def theta_route(g, start, goal, cell_m=1.37):
    """Lazy Theta* on the coarse grid. Returns the taut world polyline."""
    import heapq
    blocked, f = coarse_grid(g, cell_m)
    n = blocked.shape[0]
    span = g["wx1"] - g["wx0"]

    def to_cell(x, z):
        return (int((g["wz1"] - z) / span * g["W"]) // f,
                int((x - g["wx0"]) / span * g["W"]) // f)

    def to_world(rc):
        return (g["wx0"] + (rc[1] + 0.5) * f * g["texel_m"],
                g["wz1"] - (rc[0] + 0.5) * f * g["texel_m"])

    def nearest_free(cell):
        if 0 <= cell[0] < n and 0 <= cell[1] < n and not blocked[cell]:
            return cell
        for rad in range(1, 60):
            for dr in range(-rad, rad + 1):
                for dc in (-rad, rad) if abs(dr) < rad else range(-rad, rad + 1):
                    r, c = cell[0] + dr, cell[1] + dc
                    if 0 <= r < n and 0 <= c < n and not blocked[r, c]:
                        return (r, c)
        return None

    s, t = nearest_free(to_cell(*start)), nearest_free(to_cell(*goal))
    if s is None or t is None:
        return None

    def dist(a, b):
        return np.hypot(a[0] - b[0], a[1] - b[1])

    parent = {s: s}
    gsc = {s: 0.0}
    open_h = [(dist(s, t), s)]
    closed = set()
    while open_h:
        _f, cur = heapq.heappop(open_h)
        if cur in closed:
            continue
        # LAZY: the parent was assumed visible when this was pushed. Check it
        # now, once, and if it was wrong fall back to the best neighbour that
        # really is closed and adjacent.
        p = parent[cur]
        if p != cur and not _los(blocked, p[0], p[1], cur[0], cur[1]):
            best, bg = None, 1e18
            for dr in (-1, 0, 1):
                for dc in (-1, 0, 1):
                    nb = (cur[0] + dr, cur[1] + dc)
                    if nb in closed and gsc[nb] + dist(nb, cur) < bg:
                        best, bg = nb, gsc[nb] + dist(nb, cur)
            if best is None:
                continue
            parent[cur], gsc[cur] = best, bg
        closed.add(cur)
        if cur == t:
            out, k = [], cur
            while parent[k] != k:
                out.append(to_world(k))
                k = parent[k]
            out.append(to_world(k))
            out.reverse()
            return out
        for dr in (-1, 0, 1):
            for dc in (-1, 0, 1):
                if dr == 0 and dc == 0:
                    continue
                nb = (cur[0] + dr, cur[1] + dc)
                if not (0 <= nb[0] < n and 0 <= nb[1] < n) or blocked[nb]:
                    continue
                if dr and dc and (blocked[cur[0], nb[1]] or blocked[nb[0], cur[1]]):
                    continue
                if nb in closed:
                    continue
                pc = parent[cur]
                ng = gsc[pc] + dist(pc, nb)
                if ng < gsc.get(nb, 1e18):
                    gsc[nb], parent[nb] = ng, pc
                    heapq.heappush(open_h, (ng + dist(nb, t), nb))
    return None
