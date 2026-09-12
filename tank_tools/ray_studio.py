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

# SOLID_BIT, bake_version 2. Terrain-borne geometry over obstacle_min_h stands
# at this texel, measured with the trees LEFT OUT - read from the depth buffer
# between the model pass and the tree pass.
#
# It exists because the top map is ONE LAYER and the canopy wins the depth
# test, so a rock or a wall under a bush keys as tree - and anything that
# crushes foliage drives straight through it. On monastery that is 317,776
# tree-keyed texels carrying something solid, against the 2,581 cells this
# session measured from the outside before the bit existed.
#
# THE RULE IS NOW `kind = tree AND NOT solid`, never `kind = tree`.
SOLID_BIT = 32
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
    # CRUSHABLE ONLY IF IT IS NOT ALSO SOLID. A tank flattens a tree, a fence
    # and a vase - the owner's rule - but a texel that keys tree and carries
    # the solid bit is rock or wall standing UNDER a canopy, and driving at it
    # is driving into a cliff.
    solid = (key & SOLID_BIT).astype(bool)
    # THE SOLID BIT QUALIFIES TREES, AND ONLY TREES. It is read between the
    # model pass and the tree pass, so a model sets it for itself: 75.6% of
    # fence texels and 63.3% of prop texels carry it against 7.0% of tree
    # texels. Testing it on fence and prop un-crushed three quarters of the
    # fences on this map.
    crushable = ((kind == KIND_FENCE) | (kind == KIND_PROP) |
                 ((kind == KIND_TREE) & ~solid))
    testable = ~crushable

    collide = (over & testable)         | (key & TRUNK_BIT).astype(bool)         | (key & OUTLAND_BIT).astype(bool)         | (kind == KIND_WATER)

    # GROW IT BY THE HULL, ONCE, AT FULL RESOLUTION.
    #
    # Without this the ray resolver had no hull width ANYWHERE: it validated a
    # centre line through single 17 cm texels, with a gap test only at tangent
    # candidates. Measured against a distance transform, 12% of a finished ray
    # route had a 4.5 m tank overlapping solid geometry, closest approach
    # 0.17 m - one texel.
    #
    # Worse, the in-solid checks that passed it sampled the centre line too, so
    # the check and the flaw shared an assumption and the routes looked clean.
    # The search results were never affected because they plan on a hull-grown
    # grid - that erosion was doing more work than the search algorithm.
    #
    # 6.4 s and 537 MB transient on an 8192 square map, once per build.
    # THE REAL OBJECT IDS, bake_version 2. Which OBJECT is on top at each texel,
    # biased by one so 0 is nothing. Written by the same fragment as the key and
    # settled by one depth test, so the id and the kind always describe the same
    # surface. This replaces labelling connected components of a kind mask -
    # a wall touching a cliff was one blob, and now it is two objects.
    # THE COLOUR TYPE IS nuTERRA'S DATA PRODUCT and the palette ships in the
    # meta as kind_N_rgb. Read it rather than invent one: their classifier
    # decides what a kind means, and the colour is part of that.
    palette = {}
    for k in range(8):
        v = meta.get("kind_%d_rgb" % k)
        if v:
            palette[k] = tuple(int(x) for x in v.split(","))
    palette.setdefault(0, (60, 70, 55))          # terrain has no rgb of its own
    palette.setdefault(7, (150, 150, 150))

    ids, id_names = None, {}
    idp = os.path.join(FLIGHT, map_name + "_ids.u32")
    if os.path.exists(idp) and meta.get("id_format", "") == "u32":
        ids = np.fromfile(idp, dtype="<u4").reshape(W, W)
        csv = os.path.join(FLIGHT, map_name + "_ids.csv")
        if os.path.exists(csv):
            with open(csv, "r", encoding="utf-8", errors="replace") as fh:
                rows = fh.readlines()
            for line in rows:
                if line.startswith("#") or line.startswith("first_id"):
                    continue
                bits = line.strip().split(",", 3)
                if len(bits) == 4:
                    f, c = int(bits[0]), int(bits[1])
                    id_names[(f, f + c - 1)] = (bits[2], bits[3])

    from scipy.ndimage import distance_transform_edt
    reach = distance_transform_edt(~collide, sampling=(wx1 - wx0) / W)
    collide_hull = reach < (hull_r_m * 0.5)
    del reach

    # THE FLOOR IS KEPT, not just the collision bits, because a slope is a
    # difference between two heights and cannot be read off a boolean.
    # collide is the RAW obstacle map and stays, because the independent
    # in-solid check has to be able to ask a question the planner never asked.
    # KIND AND TRUNK ARE KEPT, for the object signature. A connected component
    # of bare geometry is not an object - a wall that touches a cliff is one
    # blob - but the key byte already says which is which per texel, so the
    # blob can be split where the KIND changes without waiting for render ids.
    # Path Studio's suggestion, and it costs nothing because the data is here.
    return dict(W=W, texel_m=(wx1 - wx0) / W, collide=collide,
                collide_hull=collide_hull, used=None,
                kind=kind, trunk=(key & TRUNK_BIT).astype(bool),
                solid=solid, ids=ids, id_names=id_names, palette=palette,
                map_name=map_name,
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
    return not g["collide_hull"][row, col]


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


def march(g, x, z, dx, dz, goal, limit, squares=None):
    """How far a ray gets, and where it stops.

    Walks the collision map texel by texel - the SAME exact traversal that
    clear_line uses - so a line this calls clear and a later check of that line
    can no longer disagree. They did: a fixed stride skips a texel it only
    clips, and the two tests skipped different ones.
    """
    gx, gz = goal
    tex = g["texel_m"]
    ex, ez = x + dx * limit, z + dz * limit
    # THE SQUARE YOU ARE STANDING IN CANNOT BLOCK YOU.
    #
    # The walk holds the square it occupies, so without this every ray it casts
    # starts inside a 1: it collides at once, rings, sidesteps half a metre,
    # holds THAT square and does it again. Measured on a 30 m route, 1,801 of
    # 1,869 steps were cast from a point whose own square was blocked, and the
    # result was 21 rings in 30 m - a tangle of sub-metre hooks rather than a
    # wrap round anything.
    home_sq = squares.index(x, z) if squares is not None else None
    t_prev = 0.0
    h_prev = None
    for col, row, t in walk_texels(g, x, z, ex, ez):
        if t > limit:
            break
        px, pz = x + dx * t, z + dz * t
        if (px - gx) ** 2 + (pz - gz) ** 2 <= REACH_M ** 2:
            return t, px, pz, True, False              # arrived

        # A SQUARE THAT IS 1 STOPS THE RAY LIKE A WALL DOES, and the check is
        # AHEAD of the move rather than after landing on it.
        #
        # 1 means solid ground OR ground a finished route has used, and neither
        # may be entered - "we cant move in to already blocked areas". Crucially
        # this is a COLLISION and not a death: the walk rings it, takes a
        # tangent and goes round, exactly as it would round a building. Killing
        # the ray outright, which is what this did first, throws away the way
        # round instead of looking for it.
        sq_hit = False
        if squares is not None:
            px_, pz_ = x + dx * t, z + dz * t
            if squares.index(px_, pz_) != home_sq and squares.blocked(px_, pz_):
                sq_hit = True
        if blocked_at(g, col, row) or sq_hit:
            # STOP JUST SHORT OF WHAT WE CANNOT ENTER. Backing off a quarter of
            # a texel keeps the hit point inside the last clear one, which is
            # what the ring is then centred on.
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

# THE BASE RING. Inside it is a win - the owner's rule, and it is the disc the
# view already draws round each base rather than a number invented here.
#
# REACH_M is 12 m and is about a ray noticing the flag; this is about a tank
# having ARRIVED. Aim at the disc, not the mark - Path Studio measured team 2's
# mark at 2.39 m of clearance against a 2.25 m hull radius, so the mark itself
# is very nearly not standable and is the wrong thing to require.
BASE_RING_M = 50.0

# Ground this close to either end is never stamped spent. Every route leaves
# the same base and reaches the same flag, so that ground is common to all of
# them - stamp it and the first route walls in the second.
END_GUARD_M = 45.0

# THE RING IS A CIRCLE IN METRES, expanding half a metre at a time. The owner's
# words: "we draw a ring at that hit point and hit the tangent on both sides. if
# we could not after expanding the ring in .5m steps to max ring size in
# settings... That path is dead."
RING_STEP_M = 0.5
RING_MIN_M = 0.5

# A RING SIZE PER PATH ATTEMPT, not one setting for the whole search.
#
# The owner's idea, and it turns a tuning constant into a route GENERATOR. How
# far the ring may grow decides how the walk treats an obstacle: a small ring
# can only find a tangent close in, so it hugs the corner and takes the squeeze;
# a large one reaches out and swings wide. Those are different roads round the
# same building, and asking for one ring size asks for one of them.
#
# So each attempt is ASSIGNED a size and the catalogue gets its variety from
# the search rather than from luck in the opening bearing - which is worth a
# great deal here, because a chain's outcome was measured as chaotic in that
# bearing: 1.5 degrees took one direction from 11 winning chains to 5.
RING_SET = (1.0, 2.0, 3.0, 5.0, 8.0, 12.0)
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
    """Is this texel one the hull cannot occupy?

    The HULL-GROWN map, not the raw one. "Can the centre line pass" and "can
    the tank pass" are different questions and only the second one matters.
    """
    W = g["W"]
    if col < 0 or row < 0 or col >= W or row >= W:
        return True
    if g["used"] is not None and g["used"][row, col]:
        return True
    return bool(g["collide_hull"][row, col])


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
          ray_cap_m, rings=None, why=None, known=None, seq_out=None):
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
    seq = []                            # landmarks passed, in order, with side
    seen_marks = set()
    keep = landmark_index(g) if known is not None else None

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
        # HAVE WE WALKED THIS ROAD ALREADY? Same landmarks, same order, same
        # sides as a route we already hold means this IS that route so far and
        # it has nowhere to go but the same way. Kill it here rather than pay
        # for four hundred more hops to rediscover it.
        if known is not None:
            for m in running_marks(g, x, z, dx, dz, keep, sig_radius(g)):
                if m not in seen_marks:
                    seen_marks.add(m)
                    seq.append(m)
            if is_prefix_of_known(seq, known):
                if seq_out is not None:
                    seq_out.extend(seq)
                return dead(why, "already walked this road", (x, z))

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
                if seq_out is not None:
                    seq_out.extend(seq)
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
    # EVERY ROAD WE HAVE ALREADY WALKED, as an ordered landmark sequence, so a
    # later bearing retracing one can be killed at the third landmark instead
    # of at the four hundredth hop. Measured on 19_monastery: 46% fewer ray
    # hops and 43% less wall clock, and NOT ONE distinct way round lost - two
    # before and two after. The 69 chains it kills were all the same road.
    known = []
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
        trace, rings, why, seq = [], [], [], []
        got = chain(g, (sx, sz), gxy, to_goal + np.deg2rad(a_deg), max_ring_m,
                    trace, min_gap_m, ray_cap_m, rings, why, known, seq)
        for seg in trace:
            rays.append((seg[0], seg[1], got is not None))
        if got is not None:
            if seq:
                known.append(seq)
            if not too_close(got, pool):
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
    # FULL SCREEN, WITH THE MAP IN THE MIDDLE AND THE CONTROLS EITHER SIDE.
    #
    # Everything used to be drawn ON the map: the status line over the terrain,
    # two legends over the terrain, and every control a keystroke you had to
    # already know. The map is the thing being looked at and it was the thing
    # being covered up.
    # WINDOWED, FILLING THE SCREEN - not exclusive fullscreen. Exclusive takes
    # the display over, hides the title bar and makes alt-tabbing a fight, and
    # this is a tool that gets watched ALONGSIDE nuTerra rather than instead of
    # it. So: a normal resizable window, sized to the desktop WORK AREA, which
    # is the screen minus the taskbar - asked of Windows rather than guessed at
    # with a magic offset.
    # A NORMAL WINDOW, MAXIMISED. Not exclusive fullscreen, and NOT placed by
    # hand either.
    #
    # Placing it myself is what broke it twice. SDL_VIDEO_WINDOW_POS at "0,0"
    # puts the CLIENT area at the top of the display and the title bar above
    # that, off the screen - the owner got no title bar. Setting it after
    # pygame.init() does not take at all, and this desktop has three displays
    # (1920x1080, 1920x1080, 800x480), so a hand-computed position lands on
    # whichever one SDL felt like and gets clamped: I asked for 1920x993 at
    # (0,31) and got 974x1039 at (953,0).
    #
    # Path Studio has had this right all along and the owner sent me to look:
    # tools/path_studio.py sets NO window position, takes a plain fixed SIZE
    # with RESIZABLE, and lets the window manager place it. So does this now -
    # and then asks Windows to MAXIMISE it, which is what "windowed fill
    # screen" actually is: the right monitor, the right work area, the title
    # bar where the window manager knows to put it, and correct at any DPI
    # without a single number computed here.
    # A GL CONTEXT, and the panels on a surface over it.
    #
    # The map is textures and batched buffers now; text stays on the CPU
    # because a glyph atlas would be a day's work to end up worse than
    # pygame's font module. `screen` therefore becomes the OVERLAY SURFACE -
    # every panel, label and readout below still draws to it exactly as it
    # did, and it is uploaded as one texture at the end of the frame.
    pygame.display.gl_set_attribute(pygame.GL_CONTEXT_MAJOR_VERSION, 3)
    pygame.display.gl_set_attribute(pygame.GL_CONTEXT_MINOR_VERSION, 3)
    pygame.display.gl_set_attribute(pygame.GL_CONTEXT_PROFILE_MASK,
                                    pygame.GL_CONTEXT_PROFILE_CORE)
    disp = pygame.display.set_mode((1400, 900),
                                   pygame.OPENGL | pygame.DOUBLEBUF |
                                   pygame.RESIZABLE)
    # BOTH WAYS IN. Run as a script the directory of this file is on the path
    # and there is no `tank_tools` package to import from; imported as a module
    # there is. Testing only the second is how this shipped broken - the same
    # mistake as the entry point that ended up mid-file, and the same lesson:
    # verify it the way it is actually launched.
    try:
        from tank_tools.gl_view import GLView
    except ImportError:
        from gl_view import GLView
    gv = GLView()
    screen = pygame.Surface(disp.get_size(), pygame.SRCALPHA)
    try:
        import ctypes
        hwnd = pygame.display.get_wm_info()["window"]
        ctypes.windll.user32.ShowWindow(hwnd, 3)      # SW_MAXIMIZE
    except Exception:
        pass                                          # a 1400x900 window is fine
    LEFT_W, RIGHT_W = 250, 330
    PANEL_BG, PANEL_LINE = (24, 26, 32), (58, 62, 72)
    # WHOSE TOOL THIS IS. Three sessions run their own windows on this desktop
    # and the owner has asked before which one he is looking at, so the name
    # goes in the caption AND is drawn inside the window - a title bar can end
    # up off the screen, as this one just did.
    OWNER_NAME = "Tank AI work"
    pygame.display.set_caption(f"Ray Studio - {OWNER_NAME} - {map_name}")
    font = pygame.font.SysFont("consolas", 16)

    # The map, once. Everything else is drawn over it each frame.
    # THREE WAYS TO SEE THE GROUND, because "what is this thing" and "can I
    # drive on it" are different questions and the owner has been reading the
    # second while asking the first.
    #
    #   PASSABLE  what the resolver sees: clear or solid, nothing else
    #   KIND      nuTerra's own palette out of the meta - building, fence,
    #             tree, rock, prop, water - so the map reads as a map
    #   ITEM      every object id its own colour, so the boundary between one
    #             building and the next is visible, which is the whole basis
    #             of the elimination rule
    def build_base(mode):
        img = np.zeros((SHOW, SHOW, 3), dtype=np.uint8)
        if mode == 0:
            img[...] = (22, 44, 26)
            img[shown] = (62, 30, 20)
            return img
        sub = slice(None, W - W % SHOW, f)
        kd = g["kind"][sub, sub][:SHOW, :SHOW]
        if mode == 1:
            for k, c in g["palette"].items():
                img[kd == k] = c
            sl = g["solid"][sub, sub][:SHOW, :SHOW]
            tr = (kd == KIND_TREE) & sl
            img[tr] = (235, 235, 90)      # tree-keyed AND solid: rock under bush
            return img
        idm = g["ids"][sub, sub][:SHOW, :SHOW] if g["ids"] is not None else None
        if idm is None:
            img[...] = (40, 40, 46)
            return img
        # A stable pseudo-random colour per id, so neighbouring objects differ.
        h = (idm.astype(np.uint64) * np.uint64(2654435761)) % np.uint64(0xFFFFFF)
        img[..., 0] = ((h >> np.uint64(16)) & np.uint64(255)).astype(np.uint8)
        img[..., 1] = ((h >> np.uint64(8)) & np.uint64(255)).astype(np.uint8)
        img[..., 2] = (h & np.uint64(255)).astype(np.uint8)
        img[idm == 0] = (26, 28, 30)
        return img

    base_mode = 1
    MODE_NAME = {0: "passable", 1: "kind", 2: "item id"}
    base = build_base(base_mode)
    ground_dirty = True
    surf = pygame.surfarray.make_surface(np.transpose(base, (1, 0, 2)))
    N = SHOW                     # the view works in picture texels

    gen = resolve(g, start, goal, ring_max, min_gap, ray_cap)
    nodes, paths, rays, bearing = [], [], 0, SWEEP_FROM_DEG
    rings, deaths = [], []
    astar_paths, astar_msg = [], ""
    tree, tree_msg, tree_follow = None, "", True
    # PACING, AND IT IS TWO SEPARATE THINGS that used to be one.
    #
    # steps_per_frame is HOW MUCH WORK a frame does. At 1 you see every single
    # line drawn as it happens - a ray or a path segment, whichever it is -
    # which is the only way to follow what the search is actually doing.
    #
    # step_delay_ms is HOW LONG to wait before the next one. The search STOPS
    # until that time is up; it does not run on and get drawn late. The frame
    # itself keeps ticking at ~60 Hz regardless, so the window stays responsive
    # and can be dragged and zoomed while the search is crawling.
    steps_per_frame = 1
    step_delay_ms = 0
    # HOW MUCH GROUND A FINISHED ROUTE CLOSES BEHIND IT, in squares, 1 to 5.
    # A square is a metre, so 1 blocks a 3x3 - about a hull - and 5 blocks an
    # 11x11, which is a corridor.
    block_radius = 1
    ring_slot = 0                 # which RING_SET entry this attempt is using
    ring_auto = True              # step to the next size when a path lands
    last_step_ms = 0
    sq_surf = None                # the block overlay, rebuilt only when it moves
    # THE SQUARE MAP IS LOADED AT STARTUP AND OWNED HERE.
    #
    # It used to be created by the branch tree, and the overlay was drawn only
    # when a tree existed - so before pressing [b] there was NO block data on
    # the screen at all. The owner looked at the map, saw nothing, and quite
    # reasonably concluded the blocking was not being used. It was being used;
    # it was never being drawn.
    try:
        squares = Squares(map_name)
        print("squares: %dx%d at %.1f m, %d solid"
              % (squares.n, squares.n, squares.cell_m, int((squares.grid != 0).sum())))
    except Exception as ex:
        squares = None
        print("squares: NOT LOADED - %s" % ex)
    slider_rects = {}             # name -> (rect, lo, hi) from the last frame
    active_slider = None
    astar_class = []              # which homotopy class each route belongs to
    landmark_m2 = LANDMARK_M2
    show_marks = True
    show_blocks = True            # the 1 m block layer, on by default
    # The search results are a different KIND of answer from the rays, so they
    # get their own family of colour and can be read apart at a glance.
    A_COLS = [(90, 170, 255), (120, 220, 255), (80, 140, 235), (150, 200, 255),
              (60, 190, 245), (110, 160, 240), (140, 230, 250), (70, 120, 220)]
    # Routes are coloured by CLASS, not by the order they were found, so two
    # spellings of one road come out the same colour and a genuinely different
    # way round comes out a different one. That is the whole point of the test
    # and it should be visible without reading a number.
    CLS_COLS = [(255, 96, 96), (96, 255, 128), (120, 170, 255), (255, 210, 80),
                (230, 120, 255), (100, 245, 235), (255, 155, 70), (190, 190, 190)]
    running, done, paused = True, False, False

    # THE VIEW, in CELLS. A 1024-cell map squeezed into a window is 1.4 m a
    # pixel, which the owner could not read: "the res is too low to see."
    # view_cells is how much map is on screen, so shrinking it zooms in.
    view_cx, view_cz = 0.0, 0.0
    view_cells = float(N)
    dragging, drag_from = False, (0, 0)
    # Where the square map sits inside the middle column this frame. The
    # helpers below read these, so every overlay lands on the map wherever the
    # map happens to be rather than in the corner of the screen.
    map_ox, map_oy = LEFT_W, 0
    buttons = []                 # (rect, label, key, is_on) rebuilt each frame

    def cell_at_mouse(mx, my, w):
        return (view_cx + (mx - map_ox) / w * view_cells,
                view_cz + (my - map_oy) / w * view_cells)

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
        return (int(map_ox + (cx - view_cx) / view_cells * w),
                int(map_oy + (cz - view_cz) / view_cells * w))

    # THE FRAME'S GEOMETRY, GATHERED THEN DRAWN ONCE.
    #
    # Every line and point in the map goes into these two lists and leaves in
    # a single glDrawArrays. That is the change: 3,858 pygame.draw.line calls
    # measured at 2.5 ms become one buffer upload measured at a fraction of
    # it, and the rescale of the block layer - 14.6 ms, the real cost - stops
    # happening at all because the GPU samples a texture instead.
    LINES = []          # (x0, y0, x1, y1, r, g, b, a, width)
    DOTS = []           # (x, y, r, g, b, a, size)

    def L(p0, p1, col, wid=1.0, alpha=255):
        LINES.append((p0[0], p0[1], p1[0], p1[1],
                      col[0], col[1], col[2], alpha, wid))

    def D(p0, col, size=3.0, alpha=255):
        DOTS.append((p0[0], p0[1], col[0], col[1], col[2], alpha, size))

    def CIRC(centre, rad_px, col, wid=1.0, segs=40, alpha=255):
        """A circle as line segments - it joins the same batch as everything
        else, so a hundred rings still cost one draw call."""
        if rad_px < 1.0:
            return
        a = np.linspace(0.0, 2.0 * np.pi, segs + 1)
        xs = centre[0] + np.cos(a) * rad_px
        ys = centre[1] + np.sin(a) * rad_px
        for k in range(segs):
            LINES.append((xs[k], ys[k], xs[k + 1], ys[k + 1],
                          col[0], col[1], col[2], alpha, wid))

    def flush():
        """Two draw calls: all the lines, then all the points."""
        if LINES:
            arr = np.array(LINES, dtype=np.float32)
            for wid in np.unique(arr[:, 8]):
                m = arr[arr[:, 8] == wid]
                v = np.empty((len(m) * 2, 2), np.float32)
                v[0::2] = m[:, 0:2]
                v[1::2] = m[:, 2:4]
                c = np.repeat(m[:, 4:8] / 255.0, 2, axis=0).astype(np.float32)
                gv.draw("lines", v, c, float(wid))
        if DOTS:
            arr = np.array(DOTS, dtype=np.float32)
            for size in np.unique(arr[:, 6]):
                m = arr[arr[:, 6] == size]
                gv.draw("points", m[:, 0:2],
                        (m[:, 2:6] / 255.0).astype(np.float32), float(size))
        del LINES[:]
        del DOTS[:]

    def map_rect():
        """The square the map is drawn into: the middle column, fitted."""
        sw, sh = screen.get_width(), screen.get_height()
        avail_w = max(80, sw - LEFT_W - RIGHT_W)
        mw = min(avail_w, sh)
        return (LEFT_W + (avail_w - mw) // 2, (sh - mw) // 2, mw)

    while running:
        map_ox, map_oy, w_now = map_rect()
        for e in pygame.event.get():
            if e.type == pygame.QUIT:
                running = False
            elif e.type == pygame.VIDEORESIZE:
                disp = pygame.display.set_mode((max(900, e.w), max(600, e.h)),
                                               pygame.OPENGL |
                                               pygame.DOUBLEBUF |
                                               pygame.RESIZABLE)
                screen = pygame.Surface(disp.get_size(), pygame.SRCALPHA)
            elif e.type == pygame.MOUSEWHEEL:
                # ZOOM TO THE CURSOR: the cell under the mouse must not move.
                # Work out which cell that is, change the zoom, then put the
                # view back so that same cell is still under the pointer -
                # which is what makes a wheel feel like a magnifier rather than
                # a scrollbar.
                # AND THE PUT-BACK HAS TO USE THE SAME ORIGIN AS THE LOOK-UP.
                # cell_at_mouse() was moved onto the map's own origin when the
                # panels went in and these two lines were not, so the zoom
                # anchored to a point exactly the left panel's width away from
                # the pointer. Half a fix is its own bug.
                mx, my = pygame.mouse.get_pos()
                if not (map_ox <= mx < map_ox + w_now and
                        map_oy <= my < map_oy + w_now):
                    continue          # the wheel over a panel is not a zoom
                ax, az = cell_at_mouse(mx, my, w_now)
                view_cells *= 0.85 ** e.y
                view_cells = min(float(N), max(24.0, view_cells))
                view_cx = ax - (mx - map_ox) / w_now * view_cells
                view_cz = az - (my - map_oy) / w_now * view_cells
            elif e.type == pygame.MOUSEBUTTONDOWN and e.button in (1, 2, 3):
                # A CLICK IN A PANEL IS A CONTROL, not a drag of the map. The
                # button posts the SAME key event the keyboard would, so there
                # is one implementation of every action and the panel cannot
                # drift away from what the keys do.
                # A SLIDER IS GRABBED, not clicked once: the value follows
                # the pointer until the button comes up.
                grabbed = None
                for nm, (sr, lo, hi) in slider_rects.items():
                    if sr.collidepoint(e.pos):
                        grabbed = nm
                        frac = (e.pos[0] - sr.x) / max(1, sr.w)
                        val = lo + (hi - lo) * max(0.0, min(1.0, frac))
                        if nm == "delay":
                            step_delay_ms = int(round(val))
                        else:
                            steps_per_frame = max(1, int(round(val)))
                        break
                if grabbed is not None:
                    active_slider = grabbed
                    continue

                hit = None
                for (r, lab, kk, on) in buttons:
                    if r.collidepoint(e.pos):
                        hit = kk
                        break
                if hit is not None:
                    pygame.event.post(pygame.event.Event(pygame.KEYDOWN,
                                                         key=hit, mod=0,
                                                         unicode="", scancode=0))
                elif e.pos[0] >= LEFT_W and e.pos[0] < screen.get_width() - RIGHT_W:
                    dragging, drag_from = True, e.pos
            elif e.type == pygame.MOUSEBUTTONUP and e.button in (1, 2, 3):
                dragging = False
                active_slider = None
            elif e.type == pygame.MOUSEMOTION and active_slider is not None:
                sr, lo, hi = slider_rects[active_slider]
                frac = (e.pos[0] - sr.x) / max(1, sr.w)
                val = lo + (hi - lo) * max(0.0, min(1.0, frac))
                if active_slider == "delay":
                    step_delay_ms = int(round(val))
                else:
                    steps_per_frame = max(1, int(round(val)))
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
                    # WHICH OF THESE ARE ACTUALLY DIFFERENT WAYS ROUND?
                    # Exact test: two routes are the same class when the loop
                    # they make together encloses no landmark.
                    astar_class = []
                    reps = []
                    for q in astar_paths:
                        c = None
                        for ci, rp in enumerate(reps):
                            if same_class(g, q, rp, landmark_m2):
                                c = ci
                                break
                        if c is None:
                            c = len(reps)
                            reps.append(q)
                        astar_class.append(c)
                    astar_msg = ("%d routes, %d distinct ways round "
                                 "(landmark %.0f m2), shortest %.0f m, %.1f s"
                                 % (len(astar_paths), len(reps), landmark_m2,
                                    min(lens, default=0), time.time() - t_a))
                    print(astar_msg)
                elif e.key == pygame.K_k:
                    # HOW BIG IS A LANDMARK. The one honest dial in the route
                    # identity test, and it is in square metres of ground, so
                    # it can be judged by looking rather than by tuning.
                    steps = [16.0, 50.0, 100.0, 250.0, 500.0, 2000.0]
                    landmark_m2 = steps[(steps.index(landmark_m2) + 1)
                                        % len(steps)] if landmark_m2 in steps                         else 100.0
                    astar_class = []
                    astar_msg = "landmark now %.0f m2 - press [a] to re-class"                                 % landmark_m2
                elif e.key == pygame.K_m:
                    show_marks = not show_marks
                elif e.key == pygame.K_o:
                    show_blocks = not show_blocks
                elif e.key in (pygame.K_F1, pygame.K_F2, pygame.K_F3,
                               pygame.K_F4, pygame.K_F5, pygame.K_F6):
                    ring_slot = e.key - pygame.K_F1
                elif e.key == pygame.K_F7:
                    ring_auto = not ring_auto
                elif e.key in (pygame.K_1, pygame.K_2, pygame.K_3,
                               pygame.K_4, pygame.K_5):
                    block_radius = e.key - pygame.K_0
                    if tree is not None:
                        tree.block_radius = block_radius
                elif e.key == pygame.K_b:
                    # THE BRANCH TREE. Every angle at every point, exhaustive,
                    # and drawn as it goes - the owner has been blind to this
                    # search while it was being tuned headless, which is the
                    # one thing he asked not to happen.
                    # THE RING FOR THIS ATTEMPT, from the set rather than
                    # from the global slider.
                    tree = BranchTree(g, start, goal, RING_SET[ring_slot],
                                      min_gap, squares=squares)
                    tree.block_radius = block_radius
                    sq_surf = None
                    tree_msg = "branch tree: running"
                elif e.key == pygame.K_v:
                    base_mode = (base_mode + 1) % 3
                    base = build_base(base_mode)
                    ground_dirty = True
                elif e.key == pygame.K_c:
                    tree_follow = not tree_follow
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

        # THE SWEEP IS PACED BY THE SAME CLOCK as the tree. It used to advance
        # once a frame while the frame blocked for 220 ms, so the delay
        # controlled the whole window rather than the work. Both are gated by
        # step_delay_ms now and the frame is left free.
        now_ms = pygame.time.get_ticks()
        step_due = (now_ms - last_step_ms) >= step_delay_ms
        if not done and not paused and step_due:
            last_step_ms = now_ms
            for _ in range(speed):
                try:
                    nodes, paths, rays, bearing, rings, deaths = next(gen)
                except StopIteration:
                    done = True
                    break

        # STEP THE TREE. A few casts a frame: enough to make progress, few
        # enough that the shape of the search is something a person can follow.
        if tree is not None and not paused and not tree.exhausted \
                and not tree.halted and step_due:
            last_step_ms = now_ms
            last = ""
            for _ in range(steps_per_frame):
                last = tree.step()
                if tree.exhausted:
                    break
            seen_i, half_i, used_i = tree.items.report()
            if tree.halted:
                # A LANDED PATH MOVES THE ASSIGNMENT ON, so pressing [b] again
                # hunts the next road rather than re-running the same one.
                if ring_auto and ring_slot < len(RING_SET) - 1:
                    ring_slot += 1
                rings_n = sum(1 for n in tree.win_chain if n["ring"])
                length = sum(np.hypot(tree.win_chain[k + 1]["pos"][0] - tree.win_chain[k]["pos"][0],
                                      tree.win_chain[k + 1]["pos"][1] - tree.win_chain[k]["pos"][1])
                             for k in range(len(tree.win_chain) - 1))
                tree_msg = ("*** PATH COMPLETE - INSIDE THE BASE RING *** %.0f m, %d pt, %d ring(s) - "
                            "found after %d cast(s). [b] restarts."
                            % (length, len(tree.win_chain), rings_n, tree.casts))
            tree_msg = ("branch tree: %d point(s), %d cast(s), %d path(s), "
                        "depth %d | items %d hit, %d half-settled, %d USED - %s"
                        % (len(tree.points), tree.casts, len(tree.paths),
                           len(tree.stack), seen_i, half_i, used_i,
                           "EXHAUSTED, a proof" if tree.exhausted else last))
            if tree_follow:
                # CENTRED ON THE CURSOR - the end of the ray just cast - not on
                # the top of the stack. The stack top teleports across the map
                # every time the search backs up, which is why following looked
                # broken rather than merely jumpy.
                cur = tree.cursor
                cx = (cur[0] - g["wx0"]) / (g["wx1"] - g["wx0"]) * N
                cz = (g["wz1"] - cur[1]) / (g["wz1"] - g["wz0"]) * N
                view_cx, view_cz = cx - view_cells * 0.5, cz - view_cells * 0.5

        map_ox, map_oy, w = map_rect()
        SW0, SH0 = disp.get_size()
        if screen.get_size() != (SW0, SH0):
            screen = pygame.Surface((SW0, SH0), pygame.SRCALPHA)
        gv.begin(SW0, SH0)
        screen.fill((0, 0, 0, 0))

        # THE GROUND, AS A TEXTURE. Uploaded once; a pan or a zoom is a
        # change of uv on one quad rather than a CPU rescale of the whole
        # image, which was 2.1 ms a frame for a picture that had not changed.
        #
        # The uv rectangle comes from the SAME view numbers to_px uses, so the
        # map and everything drawn on it cannot drift apart - which they did
        # before f26b19f4, from exactly this kind of second mapping.
        if ground_dirty or not gv.has("ground"):
            gv.upload("ground", base)
            ground_dirty = False
        u0, v0 = view_cx / N, view_cz / N
        u1, v1 = (view_cx + view_cells) / N, (view_cz + view_cells) / N
        gv.blit("ground", (map_ox, map_oy, w, w), (u0, v0, u1, v1))

        # AND NOTHING DRAWN ON THE MAP MAY SPILL INTO THE PANELS. Zoomed in,
        # a ring or a route runs far outside the frame; without a clip it was
        # painting over the controls and the readouts.
        screen.set_clip(pygame.Rect(map_ox, map_oy, w, w))

        # THE BLOCK DATA, over the ground and under everything the search drew.
        #
        # Baked-solid squares dim the ground; squares the search has SET are
        # red. That separation is the whole point - "I cant tell if it is
        # actaully setting the 0s to 1s" - and with the two the same colour
        # there was nothing to tell.
        #
        # Rebuilt only when the grid changes. A 1400 square surface every frame
        # is 2 million pixels of nothing new.
        if squares is not None and show_blocks:
            if squares.dirty or not gv.has("blocks"):
                gv.upload("blocks", squares.rgba())
                squares.dirty = False
            gv.blit("blocks", (map_ox, map_oy, w, w), (u0, v0, u1, v1))

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
                CIRC(cpx, r_px, (255, 225, 90) if won else (96, 82, 40), 2 if won else 1)
            if r_px >= 3:
                for (rx, rz, code) in rej:
                    D(to_px(rx, rz, w), REJ_COL[code], (2) * 2.0)
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
                L(apx, tpx, (90, 255, 235), 2)
                D(tpx, (90, 255, 235), (4) * 2.0)
                # and a dim spur to the ring centre, so it stays clear WHICH
                # ring produced this tangent without implying the tank drove
                # through its middle.
                L(cpx, tpx, (70, 110, 105), 1)

        # WHERE THE CHAINS GAVE UP, and on what. A cross per dead chain in the
        # colour of its reason: the picture says at a glance whether the sweep
        # is dying at one wall or in sixty different corners.
        DEATH_COL = {"ring reached max with no tangent": (235, 60, 50),
                     "no tangent a ray could leave": (175, 95, 225),
                     "looped back onto its own ground": (240, 170, 60),
                     "no progress toward the flag": (235, 235, 120),
                     "already walked this road": (120, 120, 130),
                     "ran out of hops": (120, 200, 255)}
        for (_a, reason, wh) in deaths:
            dx_, dz_ = to_px(wh[0], wh[1], w)
            c = DEATH_COL.get(reason, (255, 255, 255))
            L((dx_ - 5, dz_ - 5), (dx_ + 5, dz_ + 5), c, 2)
            L((dx_ - 5, dz_ + 5), (dx_ + 5, dz_ - 5), c, 2)

        # THE LANDMARKS - the things big enough that going round the far side
        # of one counts as a different route. Drawn so the dial is visible:
        # wind it down and the map fills with them, wind it up and only the
        # buildings and the cliff remain.
        if show_marks:
            try:
                sx_, sz_ = object_seeds(g, landmark_m2)
                for mi in range(len(sx_)):
                    mp = to_px(sx_[mi], sz_[mi], w)
                    if -20 <= mp[0] <= w + 20 and -20 <= mp[1] <= w + 20:
                        CIRC(mp, 2, (255, 255, 255), 1)
            except Exception:
                pass

        # THE SEARCH RESULTS, under the ray paths so neither hides the other.
        for i, pth in enumerate(astar_paths):
            col = (CLS_COLS[astar_class[i] % len(CLS_COLS)]
                   if i < len(astar_class) else A_COLS[i % len(A_COLS)])
            for k in range(len(pth) - 1):
                L(to_px(pth[k][0], pth[k][1], w), to_px(pth[k + 1][0], pth[k + 1][1], w), col, 2)
            for (qx, qz) in pth:
                D(to_px(qx, qz, w), col, (3) * 2.0)

        # THE BRANCH TREE ITSELF. Every ray that has been cast, coloured by
        # what became of it, so the search is something to look at rather than
        # a number to be told.
        if tree is not None and tree.halted:
            # ONE PATH, ALONE, WITH THE RINGS THAT SHAPED IT.
            #
            # The whole tree is deliberately NOT drawn here. Ten thousand dead
            # rays behind the answer is how the answer gets lost, and the
            # question being asked is "what did this route actually do" - which
            # is the sequence of collisions, the ring at each one and which
            # hand it took, not the search that found it.
            ch = tree.win_chain
            for k in range(len(ch) - 1):
                L(to_px(ch[k]["pos"][0], ch[k]["pos"][1], w), to_px(ch[k + 1]["pos"][0], ch[k + 1]["pos"][1], w), (120, 255, 170), 3)
            for node in ch:
                if node["ring"] is not None:
                    rx, rz, rr = node["ring"]
                    cpx = to_px(rx, rz, w)
                    rpx = int(m_to_px(rr, w))
                    if rpx >= 2:
                        CIRC(cpx, rpx, (255, 215, 80), 2)
                    L(cpx, to_px(node["pos"][0], node["pos"][1], w), (255, 215, 80), 1)
                    # which hand it took round this one
                    D(to_px(node["pos"][0], node["pos"][1], w), (90, 255, 235) if node["side"] > 0
                                       else (255, 150, 90), (5) * 2.0)
                else:
                    D(to_px(node["pos"][0], node["pos"][1], w), (200, 220, 210), (3) * 2.0)
            if ch:
                # THE BASE RING ITSELF, so "it is inside" is something to see
                # rather than something the status line asserts.
                bpx = to_px(goal[0], goal[1], w)
                br = int(m_to_px(BASE_RING_M, w))
                if br >= 2:
                    CIRC(bpx, br, (120, 255, 170), 2)
                for pt, col, lab in ((ch[0]["pos"], (0, 220, 255), "START"),
                                     (ch[-1]["pos"], (255, 150, 0), "IN THE BASE RING")):
                    q = to_px(pt[0], pt[1], w)
                    CIRC(q, 9, col, 3)
                    screen.blit(font.render(lab, True, col), (q[0] + 12, q[1] - 8))

        elif tree is not None:
            TREE_COL = {TAG_OPEN: (110, 110, 130),
                        TAG_PASS: (90, 240, 130),
                        TAG_FAIL: (170, 60, 55)}
            for pt in tree.points:
                if pt["parent"] is None:
                    continue
                a0 = tree.points[pt["parent"]]["pos"]
                col = TREE_COL.get(pt["tag"], (110, 110, 130))
                if pt["origin"] == ORIGIN_TANGENT:
                    col = (220, 170, 70) if pt["tag"] == TAG_OPEN else col
                L(to_px(a0[0], a0[1], w), to_px(pt["pos"][0], pt["pos"][1], w), col, 1)
            for pt in tree.points:
                r_ = 3 if pt["origin"] == ORIGIN_TANGENT else 2
                col = TREE_COL.get(pt["tag"], (110, 110, 130))
                # A TANGENT OFF AN ITEM THAT IS NOW SETTLED reads differently:
                # magenta where the whole item is USED (both hands decided),
                # cyan where just this hand has already got home.
                if pt["origin"] == ORIGIN_TANGENT and pt.get("item"):
                    if tree.items.used(pt["item"]):
                        col, r_ = (235, 110, 235), 4
                    elif tree.items.side_spent(pt["item"], pt["side"]):
                        col, r_ = (90, 230, 235), 4
                D(to_px(pt["pos"][0], pt["pos"][1], w), col, (r_) * 2.0)
            # THE LIVE BRANCH: where the search is standing right now.
            if tree.stack:
                for k in range(len(tree.stack) - 1):
                    p0 = tree.points[tree.stack[k]]["pos"]
                    p1 = tree.points[tree.stack[k + 1]]["pos"]
                    L(to_px(p0[0], p0[1], w), to_px(p1[0], p1[1], w), (255, 245, 120), 2)
                cur = tree.points[tree.stack[-1]]
                cp = to_px(cur["pos"][0], cur["pos"][1], w)
                CIRC(cp, 6, (255, 255, 255), 2)
                # THE ANGLES ALREADY TRIED HERE - the hit list, drawn. Short
                # spokes off the current point, green won, red lost.
                for aid, tg in cur["tried"].items():
                    th = angle_of(aid)
                    ex = cur["pos"][0] + np.sin(th) * 14.0
                    ez = cur["pos"][1] + np.cos(th) * 14.0
                    L(cp, to_px(ex, ez, w), TREE_COL.get(tg, (150, 150, 160)), 1)
            for q in tree.paths:
                for k in range(len(q) - 1):
                    L(to_px(q[k][0], q[k][1], w), to_px(q[k + 1][0], q[k + 1][1], w), (140, 255, 180), 3)

        # The pooled paths, drawn thick over the top.
        for pth in paths:
            for k in range(len(pth) - 1):
                L(to_px(pth[k][0], pth[k][1], w), to_px(pth[k + 1][0], pth[k + 1][1], w), (120, 255, 160), 3)

        if nodes:
            a0, b0, _ = nodes[-1]
            L(to_px(a0[0], a0[1], w), to_px(b0[0], b0[1], w), (255, 235, 90), 2)
            hx, hz = to_px(b0[0], b0[1], w)
            L((hx - 8, hz), (hx + 8, hz), (255, 255, 255), 1)
            L((hx, hz - 8), (hx, hz + 8), (255, 255, 255), 1)

        for pt, col, lab in ((start, (0, 200, 255), "START  team 1 base"),
                             (goal, (255, 140, 0), "FLAG  team 2 base")):
            px_, pz_ = to_px(pt[0], pt[1], w)
            CIRC((px_, pz_), max(4, int(50.0 / (g["wx1"] - g["wx0"]) * w)), col, 2)
            tag = font.render(f"{lab}  ({pt[0]:.0f}, {pt[1]:.0f})", True, col)
            screen.blit(tag, (px_ + 14, pz_ - 8))

        # THE CROSSHAIR, full width and full height of the map, white, on the
        # point the search is working RIGHT NOW - or on where it finished. It
        # travels as the search travels, and full-length lines mean it can be
        # picked out at any zoom without hunting for a dot among ten thousand
        # rays.
        cross = None
        if tree is not None:
            if tree.halted and tree.win_chain:
                cross = tree.win_chain[-1]["pos"]
            else:
                cross = tree.cursor
        if cross is not None:
            qx, qy = to_px(cross[0], cross[1], w)
            L((map_ox, qy), (map_ox + w, qy), (255, 255, 255), 1)
            L((qx, map_oy), (qx, map_oy + w), (255, 255, 255), 1)
            CIRC((qx, qy), 7, (255, 255, 255), 1)

        screen.set_clip(None)

        # ------------------------------------------------------------------
        # THE PANELS. Controls left, readouts right, map between.
        #
        # Every button posts the KEY it mirrors, so there is exactly one
        # implementation of each action and the panel cannot drift away from
        # what the keyboard does.
        # ------------------------------------------------------------------
        SW, SH = screen.get_width(), screen.get_height()
        buttons = []
        pygame.draw.rect(screen, PANEL_BG, pygame.Rect(0, 0, LEFT_W, SH))
        pygame.draw.rect(screen, PANEL_BG,
                         pygame.Rect(SW - RIGHT_W, 0, RIGHT_W, SH))
        pygame.draw.line(screen, PANEL_LINE, (LEFT_W, 0), (LEFT_W, SH))
        pygame.draw.line(screen, PANEL_LINE, (SW - RIGHT_W, 0), (SW - RIGHT_W, SH))

        def header(x, y, text, wide):
            screen.blit(font.render(text, True, (150, 200, 255)), (x, y))
            pygame.draw.line(screen, PANEL_LINE, (x, y + 18), (x + wide, y + 18))
            return y + 26

        def button(x, y, wpx, label, key, on=False, col=None):
            r = pygame.Rect(x, y, wpx, 22)
            hov = r.collidepoint(pygame.mouse.get_pos())
            bg = (62, 96, 66) if on else ((52, 56, 66) if hov else (38, 41, 49))
            pygame.draw.rect(screen, bg, r, border_radius=3)
            pygame.draw.rect(screen, PANEL_LINE, r, 1, border_radius=3)
            screen.blit(font.render(label, True, col or (225, 228, 235)),
                        (x + 7, y + 3))
            buttons.append((r, label, key, on))
            return y + 26

        def pair(x, y, wpx, la, ka, lb, kb):
            hw = (wpx - 4) // 2
            for r, lab, kk in ((pygame.Rect(x, y, hw, 22), la, ka),
                               (pygame.Rect(x + hw + 4, y, hw, 22), lb, kb)):
                hov = r.collidepoint(pygame.mouse.get_pos())
                pygame.draw.rect(screen, (52, 56, 66) if hov else (38, 41, 49),
                                 r, border_radius=3)
                pygame.draw.rect(screen, PANEL_LINE, r, 1, border_radius=3)
                screen.blit(font.render(lab, True, (225, 228, 235)),
                            (r.x + 7, r.y + 3))
                buttons.append((r, lab, kk, False))
            return y + 26

        def slider(x, y, wpx, name, label, val, lo, hi, fmt="%d"):
            screen.blit(font.render(label + "  " + (fmt % val), True,
                                    (200, 205, 215)), (x, y))
            tr = pygame.Rect(x, y + 18, wpx, 12)
            pygame.draw.rect(screen, (30, 33, 40), tr, border_radius=6)
            pygame.draw.rect(screen, PANEL_LINE, tr, 1, border_radius=6)
            frac = 0.0 if hi <= lo else (val - lo) / float(hi - lo)
            kx = int(tr.x + max(0.0, min(1.0, frac)) * tr.w)
            pygame.draw.rect(screen, (120, 200, 255),
                             pygame.Rect(tr.x, tr.y, kx - tr.x, tr.h),
                             border_radius=6)
            pygame.draw.circle(screen, (235, 245, 255), (kx, tr.y + 6), 6)
            slider_rects[name] = (tr, lo, hi)
            return y + 38

        def checkbox(x, y, wpx, label, key, on):
            r = pygame.Rect(x, y, wpx, 22)
            hov = r.collidepoint(pygame.mouse.get_pos())
            pygame.draw.rect(screen, (52, 56, 66) if hov else (38, 41, 49), r,
                             border_radius=3)
            pygame.draw.rect(screen, PANEL_LINE, r, 1, border_radius=3)
            bx = pygame.Rect(x + 5, y + 5, 12, 12)
            pygame.draw.rect(screen, (20, 22, 28), bx)
            pygame.draw.rect(screen, PANEL_LINE, bx, 1)
            if on:
                pygame.draw.line(screen, (120, 255, 170), (bx.x + 2, bx.y + 6),
                                 (bx.x + 5, bx.y + 9), 2)
                pygame.draw.line(screen, (120, 255, 170), (bx.x + 5, bx.y + 9),
                                 (bx.x + 10, bx.y + 3), 2)
            screen.blit(font.render(label, True, (225, 228, 235)), (x + 23, y + 3))
            buttons.append((r, label, key, on))
            return y + 26

        def readout(x, y, label, value, col=(220, 225, 235)):
            screen.blit(font.render(label, True, (135, 140, 152)), (x, y))
            t = font.render(str(value), True, col)
            screen.blit(t, (SW - 14 - t.get_width(), y))
            return y + 18

        # ---- LEFT: what you can do
        LX, LW = 12, LEFT_W - 24
        y = 12
        # WHOSE WINDOW THIS IS, drawn INSIDE it. The title bar carries the same
        # thing, but a title bar can end up off the screen - it just did - and
        # with three sessions running their own tools the owner has to be able
        # to tell at a glance which one he is looking at.
        screen.blit(font.render("RAY STUDIO", True, (235, 240, 250)), (LX, y))
        y += 18
        screen.blit(font.render(OWNER_NAME, True, (120, 220, 255)), (LX, y))
        y += 18
        screen.blit(font.render(map_name, True, (135, 140, 152)), (LX, y))
        y += 24
        y = header(LX, y, "SEARCH", LW)
        y = button(LX, y, LW, "Branch tree  [b]", pygame.K_b, tree is not None)
        y = checkbox(LX, y, LW, "Lock view to current point", pygame.K_c,
                     tree_follow)
        y = button(LX, y, LW, "Bearing sweep  [r]", pygame.K_r)
        y = button(LX, y, LW, "A* catalogue  [a]", pygame.K_a, bool(astar_paths))
        y = button(LX, y, LW, "PAUSED  [space]" if paused else "Pause  [space]",
                   pygame.K_SPACE, paused)
        y += 4
        y = slider(LX, y, LW, "steps", "Steps per frame", steps_per_frame, 1, 64)
        y = slider(LX, y, LW, "delay", "Frame delay", step_delay_ms, 0, 50,
                   "%d ms")
        # THE BLOCK RADIUS, as five buttons rather than a cycling one: the
        # whole set is visible and the chosen one is lit, so it reads as the
        # dropdown the owner asked for instead of a number you have to click
        # through to see.
        screen.blit(font.render("Block radius (squares)", True, (200, 205, 215)),
                    (LX, y))
        y += 18
        bw = (LW - 16) // 5
        for k in range(1, 6):
            rb = pygame.Rect(LX + (k - 1) * (bw + 4), y, bw, 22)
            on = (block_radius == k)
            hov = rb.collidepoint(pygame.mouse.get_pos())
            pygame.draw.rect(screen, (62, 96, 66) if on else
                             ((52, 56, 66) if hov else (38, 41, 49)), rb,
                             border_radius=3)
            pygame.draw.rect(screen, PANEL_LINE, rb, 1, border_radius=3)
            screen.blit(font.render(str(k), True, (235, 240, 248)),
                        (rb.x + bw // 2 - 4, rb.y + 3))
            buttons.append((rb, str(k), pygame.K_0 + k, on))
        y += 30
        y += 4
        y = header(LX, y, "SEEK RING PER PATH", LW)
        screen.blit(font.render("metres, assigned to this attempt", True,
                                (135, 140, 152)), (LX, y))
        y += 18
        rw = (LW - 20) // 6
        for k, rv in enumerate(RING_SET):
            rr_ = pygame.Rect(LX + k * (rw + 4), y, rw, 22)
            on = (ring_slot == k)
            hov = rr_.collidepoint(pygame.mouse.get_pos())
            pygame.draw.rect(screen, (62, 96, 66) if on else
                             ((52, 56, 66) if hov else (38, 41, 49)), rr_,
                             border_radius=3)
            pygame.draw.rect(screen, PANEL_LINE, rr_, 1, border_radius=3)
            screen.blit(font.render("%g" % rv, True, (235, 240, 248)),
                        (rr_.x + 4, rr_.y + 3))
            buttons.append((rr_, "%g" % rv, pygame.K_F1 + k, on))
        y += 28
        y = checkbox(LX, y, LW, "Next ring on each path", pygame.K_F7, ring_auto)
        y += 8
        y = header(LX, y, "VIEW", LW)
        y = button(LX, y, LW, "Ground: " + MODE_NAME[base_mode] + "  [v]",
                   pygame.K_v)
        y = checkbox(LX, y, LW, "Block layer  [o]", pygame.K_o, show_blocks)
        y = button(LX, y, LW, "Landmarks  [m]", pygame.K_m, show_marks)
        y = button(LX, y, LW, "Fit map  [f]", pygame.K_f)
        y = button(LX, y, LW, "Swap ends  [tab]", pygame.K_TAB)
        y += 8
        y = header(LX, y, "TUNING", LW)
        y = pair(LX, y, LW, "- step", pygame.K_COMMA, "+ step", pygame.K_PERIOD)
        y = pair(LX, y, LW, "- ring", pygame.K_LEFTBRACKET,
                 "+ ring", pygame.K_RIGHTBRACKET)
        y = pair(LX, y, LW, "- gap", pygame.K_MINUS, "+ gap", pygame.K_EQUALS)
        y = button(LX, y, LW, "Landmark %.0f m2  [k]" % landmark_m2, pygame.K_k)
        y += 12
        y = button(LX, y, LW, "QUIT  [q]", pygame.K_q, False, (255, 170, 170))
        screen.blit(font.render("wheel zooms, drag pans", True, (110, 115, 128)),
                    (LX, SH - 24))

        # ---- RIGHT: what it is doing
        RX, RW = SW - RIGHT_W + 12, RIGHT_W - 24
        ry = 12
        ry = header(RX, ry, "STATE", RW)
        ry = readout(RX, ry, "hull", "%.1f m" % hull)
        ry = readout(RX, ry, "ray step", "%.0f m" % ray_cap)
        ry = readout(RX, ry, "seek ring (attempt)", "%.1f m" % RING_SET[ring_slot],
                     (255, 225, 120))
        ry = readout(RX, ry, "min gap", "%.1f m" % min_gap)
        ry = readout(RX, ry, "landmark", "%.0f m2" % landmark_m2)
        ry += 10

        if tree is not None:
            ry = header(RX, ry, "BRANCH TREE", RW)
            seen_i, half_i, used_i = tree.items.report()
            ry = readout(RX, ry, "points", "%d" % len(tree.points))
            ry = readout(RX, ry, "ray casts", "%d" % tree.casts)
            ry = readout(RX, ry, "stack depth", "%d" % len(tree.stack))
            ry = readout(RX, ry, "paths", "%d" % len(tree.paths),
                         (120, 255, 170) if tree.paths else (220, 225, 235))
            ry = readout(RX, ry, "items hit", "%d" % seen_i)
            ry = readout(RX, ry, "  one hand won", "%d" % half_i, (90, 230, 235))
            ry = readout(RX, ry, "  fully USED", "%d" % used_i, (235, 110, 235))
            ry = readout(RX, ry, "block radius", "%d sq" % tree.block_radius)
            if tree.halted:
                ry += 4
                for line in ("*** PATH COMPLETE ***", "inside the base ring"):
                    screen.blit(font.render(line, True, (120, 255, 170)), (RX, ry))
                    ry += 18
            ry += 10

        ry = header(RX, ry, "BLOCK LAYER", RW)
        if squares is None:
            ry = readout(RX, ry, "square map", "NOT FOUND", (255, 150, 150))
        else:
            ry = readout(RX, ry, "squares", "%d x %d" % (squares.n, squares.n))
            ry = readout(RX, ry, "baked solid", "%d" % int((squares.base != 0).sum()))
            ry = readout(RX, ry, "set by routes", "%d" % squares.driven,
                         (255, 90, 90) if squares.driven else (220, 225, 235))
            ry = readout(RX, ry, "layer", "shown" if show_blocks else "hidden")
        ry += 10

        ry = header(RX, ry, "BEARING SWEEP", RW)
        ry = readout(RX, ry, "rays", "%d" % rays)
        ry = readout(RX, ry, "routes", "%d" % len(paths))
        ry = readout(RX, ry, "bearing", "%+.0f" % bearing)
        ry = readout(RX, ry, "state",
                     "DONE" if done else ("PAUSED" if paused else "sweeping"))
        tally = {}
        for (_a, reason, _w) in deaths:
            tally[reason] = tally.get(reason, 0) + 1
        if tally:
            ry += 4
            ry = readout(RX, ry, "chains dead", "%d" % len(deaths))
            for reason, cnt in sorted(tally.items(), key=lambda kv: -kv[1]):
                c = DEATH_COL.get(reason, (255, 255, 255))
                screen.blit(font.render("%3d  %s" % (cnt, reason[:28]), True, c),
                            (RX + 6, ry))
                ry += 16
        ry += 10

        if astar_msg:
            ry = header(RX, ry, "SEARCH CATALOGUE", RW)
            for k0 in range(0, len(astar_msg), 40):
                screen.blit(font.render(astar_msg[k0:k0 + 40], True,
                                        (120, 220, 255)), (RX, ry))
                ry += 16
            ry += 10

        ry = header(RX, ry, "KEY", RW)
        if base_mode == 1:
            kl = [(g["palette"].get(k, (150, 150, 150)),
                   ("terrain", "building", "fence", "tree", "rock", "prop",
                    "water", "other")[k]) for k in range(8)]
            kl.append(((235, 235, 90), "tree AND solid: rock under canopy"))
        else:
            kl = [((190, 55, 45), "ring probe: solid"),
                  ((210, 120, 45), "ring probe: chord blocked"),
                  ((165, 90, 215), "ring probe: no escape"),
                  ((70, 140, 230), "ring probe: gap too tight"),
                  ((90, 255, 235), "tangent / left hand"),
                  ((255, 150, 90), "right hand"),
                  ((255, 225, 90), "ring that found one"),
                  ((235, 110, 235), "item fully used")]
        for col, lab in kl:
            pygame.draw.rect(screen, col, pygame.Rect(RX, ry + 3, 12, 10))
            screen.blit(font.render(lab, True, col), (RX + 20, ry))
            ry += 17

        if tree_msg:
            screen.blit(font.render(tree_msg[:70], True, (255, 245, 120)),
                        (LEFT_W + 10, SH - 22))

        # TWO DRAW CALLS FOR THE WHOLE MAP, then the panels as one texture
        # over the top. The overlay is uploaded every frame because its text
        # changes every frame - but it is panels, not the whole window.
        flush()
        gv.surface_texture("ui", screen)
        gv.blit("ui", (0, 0, SW0, SH0))
        pygame.display.flip()
        # THE FRAME ALWAYS TICKS. The search is paced by step_delay_ms above,
        # NOT by blocking the frame - so the window stays draggable and
        # zoomable however slowly the search is set to crawl. Blocking here was
        # why a 220 ms pace made the whole tool feel frozen.
        pygame.time.wait(16)

    pygame.quit()




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


# ==========================================================================
# WHICH OBSTACLES DID THIS ROUTE PASS? - the homotopy signature
# ==========================================================================
#
# The owner: "we need a test to find out when a path was a winner so we can
# stop trying it over and over. Make a list of all objects hit in the path."
#
# That is the right test and it has a name. Two routes are the SAME ROUTE in
# any sense that matters if they pass the same obstacles on the same sides -
# they are homotopic, deformable into one another without crossing anything.
# Everything else is two spellings of one road.
#
# What it replaces: too_close(), which asks whether two polylines stay within
# REJECT_M of each other. That is a proxy and it is wrong in both directions.
# Two genuinely different ways round a building merge if the building is
# narrow; one road and the same road shifted fifty-six metres count as two.
#
# Measured on 19_monastery, hull-grown obstacles: 1,853 connected components,
# 98 of them over 100 m2. All eight catalogue routes come out with UNIQUE
# object sets and pairwise Jaccard similarity of 0.01 to 0.14 - the closest
# pair being the 1,417 m and 1,521 m routes, which really are near neighbours.
#
# THE LIMIT, and it is why the owner is right that render ids would be better:
# a connected component is not an object. A wall that touches a building is
# one component, and monastery's largest single component is 29% of all solid
# ground - the cliff band - so it appears in nearly every signature and
# carries almost no information. Real per-object ids from the bake would split
# that blob into the models it is actually made of. Until then, components are
# what the geometry alone can tell us, and they already work.

def sig_radius(g):
    """How far either side of the path counts as having passed something.

    DERIVED, not chosen. Path Studio caught this: a free constant here lets a
    threshold back in through the side door, which is the exact thing the
    signature was built to get rid of. The obstacle map is already grown by the
    hull radius, so an obstacle at zero distance is one the hull touches; one
    hull radius further out is an obstacle within a hull-width of the hull.
    That is hull radius plus the erosion, and the erosion IS the hull radius.
    """
    return g["hull"]


def object_map(g):
    """Distinct obstacles, labelled, and NOT spanning a kind boundary.

    Plain connected components merge everything that touches: monastery's
    largest was 29% of all solid ground, one blob of cliff with every wall and
    building that leans on it, appearing in nearly every signature and telling
    us almost nothing.

    Labelling each KIND separately fixes most of that for free, because the key
    byte already carries the kind per texel. A route that hugs the cliff and
    then rounds a building now records (cliff, left), (building, right) instead
    of one meaningless blob. Trunks are labelled separately again: a stamped
    tree is an object whatever canopy it stands in.

    Path Studio's suggestion. Their third option - watershed on the distance
    transform of the solid, split at its necks - is deliberately NOT done here:
    it is a guess at what render ids will say outright, so it waits for them.
    """
    if "objects" not in g:
        # REAL IDS IF THE BAKE HAS THEM. bake_version 2 writes which OBJECT is
        # on top at every texel, so "the same object" means an actual model or
        # tree placement. The component labelling below is the fallback for a
        # v1 bake, and it was always a stand-in: a wall touching a cliff came
        # out as one blob, and monastery's largest was 29% of all solid ground.
        if g.get("ids") is not None:
            g["objects"] = (g["ids"], int(g["ids"].max()))
            return g["objects"]
        from scipy.ndimage import label
        solid = g["collide_hull"]
        lab = np.zeros(solid.shape, dtype=np.int32)
        nxt = 0
        # Each kind its own label space, then trunks on top of that.
        layers = [(solid & (g["kind"] == k) & ~g["trunk"]) for k in range(8)]
        layers.append(solid & g["trunk"])
        for m in layers:
            if not m.any():
                continue
            sub, cnt = label(m)
            lab[m] = sub[m] + nxt
            nxt += cnt
        g["objects"] = (lab, nxt)
    return g["objects"]


def path_signature(g, path, radius_m=None, sided=True):
    """The set of obstacles this route passed, and which side it passed them.

    Sided by default. Without the side, going clockwise round a building and
    going anticlockwise round the same building are one signature - and those
    are the two most obviously different routes there are.

    The side is the sign of the cross product between the direction of travel
    and the bearing to the obstacle's nearest texel: +1 it went by on the left,
    -1 on the right. An obstacle passed on both sides (the route went round it)
    records both, which is itself the correct answer.
    """
    lab, _n = object_map(g)
    W = g["W"]
    rad = int((radius_m if radius_m is not None else sig_radius(g)) / g["texel_m"])
    out = set()
    for k in range(len(path) - 1):
        ax, az = path[k]
        bx, bz = path[k + 1]
        L = np.hypot(bx - ax, bz - az)
        if L < 1e-9:
            continue
        ux, uz = (bx - ax) / L, (bz - az) / L
        for j in range(max(1, int(L / 1.0)) + 1):
            t = j / max(1, int(L / 1.0))
            x, z = ax + (bx - ax) * t, az + (bz - az) * t
            c, r = to_texel(g, x, z)
            r0, r1 = max(0, r - rad), min(W, r + rad + 1)
            c0, c1 = max(0, c - rad), min(W, c + rad + 1)
            win = lab[r0:r1, c0:c1]
            for oid in np.unique(win):
                if oid == 0:
                    continue
                if not sided:
                    out.add(int(oid))
                    continue
                # Nearest texel of that object inside the window decides the
                # side. Cross product z-component of travel x bearing-to-object.
                hits = np.argwhere(win == oid)
                rr, cc = hits[0]
                ox = g["wx0"] + (c0 + cc + 0.5) * g["texel_m"]
                oz = g["wz1"] - (r0 + rr + 0.5) * g["texel_m"]
                side = 1 if (ux * (oz - z) - uz * (ox - x)) > 0 else -1
                out.add((int(oid), side))
    return frozenset(out)


def same_route(sig_a, sig_b, tol=0.6):
    """Are these two the same road? Jaccard over the object sets.

    Not equality: a route that clips one extra kerb is not a new route. The
    threshold is what "same" means and it is the one number here worth tuning
    against what the owner calls two routes when he looks at them.
    """
    if not sig_a or not sig_b:
        return False
    inter = len(sig_a & sig_b)
    union = len(sig_a | sig_b)
    return union > 0 and (inter / union) >= tol


# --------------------------------------------------------------------------
# THE EXACT TEST: do these two routes enclose anything?
# --------------------------------------------------------------------------
#
# The set-of-objects signature with a Jaccard threshold was a PROXY for the
# homotopy class and it smuggled two free numbers back in - a radius and a
# tolerance. Measured, that mattered: the eight-equals-eight agreement it
# first produced held at exactly one radius and one tolerance on a steep
# curve (tol 0.5 -> 8, 0.6 -> 17, 0.75 -> 64) and did not survive either
# improving the object map or deriving the radius. It was a coincidence of
# tuning presented as a cross-validation, which is worse than no result.
#
# The real test has no thresholds in it at all. Two routes between the same
# two points are the SAME class exactly when the closed loop made by running
# one forward and the other backward encloses NO obstacle - that loop can then
# be shrunk to nothing without crossing anything, which is what homotopic
# means. If it encloses even one obstacle, the routes go around opposite sides
# of it and are genuinely different ways.
#
# No radius, no tolerance, no threshold. Just: is anything inside the loop.

def encloses(poly_x, poly_y, px, pz):
    """Even-odd point-in-polygon, vectorised over the points."""
    inside = np.zeros(px.shape, dtype=bool)
    n = len(poly_x)
    j = n - 1
    for i in range(n):
        xi, yi, xj, yj = poly_x[i], poly_y[i], poly_x[j], poly_y[j]
        straddles = (yi > pz) != (yj > pz)
        if straddles.any():
            with np.errstate(divide="ignore", invalid="ignore"):
                xint = (xj - xi) * (pz - yi) / np.where(yj != yi, yj - yi, 1e-30) + xi
            inside ^= straddles & (px < xint)
        j = i
    return inside


def object_seeds(g, min_area_m2=None):
    """One representative point per obstacle worth naming, and its area.

    Anything smaller than the hull's own footprint is gravel, not a landmark:
    a route does not meaningfully go "around" a stone it could not tell from
    the ground. Derived from the hull, not chosen.
    """
    key = "seeds_%.2f" % (min_area_m2 if min_area_m2 is not None else -1)
    if key in g:
        return g[key]
    from scipy.ndimage import sum as ndsum, center_of_mass, label
    lab, n = object_map(g)
    if n == 0:
        g[key] = (np.zeros(0), np.zeros(0))
        return g[key]
    floor_a = min_area_m2 if min_area_m2 is not None else np.pi * (g["hull"] * 0.5) ** 2
    idx = np.arange(1, n + 1)
    area = np.array(ndsum(lab > 0, lab, idx)) * g["texel_m"] ** 2
    big = idx[area >= floor_a]
    # A centroid can fall outside a horseshoe, so take an actual member texel.
    xs, zs = [], []
    flat = lab.ravel()
    order = np.argsort(flat, kind="stable")
    sortedv = flat[order]
    starts = np.searchsorted(sortedv, big, side="left")
    for b, st in zip(big, starts):
        if sortedv[st] != b:
            continue
        p = order[st]
        r, c = divmod(int(p), g["W"])
        xs.append(g["wx0"] + (c + 0.5) * g["texel_m"])
        zs.append(g["wz1"] - (r + 0.5) * g["texel_m"])
    g[key] = (np.array(xs), np.array(zs))
    return g[key]


# HOW BIG SOMETHING HAS TO BE before going round its other side counts as a
# different route. This is the one honest dial in the test and it is in square
# metres of ground, not a similarity score - the owner can look at a building
# and say whether it is a landmark.
#
# 100 m2 is about ten metres across: a building, a walled yard, a rock worth
# naming. Measured on 19_monastery, the choice matters and it is what finally
# separated the two planners honestly:
#
#     landmark    seeds    ray classes    catalogue classes
#       16 m2      3168        20                8
#      100 m2       383         2                8
#      500 m2        92         1                8
#     2000 m2        28         1                6
#
# At any scale where "the other side of it" means anything, the ray sweep is
# finding ONE route and the catalogue is finding eight. At 16 m2 everything is
# a landmark, every wobble is its own class, and the number 20 says nothing.
LANDMARK_M2 = 100.0


def same_class(g, a, b, min_area_m2=LANDMARK_M2):
    """Are these two routes the same way round? Exact, no similarity score.

    Run a forward and b backward to make a closed loop; if it encloses no
    landmark the loop shrinks to nothing and the routes are the same way.

    This REPLACES a Jaccard threshold over object sets, which was a proxy and
    smuggled in two free numbers - a radius and a tolerance. That proxy first
    produced an apparent agreement between the two planners (eight routes each)
    and it did not survive contact: it held at exactly one radius and one
    tolerance on a steep curve, and improving the object map destroyed it. It
    was a coincidence of tuning that looked like a cross-validation, which is
    worse than having no result at all.
    """
    sx, sz = object_seeds(g, min_area_m2)
    if len(sx) == 0:
        return True
    loop = list(a) + list(reversed(b))
    px = np.array([p[0] for p in loop], dtype=float)
    pz = np.array([p[1] for p in loop], dtype=float)
    return not encloses(px, pz, sx, sz).any()


# --------------------------------------------------------------------------
# EARLY ABANDON: stop walking a road we have already walked
# --------------------------------------------------------------------------
#
# The owner's ask was never only "dedup the winners" - it was "stop trying it
# over and over". Deduplicating at hop 400 still pays for all 400 hops. On
# monastery that is a hundred winning chains which the exact test says are a
# hundred spellings of ONE road, every one of them walked to the end.
#
# A chain that has passed the same landmarks, in the same order, on the same
# sides as a route we already have IS that route so far - that is what the
# homotopy class means - and it has nowhere to go but the same way. So it can
# be killed the moment its sequence is a prefix of a known one.
#
# This is a PREFIX test on an ordered sequence, not the set comparison the
# signature uses. Order matters here: two routes that pass the same three
# landmarks in a different order are different roads, and killing one for the
# other would lose a genuine route.

ABANDON_AFTER = 3           # landmarks in common before a chain counts as known


def landmark_index(g, min_area_m2=LANDMARK_M2):
    """Landmark positions and their ids, for the running trace."""
    key = "lmi_%.2f" % min_area_m2
    if key in g:
        return g[key]
    from scipy.ndimage import sum as ndsum
    lab, n = object_map(g)
    idx = np.arange(1, n + 1)
    area = np.array(ndsum(lab > 0, lab, idx)) * g["texel_m"] ** 2
    # A FLOOR, NEVER A CEILING. Path Studio's catch: a route that goes round
    # the cliff band the other way encloses a 54,000 m2 object, and excluding
    # the big ones would delete exactly the case worth detecting.
    keep = np.zeros(n + 1, dtype=bool)
    keep[1:] = area >= min_area_m2
    g[key] = keep
    return g[key]


def running_marks(g, x, z, ux, uz, keep, radius_m):
    """Which landmarks are beside this point, and on which hand."""
    lab, _n = object_map(g)
    W = g["W"]
    rad = int(radius_m / g["texel_m"])
    c, r = to_texel(g, x, z)
    r0, r1 = max(0, r - rad), min(W, r + rad + 1)
    c0, c1 = max(0, c - rad), min(W, c + rad + 1)
    win = lab[r0:r1, c0:c1]
    out = []
    for oid in np.unique(win):
        if oid == 0 or not keep[oid]:
            continue
        hits = np.argwhere(win == oid)
        rr, cc = hits[0]
        ox = g["wx0"] + (c0 + cc + 0.5) * g["texel_m"]
        oz = g["wz1"] - (r0 + rr + 0.5) * g["texel_m"]
        out.append((int(oid), 1 if (ux * (oz - z) - uz * (ox - x)) > 0 else -1))
    return out


def is_prefix_of_known(seq, known):
    """Has this chain retraced the opening of a road we already have?"""
    if len(seq) < ABANDON_AFTER:
        return False
    t = tuple(seq)
    for k in known:
        if len(k) >= len(t) and tuple(k[:len(t)]) == t:
            return True
    return False


def object_at(g, x, z, dx, dz):
    """Which object stopped a ray that stopped here.

    Looks just PAST the backed-off hit point, along the direction of travel:
    march() stops a quarter texel short of the texel it cannot enter, so the
    obstacle is the next one along, not the one under the hit.
    """
    lab, _n = object_map(g)
    tex = g["texel_m"]
    for k in range(1, 5):
        c, r = to_texel(g, x + dx * tex * k * 0.5, z + dz * tex * k * 0.5)
        if 0 <= c < g["W"] and 0 <= r < g["W"] and lab[r, c] != 0:
            return int(lab[r, c])
    return 0


def ring_branch(g, hx, hz, from_xz, indx, indz, obj, max_ring_m, goal,
                min_gap_m, claimed, squares=None):
    """Both ways round one obstacle, each found by growing the ring on its own.

    Two things this does that ring_tangents does not, and the trial run needed
    both:

    EACH SIDE GROWS INDEPENDENTLY. ring_tangents returns at the first radius
    where EITHER hand clears, so a collision almost always yielded ONE tangent
    and the "tree" came out as a 2,996-deep chain with a branching factor of
    1.00. A branch search with one branch is a walk.

    AND "ESCAPE" MEANS PAST THIS OBJECT, not six metres. The old test asked
    whether a ray could travel TANGENT_ESCAPE_M from the tangent; a tangent
    half a metre round a building passes that easily and then re-aims straight
    back into the same building. Measured: every ray travelled a median 6.26 m
    - the escape distance exactly - and 3,000 of them advanced 65 m of 785.
    The ring now grows until a ray at the flag leaves the tangent WITHOUT
    hitting the object we are going round. That is what "expand until it
    doesn't hit anything" was always asking for.
    """
    gx, gz = goal
    base_ang = np.arctan2(indx, indz)
    fx, fz = from_xz
    out = {}
    for side in (1, -1):
        if obj and (obj, side) in claimed:
            continue                       # this hand is spent
        r = RING_MIN_M
        while r <= max_ring_m + 1e-6 and side not in out:
            a = RING_ANGLE_STEP
            while a <= RING_ARC_MAX:
                th = base_ang + a * side
                px, pz = hx + np.sin(th) * r, hz + np.cos(th) * r
                a += RING_ANGLE_STEP
                if not standable(g, px, pz):
                    continue
                if squares is not None and squares.blocked(px, pz) and                         squares.index(px, pz) != squares.index(fx, fz):
                    continue          # cannot anchor on ground already spent,
                                      # but our own square is not "spent"

                if not clear_line(g, fx, fz, px, pz):
                    continue
                d2 = max(np.hypot(gx - px, gz - pz), 1e-6)
                ux, uz = (gx - px) / d2, (gz - pz) / d2
                t2, ex, ez, reached, blocked = march(g, px, pz, ux, uz, goal,
                                                     min(d2 + REACH_M, 120.0),
                                                     squares)
                if blocked and obj and object_at(g, ex, ez, ux, uz) == obj:
                    continue               # still stuck on the same thing
                if t2 < TANGENT_ESCAPE_M and not reached:
                    continue
                wide, _, _ = measure_gap(g, px, pz, ux, uz, min_gap_m)
                if wide < min_gap_m:
                    continue
                out[side] = (px, pz, r)
                break
            r += RING_STEP_M
    return out


# ==========================================================================
# THE ONE-METRE SQUARES
# ==========================================================================
#
# The owner's design, built by nuTerra (nuTerra/Tanks/TankSquares.vb) and read
# here straight off disk. A flat byte per square: 1 solid, 0 open.
#
#   "fit squares 1m apart... divide and round our position in xz and use that
#    to find the [1 m] cube in the map. If its 1 its a collision."
#
# Finding your square is arithmetic, not a search - which is the whole reason
# this replaced the discs that were cut out at aa100c09. Those were maximal
# free-space circles with a neighbour graph: 770 ms to build, a nearest-disc
# query to use, and they did not pay.
#
# AND IT IS ALSO THE MEMORY OF WHERE WE HAVE DRIVEN. A completed route sets the
# last square it stood in to 1, so the next search cannot come home the same
# way. One array answers "is this solid" and "has this been used" with the same
# byte, which is why a reset RELOADS THE FILE rather than clearing a flag - the
# pristine copy is on disk and the working copy has been written on.


class Squares(object):
    """The 1 m square map: collision, and where we have already driven."""

    def __init__(self, map_name):
        base = os.path.join(FLIGHT, map_name + "_squares")
        meta = read_meta(base + ".txt")
        self.n = int(meta["n"])
        self.cell_m = float(meta["cell_m"])
        self.wx0 = float(meta["wx_min"])
        # ROW 0 IS THE NORTH EDGE, not the south one.
        #
        # TankSquares walks the bake's own texel rows, and bake row 0 is at
        # wz_MAX - so the square rows count DOWN from the north edge exactly
        # like to_texel does. Reading them upward from wz_min mirrored the
        # whole collision map in z, and it did not look broken: 77% of samples
        # still agreed with the fine map by luck, because most of the map is
        # open either way. Measured against the fine map, the flip agrees 97.6%
        # and this way round agrees 77.0% - which is what settled it.
        # EITHER LABEL. Files written before the meta was corrected say
        # wz_min and mean the opposite edge; new ones say wz_max and mean it.
        # Accepting both means an old file on disk still reads correctly
        # instead of throwing a KeyError the next time the bake is cut.
        if "wz_max" in meta:
            self.wz1 = float(meta["wz_max"])
        else:
            self.wz1 = (float(meta["wz_min"]) +
                        int(meta["n"]) * float(meta["cell_m"]))
        self.path = base + ".u8"
        self.grid = None
        self.reload()

    def reload(self):
        """Back to what nuTerra baked. A reset has to do this."""
        self.grid = np.fromfile(self.path, dtype=np.uint8).reshape(self.n, self.n)
        # THE PRISTINE COPY IS KEPT, not just reloaded from.
        #
        # "I cant tell if it is actaully setting the 0s to 1s" - and there was
        # no way to: once a square is 1 it looks the same whether it was baked
        # solid or set by a finished route. Holding the baked grid beside the
        # working one makes the difference visible, and it is the only evidence
        # that the memory is doing anything at all.
        self.base = self.grid.copy()
        self.driven = 0
        self.dirty = True

    def index(self, x, z):
        """Divide and round. The owner's words, and it is the whole lookup."""
        return (int((self.wz1 - z) / self.cell_m),
                int((x - self.wx0) / self.cell_m))

    def blocked(self, x, z):
        r, c = self.index(x, z)
        if r < 0 or c < 0 or r >= self.n or c >= self.n:
            return True
        return self.grid[r, c] != 0

    def release(self, x, z):
        """Give a square back. Used when a branch backs out of it.

        A square held by the branch currently being walked is not spent - it is
        merely occupied. If it stays 1 after the branch dies, ground that was
        only bad because of a wrong turn further up is closed for ever, and the
        search walls itself into a corner it dug.
        """
        r, c = self.index(x, z)
        if 0 <= r < self.n and 0 <= c < self.n and self.base[r, c] == 0                 and self.grid[r, c] != 0:
            self.grid[r, c] = 0
            self.driven -= 1
            self.dirty = True

    def rgba(self):
        """The block data as an RGBA byte stream, ready to upload as a layer.

        ZERO ALPHA WHERE THE GROUND IS OPEN, so the map shows through
        untouched; solid where it is blocked. That is what a fragment shader
        would do with this array and it is the only honest way to see it -
        anything drawn over the open ground as well is a tint, not a mask.

        Baked-solid and set-by-a-route are DIFFERENT COLOURS. With one colour
        there is no way to tell whether the memory is doing anything, which is
        the exact complaint that produced this.
        """
        n = self.n
        img = np.zeros((n, n, 4), dtype=np.uint8)
        was = self.base != 0
        now = (self.grid != 0) & ~was
        img[was] = (10, 12, 20, 190)        # baked solid: nearly opaque
        img[now] = (255, 60, 60, 220)       # set by a finished route
        return img

    def mark(self, x, z, rad=1):
        """Set this square AND its surround to 1.

        "we make all surrounding that block are 1's as well. I want a way to
        set that. drop down 1 to 5."

        The radius is in squares, and a square is a metre, so rad 1 blocks a
        3x3 - about a hull - and rad 5 blocks an 11x11, which is a corridor.
        A single square is too small to close a way home: the next search steps
        round it without noticing, which is no memory at all.
        """
        r, c = self.index(x, z)
        r0, r1 = max(0, r - rad), min(self.n, r + rad + 1)
        c0, c1 = max(0, c - rad), min(self.n, c + rad + 1)
        if r1 <= r0 or c1 <= c0:
            return 0
        patch = self.grid[r0:r1, c0:c1]
        n_new = int((patch == 0).sum())
        patch[:] = 1
        self.driven += n_new
        self.dirty = True
        return n_new


# ==========================================================================
# THE BRANCH TREE - every angle, at every point
# ==========================================================================
#
# The owner's structure. Spec and history: tank_tools/BRANCH_SEARCH.md, which
# is a LIVING document - if this code and that file disagree, one is a bug.
#
# The first attempt branched only at COLLISIONS and it does not work: branching
# factor 1.00, a 2,996-deep chain, 4,000 rays and no route. The structure is
# per-POINT.
#
#   "each angle is an id.. we need a list that grows with each new ray angle
#    tried tagged as fail or pass"
#   "After we save this, we will back up, check if the point came from tangent
#    intersection or not. If it was just a continuation of move, we try ray
#    cast rays except for ones that won or lost."
#
# So a point reached by simply moving is STILL a branch point: we can leave it
# in any direction we have not already tried there. The per-point angle list is
# what makes that terminate instead of looping - it is the memory of the search.
#
# This is exhaustive by design. It will run for a long time. That is the point:
# "we are creating every possible solution for every ray at every point."

# How far one ray may fly before it counts as a move rather than a journey.
# Open in the spec (BRANCH_SEARCH.md, "still to settle"): distance travelled is
# the honest measure and hop count is the cheap one. This is the honest one.
WALK_BUDGET_M = 450.0

# ONLY CAST FORWARD. "you cast rays only in front of you. no need to sweep
# already travel areas."
#
# This does two things at once and the second is the one that matters. It cuts
# the fan at every point from the whole circle to an arc - and it means the
# walk never looks BACK at the ground it is standing on and has already marked,
# which is what turned a 30 m route into 21 rings of sub-metre hooks: 1,801 of
# 1,869 steps were cast from a point whose own square the walk had just held.
#
# Casting behind you is not exploration. It is re-asking a question you have
# already answered, and on this search it was most of the work.
FORWARD_ARC_DEG = 90.0

# TWENTY RAYS, AND ONLY FORWARD. The owner's numbers.
#
# Twenty across a 180 degree forward arc is 9.5 degrees apart. That is honest
# at this scale: measured earlier, at a 3 m step two rays 6 degrees apart land
# 31 cm apart and the tank is 4.5 m wide, so sixty rays round the whole circle
# was generating about sixteen times more branches than the geometry can tell
# apart - and half of them pointed backwards.
#
# An angle id is now an offset WITHIN THIS POINT'S ARC, 0..19, not a compass
# bearing. That is the right space for it: the hit list is per point, and what
# it needs to record is which of the ways forward FROM HERE have been tried.
RAYS_PER_POINT = 20
N_ANGLES = RAYS_PER_POINT

TAG_OPEN, TAG_PASS, TAG_FAIL = "OPEN", "PASS", "FAIL"
ORIGIN_ROOT, ORIGIN_TANGENT, ORIGIN_CONTINUE = "ROOT", "TANGENT", "CONTINUE"


def fan_offset(aid):
    """Angle id -> offset from the heading, in radians.

    CENTRE OUT: 0 is straight on, then alternately right and left. Iterating
    the ids in order therefore tries the cheapest way first and works outward,
    and the owner's "start scanning left" survives as which hand goes first.
    """
    step = np.deg2rad(2.0 * FORWARD_ARC_DEG / (RAYS_PER_POINT - 1))
    k = (aid + 1) // 2
    return k * step * (1 if aid % 2 else -1) if aid else 0.0


def angle_of(aid):
    """Kept for the spoke drawing, which wants an absolute direction."""
    return fan_offset(aid)


class ItemLedger(object):
    """Every item id ever hit, which hands have won, and which have failed.

    The owner's elimination rule, in his words:

        "If we hit that and it was a win. we can't hit it again. if we do that
         ray is dead."
        "if that item made it to home, it does not mean there isnt a way around
         it. that is why we test tangent on the other side of the ring"
        "if a item has at least one good path and failed going out route, it
         can be marked as used so we stop trying to go around it again."

    So a win does NOT retire an item. It retires ONE HAND of it. The other hand
    still has to be tried, because a route home past the left of a building
    says nothing about whether there is also one past the right.

    An item is USED - dead on contact, no ring, no tangents - only once one
    hand has won and the other has failed. Both are then settled and there is
    nothing further to learn from it.
    """

    def __init__(self):
        self.won = {}       # item id -> set of sides that reached the base
        self.failed = {}    # item id -> set of sides proved dead
        self.order = []     # every item id ever hit, in the order first seen

    def seen(self, item):
        if item and item not in self.won:
            self.won[item] = set()
            self.failed[item] = set()
            self.order.append(item)

    def win(self, item, side):
        self.seen(item)
        self.won[item].add(side)

    def fail(self, item, side):
        self.seen(item)
        self.failed[item].add(side)

    def used(self, item):
        """Both hands settled, at least one of them a win: stop trying."""
        if not item or item not in self.won:
            return False
        w, f = self.won[item], self.failed[item]
        return bool(w) and len(w | f) >= 2

    def side_spent(self, item, side):
        """This hand has already reached the base - a ray retaking it is dead."""
        return bool(item) and item in self.won and side in self.won[item]

    def report(self):
        used = sum(1 for i in self.order if self.used(i))
        half = sum(1 for i in self.order if self.won[i] and not self.used(i))
        return len(self.order), half, used


class BranchTree(object):
    """Every point reached, the angle that reached it, and what it has tried."""

    def __init__(self, g, start, goal, max_ring_m=RING_MAX_DEFAULT_M,
                 min_gap_m=MIN_GAP_DEFAULT_M, walk_m=None, squares=None):
        self.g = g
        self.goal = goal
        self.max_ring_m = max_ring_m
        self.min_gap_m = min_gap_m
        # ONE STEP PER CAST. "we make our step and cast ray left. keep going
        # until we hit something." A ray that is allowed to fly 450 m hits
        # something on this map every single time, so EVERY point came out a
        # TANGENT and not one continuation existed - which deletes half the
        # owner's structure. A step at a time: a clear step makes a CONTINUE
        # point, which is a branch point in its own right, and only a blocked
        # one draws a ring.
        self.walk_m = walk_m or RAY_CAP_DEFAULT_M
        self.points = []
        self.paths = []
        self.items = ItemLedger()
        # STOP AT THE FIRST COMPLETED PATH. The owner wants to look at one
        # whole route - start to finish, with the rings that shaped it -
        # before the tree buries it under ten thousand more rays.
        self.halt_on_first = True
        self.halted = False
        self.win_chain = []
        self.win_rings = []          # (cx, cz, r, side, tangent x, tangent z)
        self.win_pts = []
        # THE SQUARE MAP, if nuTerra has written one. Optional so a v1 bake or
        # a map without it still runs, but it is the ground memory the search
        # was missing and without it the walk circles for ever.
        # HANDED IN, so the viewer and the search share ONE grid and what the
        # search sets is what the overlay draws. Two copies would have looked
        # exactly like a memory that does nothing.
        self.squares = squares
        if self.squares is None:
            try:
                self.squares = Squares(g["map_name"])
            except Exception:
                self.squares = None
        self.last_square = None      # only the last one. "we dont need a list."
        self.block_radius = 1        # squares of surround blocked with it, 1..5
        self.casts = 0
        self.exhausted = False
        # WHERE THE SEARCH ACTUALLY IS, for the view to follow.
        #
        # Following stack[-1] looked wrong because it IS wrong: the top of a
        # depth-first stack teleports the moment the search backtracks, so the
        # view jumped across the map instead of travelling with the walk. The
        # cursor is the end of the ray just cast, which moves the way the
        # search moves and only jumps where the search genuinely jumps.
        self.cursor = start
        self.plugged = 0             # cul-de-sacs filled in as they were found
        # PLUGGING IS OFF BY DEFAULT, and the measurement is why.
        #
        # Four targets, plugs off against on: north 150 identical, east 300
        # identical, base to base BETTER (3,185 casts to 1,676), north 400
        # TWICE AS BAD (4,181 to 9,169, and the route 7,122 m to 14,132 m).
        # One better, one much worse, two unchanged - which is not a rule
        # working, it is a chaotic search being nudged.
        #
        # And there is a reason it cannot work as stated: an instant out is not
        # a property of the GROUND, it is a property of the ground AND THE
        # HEADING. "No way forward from here" was asked facing one way; a
        # cul-de-sac approached from the north can be a through route
        # approached from the west. Plugging the square throws that away, and
        # north 400 is what that costs.
        #
        # Kept, off, and switchable - the idea is sound and would work against
        # a (square, heading) record rather than a square.
        self.plug_dead_ends = False
        # The opening bearing is the owner's "start scanning left": the root
        # begins its angle order there and works round.
        # The root has nothing behind it, so its arc is centred on the sweep's
        # opening bearing - which is what "start scanning left" sets.
        to_goal = np.arctan2(goal[0] - start[0], goal[1] - start[1])
        self.open_heading = to_goal + np.deg2rad(SWEEP_FROM_DEG)
        root = self.add(None, start, None, ORIGIN_ROOT, self.open_heading)
        self.stack = [root["id"]]

    def add(self, parent, pos, angle_in, origin, heading=0.0):
        p = dict(id=len(self.points), pos=pos, angle_in=angle_in,
                 parent=parent, origin=origin, tried={}, tag=TAG_OPEN,
                 kids=[], hit=None, item=None, side=None, ring=None,
                 heading=heading)
        self.points.append(p)
        if parent is not None:
            self.points[parent]["kids"].append(p["id"])
        return p

    def next_angle(self, p):
        """The next untried angle IN FRONT OF US, nearest the heading first.

        THE HIT LIST IN USE. Every angle already tried here - won or lost - is
        skipped, which is what stops the search re-casting a ray it has already
        settled and what lets backing up terminate rather than loop.

        AND ONLY FORWARD. The arc is centred on the angle that REACHED this
        point, so "forward" means the way we were going, not a compass
        direction. The root has no incoming angle and uses the sweep's opening
        bearing instead.

        Order is straight-on first, then alternately left and right, so the
        cheapest answer is tried before the expensive ones and the owner's
        "start scanning left" survives as the tie-break.
        """
        # STRAIGHT ON FIRST, then alternately out. Iterating the ids in
        # order does that, because fan_offset() is laid out centre-out.
        #
        # TRYING THE RAY NEAREST THE FLAG FIRST WAS TRIED AND IS WORSE. It
        # sounds obviously right and it is not: depth first then drives
        # straight at the goal and into every cul-de-sac between here and it,
        # and this map is full of them. Measured, base to base: 5,732 m and a
        # second route before exhausting, against 10,483 m - 13.3x the direct
        # line - and exhausted after ONE. Keeping the heading is what lets a
        # walk follow a wall round to where it opens.
        for aid in range(RAYS_PER_POINT):
            if aid not in p["tried"]:
                return aid
        return None

    def chain_to(self, pid):
        """The whole route from the root down to this point, kept start to end."""
        out = []
        k = pid
        while k is not None:
            out.append(self.points[k]["pos"])
            k = self.points[k]["parent"]
        out.reverse()
        return out

    def step(self):
        """One ray cast. Returns a short string describing what happened."""
        if self.halted:
            return "HALTED on the first completed path"
        g, (gx, gz) = self.g, self.goal
        while self.stack:
            p = self.points[self.stack[-1]]
            aid = self.next_angle(p)
            if aid is None:
                # EVERY ANGLE AT THIS POINT HAS BEEN TRIED. Settle it from what
                # its children became and back up a level.
                kid_tags = [self.points[k]["tag"] for k in p["kids"]]
                p["tag"] = TAG_PASS if TAG_PASS in kid_tags else TAG_FAIL
                # A TANGENT POINT THAT FAILED SETTLES THAT HAND of its item.
                # Once one hand has won and the other has failed, the item is
                # USED and no later ray bothers with it again.
                if p["tag"] == TAG_FAIL and p["origin"] == ORIGIN_TANGENT                         and p.get("item"):
                    self.items.fail(p["item"], p["side"])
                # PLUG A DEAD END; GIVE BACK A DEAD BRANCH.
                #
                # "now we can use the instant out to help plug gaps so the path
                # can't get in there again."
                #
                # The two failures are not the same thing and must not be
                # treated the same way:
                #
                # AN INSTANT OUT - every angle tried from here and not one of
                # them produced a child - is a genuine cul-de-sac. The ground
                # itself is the problem, nothing further down. Plug it, and no
                # later branch has to discover it again.
                #
                # A FAILURE WITH CHILDREN is different. This point was fine;
                # what lay beyond it was not, and beyond it may be reachable
                # another way. Keeping it would wall the search into a corner
                # it dug - which is measured, and is why the release exists.
                if self.squares is not None and p["tag"] == TAG_FAIL:
                    if not p["kids"] and self.plug_dead_ends:
                        self.squares.mark(p["pos"][0], p["pos"][1],
                                          self.block_radius)
                        self.plugged += 1
                    else:
                        self.squares.release(p["pos"][0], p["pos"][1])
                self.stack.pop()
                return "backed up from %d" % p["id"]

            p["tried"][aid] = TAG_OPEN
            self.casts += 1
            # FORWARD IS THE WAY WE CAME. The arc is centred on the heading
            # that reached this point, so the fan never sweeps the ground
            # behind us - which is ground we have already walked and marked.
            a = p["heading"] + fan_offset(aid)
            dx, dz = np.sin(a), np.cos(a)
            d = np.hypot(gx - p["pos"][0], gz - p["pos"][1])
            limit = min(d + REACH_M, self.walk_m)
            got, hx, hz, reached, blocked = march(g, p["pos"][0], p["pos"][1],
                                                  dx, dz, self.goal, limit,
                                                  self.squares)
            self.cursor = (hx, hz)

            # REACHING THE BASE IS REACHING THE BASE. "If the past point hits
            # base location, its done."
            #
            # This used to also demand a clear line from the arrival point to
            # the exact base MARK, and that is why finished routes were never
            # signalled: Path Studio measured team 2's mark at 2.39 m of
            # clearance against a 2.25 m hull radius, so the last few metres to
            # the mark clip the clutter round it and the test failed on routes
            # that had plainly got there. Their conclusion was the right one -
            # aim at the disc, not the mark.
            #
            # The winning point is therefore where the ray ACTUALLY got to,
            # inside the base, rather than the mark itself - which also stops
            # the drawing running a final hop through whatever surrounds it.
            # WHERE WE GOT TO IS THE LAST SQUARE WE STOOD IN. The blocked
            # test now lives inside march(), which stops the ray AHEAD of a 1
            # and reports it as a collision - so the ring below goes round used
            # ground the same way it goes round a wall.
            if self.squares is not None:
                self.last_square = (hx, hz)
                # HOLD THE SQUARE WE ARE STANDING ON. Not spent - occupied.
                # Without this the walk re-enters ground it is already on and
                # never runs out of angles, so it never backs up: 16,577 points
                # crowding 81 to a patch and the stack never shrinking once.
                # With it, 352 points, 4.8 to a patch, and the tree unwinds.
                self.squares.mark(hx, hz, 0)

            if np.hypot(gx - hx, gz - hz) <= BASE_RING_M:
                p["tried"][aid] = TAG_PASS
                win = self.add(p["id"], (hx, hz), aid, ORIGIN_CONTINUE, a)
                win["tag"] = TAG_PASS
                self.paths.append(self.chain_to(win["id"]))
                # CLAIM THE HANDS THIS ROUTE USED. Per side, never the whole
                # item: the other way round it is still an open question.
                k = p["id"]
                while k is not None:
                    q = self.points[k]
                    if q.get("item"):
                        self.items.win(q["item"], q["side"])
                    k = q["parent"]
                # THE WINNING CHAIN, as point records rather than positions,
                # so the redraw has the rings and the items too.
                chain, k = [], win["id"]
                while k is not None:
                    chain.append(self.points[k])
                    k = self.points[k]["parent"]
                chain.reverse()
                self.win_chain = chain
                # THE PATH AND ITS RINGS, SAVED TOGETHER. A route without the
                # rings that shaped it cannot be read back - the rings are the
                # WHY of every turn it took, and they were only ever living in
                # the tree that produced them.
                self.win_rings = [(n["ring"][0], n["ring"][1], n["ring"][2],
                                   n["side"], n["pos"][0], n["pos"][1])
                                  for n in chain if n["ring"] is not None]
                self.win_pts = [n["pos"] for n in chain]
                # ONE SQUARE PER COMPLETED ROUTE. The last one we stood in
                # becomes 1, so the next search cannot come home this way and
                # has to find another. Not a trail - the owner was explicit:
                # "just save the last zone we where in. we dont need a list."
                # THE WHOLE ROUTE IS SPENT, not just the square it ended on.
                #
                # "most of the map should be greyed out when all possible paths
                # has been ran" and "It should still be 1 behind any start
                # paths." One square per route cannot grey out a map: it has to
                # be the ground the route actually drove.
                #
                # This does NOT need the list the owner said we do not need -
                # nothing is tracked while walking. The finished chain is
                # already in hand at this point, so the corridor is stamped in
                # one pass at the end.
                # AND THE ENDS ARE SPARED. Every route has to leave the same
                # base and reach the same flag, so the ground around each is
                # common to all of them. Stamping it walls in the start with
                # the FIRST route: measured, the third run found no path and it
                # was not the map - the start square itself had been marked, so
                # the search could not begin. Both earlier versions of this
                # carried a 45 m guard and this one had lost it.
                if self.squares is not None and self.win_pts:
                    step = max(0.5, self.squares.cell_m)
                    a0, b0 = self.win_pts[0], self.win_pts[-1]
                    for k in range(len(self.win_pts) - 1):
                        ax, az = self.win_pts[k]
                        bx, bz = self.win_pts[k + 1]
                        d = np.hypot(bx - ax, bz - az)
                        n_s = max(1, int(d / step))
                        for j in range(n_s + 1):
                            u = j / n_s
                            px, pz = ax + (bx - ax) * u, az + (bz - az) * u
                            if np.hypot(px - a0[0], pz - a0[1]) < END_GUARD_M:
                                continue
                            if np.hypot(px - b0[0], pz - b0[1]) < END_GUARD_M:
                                continue
                            self.squares.mark(px, pz, self.block_radius)
                if self.halt_on_first:
                    self.halted = True
                return "ARRIVED via %d" % aid

            if got < g["texel_m"]:
                p["tried"][aid] = TAG_FAIL      # wedged: this angle goes nowhere
                return "dead angle %d at %d" % (aid, p["id"])

            if not blocked:
                # A CLEAR MOVE. The end of it is a new point, and by the owner's
                # rule it is a branch point in its own right - we can leave it
                # in any direction not already tried THERE.
                # The new point carries the heading that got it there, so
                # its own fan opens forward from here.
                self.add(p["id"], (hx, hz), aid, ORIGIN_CONTINUE, a)
                self.stack.append(self.points[-1]["id"])
                return "moved %.0f m on %d" % (got, aid)

            # A COLLISION. Ring it, both hands, and each tangent is a point.
            obj = object_at(g, hx, hz, dx, dz)
            p["hit"] = (hx, hz)
            self.items.seen(obj)

            # DEAD ON CONTACT. Both hands of this item are settled and one of
            # them got home, so there is nothing left to learn here - no ring,
            # no tangents, this ray is finished.
            if self.items.used(obj):
                p["tried"][aid] = TAG_FAIL
                return "item %d is used - ray dead on %d" % (obj, aid)

            spent = set()
            for side in (1, -1):
                if self.items.side_spent(obj, side):
                    spent.add(side)
            sides = ring_branch(g, hx, hz, p["pos"], dx, dz, obj,
                                self.max_ring_m, self.goal, self.min_gap_m,
                                set((obj, sd) for sd in spent), self.squares)
            made = []
            for side in (1, -1):
                t = sides.get(side)
                if t is None:
                    continue
                # A TANGENT'S FORWARD IS THE WAY IT LEFT THE RING, not the
                # way the blocked ray was pointing.
                th = np.arctan2(t[0] - p["pos"][0], t[1] - p["pos"][1])
                node = self.add(p["id"], (t[0], t[1]), aid, ORIGIN_TANGENT, th)
                node["item"], node["side"] = obj, side
                # THE RING THAT FOUND IT: centre and the radius it had grown
                # to. Kept so a finished path can be redrawn with the rings
                # that shaped it, which is the only way to see WHY it went the
                # way it did rather than just that it did.
                node["ring"] = (hx, hz, t[2])
                made.append(node)
            if not made:
                p["tried"][aid] = TAG_FAIL
                return "no way round on %d at %d" % (aid, p["id"])
            for m in reversed(made):
                self.stack.append(m["id"])
            return "ring gave %d tangent(s) on %d" % (len(made), aid)

        self.exhausted = True
        return "EXHAUSTED"


# THE ENTRY POINT LIVES AT THE END, and it has to.
#
# It was stranded in the middle of the file: code appended after it - the whole
# branch tree - did not exist yet when main() ran, so the viewer died on its
# first frame with a NameError and the owner got no window at all. It survived
# my own testing only because I imported the module fully and THEN called
# main(), which loads every definition first. Running it the way he runs it,
# as a script, never worked.
#
# Anything appended to this file from here goes ABOVE this block.
if __name__ == "__main__":
    main()
