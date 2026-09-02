"""
Radar navigator with COMMITTED-SIDE avoidance, at 5 m above ground level.

The camera rides 5 m over the bare terrain and follows the nominal loop from
19_monastery_plan.csv (x,z only - that plan's altitude was a 26-49 m flight and
is irrelevant here). At 5 m AGL the nominal course drives straight through
buildings and tree lines, so a radar fan is cast every step, and when the course
ahead is not flyable the navigator picks a side ONCE and follows the obstacle
boundary round on that side until the course is reachable again.

Two rules are load bearing:

  the trap rule - a bearing is only accepted if BOTH a near point and a far
                  point along it are clear. Testing only the near point flies
                  into courtyards and alcoves that then have to be reversed out
                  of. Toggle with --no-trap-rule to measure what it buys.

  side commitment - re-deciding left/right every step dithers in front of a long
                  wall. The side is chosen once, from the radar returns, and
                  held until the rejoin test passes.

    python radar_commit.py [--no-trap-rule]
"""

import csv
import math
import os
import sys

import numpy as np
from scipy import ndimage
from PIL import Image, ImageDraw

# --------------------------------------------------------------------------
# Flight envelope
# --------------------------------------------------------------------------

AGL = 4.0            # metres over the ground of whatever terrace we are on
MARGIN = 1.5         # headroom kept under the camera
BLOCK_H = AGL - MARGIN   # 3.5 m - anything shorter is simply flown over
BODY_R = 8.0         # standoff kept from a blocking cell, metres. Doubled from
                     # 4 - at 4 the measured minimum clearance was 3.87 m, which
                     # WAS the dilation, so the camera rode its own safety
                     # margin with nothing spare on top of it.

# Level flight. The camera holds ONE world Y for the whole route instead of
# riding 5 m over whatever the ground does.
#
# This makes the obstacle test trivial and, more importantly, exact: `top`
# already includes terrain, so `top > level - MARGIN` blocks a hill and a bell
# tower with the same comparison and the navigator never needs to know which is
# which. Terrain-following needed obstacle height, which is a DIFFERENCE of two
# baked layers and carries both their errors.
LEVEL_FLIGHT = True
LEVEL_Y = None       # None = auto. A number here forces ONE level for the route.
LEVEL_CLEAR = 6.0    # metres of air over the highest ground the course crosses
LEVEL_STEP = 4.0     # how much to raise the level when the loop will not close
LEVEL_TRIES = 6

# Terraced flight. The camera holds a level and steps to a new one only where
# the ground makes a real jump, instead of either riding every bump or picking
# one height for the whole map.
#
# A single level had to clear the highest GROUND the route crosses, which on
# Abbey put the camera 28 m up and above almost everything - nothing left to
# avoid. Terraces keep it near the ground where the ground is flat, which is
# most of the route, and only climb where the terrain actually climbs.
TERRACED = True
TERRACE_BAND = 6.0    # ground spread a terrace tolerates before it must step
TERRACE_MIN_M = 70.0  # shortest terrace. Without a floor a noisy hillside
                      # shatters the route into a staircase of one-step terraces.
TERRACE_LIFT = 3.0    # metres to raise every terrace by when the loop will not
                      # close. Doubling BODY_R to 8 m sealed the gaps the route
                      # used and it ran to the step limit; more air reopens them.
TERRACE_TRIES = 6

# Push nominal course samples that sit inside an obstacle out to the nearest
# free cell before flying.
#
# OFF, and kept because the finding is worth more than the code. The diagnosis
# was right - at +0 lift, terrace 9 had 23 of its 24 course samples inside an
# obstacle at its own level, because flight_plan routed at altitude and only
# avoided things over 55 m. But repairing them did NOT make the loop close, and
# made everything else worse: minimum clearance 6.84 -> 3.06 m, reversals
# 0 -> 63, length 1355 -> 1804 m. The repaired samples land just barely outside
# the planning mask and the flight then hugs them.
#
# The real fix is upstream - flight_plan should route the nominal course at the
# altitude it will be flown at, instead of being repaired afterwards.
REPAIR_COURSE = False

# Set by main once the level is settled, so score and draw can read it without
# threading it through every signature. One number, constant for a whole run.
FLIGHT_Y = None

STEP = 2.0           # metres per simulation step
NEAR_D = 9.0         # first lookahead point, metres
FAR_D = 22.0         # second lookahead point - the trap test
LOOKAHEAD = 14.0     # metres ahead on the nominal course we aim at

RADAR_FOV = 150.0    # total fan width, degrees
RADAR_RAYS = 41      # rays in the fan
RADAR_RANGE = 90.0   # maximum radar range, metres

# How far a hit must stand above the flight level, AVERAGED over the hit cell
# and the samples behind it, before it counts as a real object.
#
# A single cell at the hit cannot tell a barn from a hillside grazing the
# level - both read as blocked. Sampling PAST the hit separates them: a
# building stands well clear and keeps standing, terrain that merely brushes
# the level stays barely over it all the way through.
SOLID_H = 3.0
POST_SAMPLES = 3

# Standoff is for OBJECTS. Terrain gets its own, much smaller.
#
# Dilating both by 8 m is what sealed Abbey and forced the camera 9 m up: at a
# low terrace most blocked cells are ground, not things, and pushing 8 m back
# from every slope closes every gap on the map. You want to be well clear of a
# barn; you only need to not scrape a hillside you are already above.
OBJECT_MIN_H = 2.0   # stands this far over its own ground -> it is an object
TERRAIN_R = 3.0      # standoff from ground that merely reaches the level

SWEEP_STEP = 4.0     # degrees between candidate bearings
MAX_DETOUR = 700.0   # metres on one detour before the guard fires
MAX_STEPS = 6000

FOLDER = os.path.join(os.environ.get("TEMP", "."), "nuTerra", "flight")
MAP = "19_monastery"
OUT_DIR = os.path.dirname(os.path.abspath(__file__))
OUT_PNG = os.path.join(OUT_DIR, "radar_commit.png")


# --------------------------------------------------------------------------
# The bake - same loader as tools/flight_plan.py, trimmed to what is used here
# --------------------------------------------------------------------------

class Bake:
    def __init__(self, folder, map_name):
        meta = {}
        with open(os.path.join(folder, map_name + "_meta.txt")) as f:
            for line in f:
                line = line.strip()
                if not line or line.startswith("#"):
                    continue
                k, _, v = line.partition("=")
                meta[k] = v

        self.map_name = meta["map"]
        self.w = int(meta["width"])
        self.h = int(meta["height"])
        self.wx_min = float(meta["wx_min"])
        self.wx_max = float(meta["wx_max"])
        self.wz_min = float(meta["wz_min"])
        self.wz_max = float(meta["wz_max"])
        self.empty = float(meta["empty"])

        self.top = self._r32(os.path.join(folder, map_name + "_top.r32"))
        self.floor = self._r32(os.path.join(folder, map_name + "_floor.r32"))

        self.no_data = self.floor <= self.empty + 1.0
        if self.no_data.any():
            fill = float(np.median(self.floor[~self.no_data]))
            self.floor[self.no_data] = fill
            self.top[self.no_data] = fill

        self.obstacle = np.maximum(self.top - self.floor, 0.0)
        self.mx = (self.wx_max - self.wx_min) / self.w
        self.mz = (self.wz_max - self.wz_min) / self.h

    def _r32(self, path):
        with open(path, "rb") as f:
            raw = f.read()
        a = np.frombuffer(raw, dtype="<f4").astype(np.float64)
        return a.reshape(self.h, self.w).copy()

    def world_of(self, col, row):
        return (self.wx_min + (col + 0.5) * self.mx,
                self.wz_max - (row + 0.5) * self.mz)

    def texel_of(self, x, z):
        c = (x - self.wx_min) / self.mx - 0.5
        r = (self.wz_max - z) / self.mz - 0.5
        return c, r

    def sample(self, field, x, z):
        c, r = self.texel_of(x, z)
        ci = int(np.clip(round(c), 0, self.w - 1))
        ri = int(np.clip(round(r), 0, self.h - 1))
        return field[ri, ci]


# --------------------------------------------------------------------------
# Radar
# --------------------------------------------------------------------------
#
# Everything the navigator senses goes through march(). It walks the integer
# grid cell by cell (Amanatides-Woo: always advance whichever axis boundary is
# nearer, so no cell on the line is skipped and none is visited twice) and
# returns the distance at which the ray first enters a blocking cell.
#
# Cells are square (1.367 m), so distance in cell units scales straight to
# metres and the DDA needs no anisotropic fiddling.

class Radar:
    def __init__(self, bake, mask_plan, mask_raw, cell_m, levels=None):
        # levels: list of (level, plan, raw) - one obstacle world per terrace.
        # The mask MUST follow the level. Planning at 28 m and then flying a
        # terrace at 4 m would route the camera round nothing and straight
        # through every building under 28 m on the way.
        self.levels = levels
        self.plan = mask_plan      # blocked, dilated by the body radius
        self.raw = mask_raw        # blocked, undilated - the clip test
        self.level = None
        self.bake = bake
        self.cell = cell_m
        self.h, self.w = mask_plan.shape

    def set_level(self, k):
        if not self.levels:
            return
        k = max(0, min(k, len(self.levels) - 1))
        self.level, self.plan, self.raw = self.levels[k]

    def march(self, x, z, ux, uz, max_m, mask=None):
        """Range in metres to the first blocking cell along the ray, or max_m."""
        m = self.plan if mask is None else mask
        cx, cy = self.bake.texel_of(x, z)      # continuous texel coords
        cx += 0.5
        cy += 0.5                              # cell-corner space: cell i spans [i, i+1)
        # direction in texel space: +x is +col, +z is -row
        dx = ux / self.cell
        dy = -uz / self.cell
        n = math.hypot(dx, dy)
        if n == 0.0:
            return max_m
        dx /= n
        dy /= n

        ix = int(math.floor(cx))
        iy = int(math.floor(cy))
        max_t = max_m / self.cell

        if not (0 <= ix < self.w and 0 <= iy < self.h):
            return 0.0
        if m[iy, ix]:
            return 0.0

        stepx = 1 if dx > 0 else -1
        stepy = 1 if dy > 0 else -1
        if dx != 0.0:
            tmx = ((ix + (1 if dx > 0 else 0)) - cx) / dx
            tdx = abs(1.0 / dx)
        else:
            tmx = float("inf")
            tdx = float("inf")
        if dy != 0.0:
            tmy = ((iy + (1 if dy > 0 else 0)) - cy) / dy
            tdy = abs(1.0 / dy)
        else:
            tmy = float("inf")
            tdy = float("inf")

        while True:
            if tmx < tmy:
                t = tmx
                ix += stepx
                tmx += tdx
            else:
                t = tmy
                iy += stepy
                tmy += tdy
            if t > max_t:
                return max_m
            if not (0 <= ix < self.w and 0 <= iy < self.h):
                return t * self.cell
            if m[iy, ix]:
                return t * self.cell

    def solidity(self, x, z, ux, uz, hit_r):
        """Mean height above the flight level over the hit cell and the next
        POST_SAMPLES cells along the ray.

        This is the object-versus-terrain test. Big number: something is
        standing there. Small number: the ground has drifted a little over the
        level and will drift back.
        """
        if self.level is None:
            return 1e9
        tot = 0.0
        cnt = 0
        for k in range(POST_SAMPLES + 1):
            d = hit_r + k * self.cell
            px, pz = x + ux * d, z + uz * d
            c, r = self.bake.texel_of(px, pz)
            ci = int(min(max(int(round(c)), 0), self.w - 1))
            ri = int(min(max(int(round(r)), 0), self.h - 1))
            tot += float(self.bake.top[ri, ci]) - self.level
            cnt += 1
        return tot / max(cnt, 1)

    def post_samples(self, x, z, ux, uz, hit_r):
        """World positions of the hit cell and the POST_SAMPLES behind it -
        the points solidity() averages over. Drawn so the classification can
        be checked by eye rather than taken on trust."""
        return [(x + ux * (hit_r + k * self.cell),
                 z + uz * (hit_r + k * self.cell))
                for k in range(POST_SAMPLES + 1)]

    def clear(self, x, z, ux, uz, dist):
        """Is the whole segment of length dist flyable?"""
        return self.march(x, z, ux, uz, dist + 1e-6) >= dist - 1e-6

    def fan(self, x, z, heading):
        """The radar return: (bearing, range) for every ray in the fan."""
        half = math.radians(RADAR_FOV) * 0.5
        out = []
        for i in range(RADAR_RAYS):
            a = heading - half + 2.0 * half * i / (RADAR_RAYS - 1)
            r = self.march(x, z, math.cos(a), math.sin(a), RADAR_RANGE)
            out.append((a, r))
        return out


def plan_terraces(bake, nx, nz, extra=0.0):
    """Split the nominal course into stretches of near-constant ground.

    Walk the course accumulating the ground spread. While it stays inside
    TERRACE_BAND the terrain has not really changed and the camera holds its
    level; the moment the spread would exceed it, that is the rapid jump, and a
    new terrace starts at the new ground. TERRACE_MIN_M stops a rough hillside
    turning into a staircase of one-step terraces.

    Returns (terrace index per nominal sample, level per terrace). The level is
    the HIGHEST ground in the terrace plus AGL, not the average - the camera has
    to clear the whole stretch it is holding that height over, so the low end of
    a terrace simply sits further above the ground than the high end. That is
    the cost of being level, and it is bounded by TERRACE_BAND.
    """
    n = len(nx)
    g = np.array([bake.sample(bake.floor, nx[i], nz[i]) for i in range(n)])
    spacing = float(np.mean(np.hypot(np.diff(nx, append=nx[0]),
                                     np.diff(nz, append=nz[0]))))
    min_pts = max(2, int(round(TERRACE_MIN_M / max(spacing, 1e-6))))

    seg_of = np.zeros(n, dtype=int)
    start = 0
    lo = hi = float(g[0])
    seg = 0
    for i in range(1, n):
        nlo = min(lo, float(g[i]))
        nhi = max(hi, float(g[i]))
        if (nhi - nlo) > TERRACE_BAND and (i - start) >= min_pts:
            seg += 1
            start = i
            lo = hi = float(g[i])
        else:
            lo, hi = nlo, nhi
        seg_of[i] = seg

    # The route is a loop, so the last terrace runs into the first. If the tail
    # is too short to stand on its own, fold it into terrace 0 rather than
    # leaving a step at the seam that the camera takes once per lap.
    nseg = seg + 1
    if nseg > 1 and int(np.sum(seg_of == seg)) < min_pts:
        seg_of[seg_of == seg] = 0
        nseg -= 1
        # renumber so the ids stay contiguous
        used = sorted(set(int(v) for v in seg_of))
        remap = {v: k for k, v in enumerate(used)}
        seg_of = np.array([remap[int(v)] for v in seg_of])
        nseg = len(used)

    levels = []
    for k in range(nseg):
        m = seg_of == k
        levels.append(float(g[m].max()) + AGL + extra)
    return seg_of, levels


def plan_flight(bake, nx, nz, two_point, verbose=True):
    """Terraces, obstacle worlds and a radar that can switch between them.

    Lives here, and both radar_commit and export_cam_path call it, because the
    two used to build this separately - which is how an exported path ends up
    flown against a different obstacle set from the one that planned it.

    Raises every terrace together until the loop closes. Raising them as a block
    rather than individually keeps the steps between them where the terrain put
    them; lifting only the offending terrace would invent a step that no ground
    feature justifies.
    """
    cell_m = bake.mx
    extra = 0.0
    for attempt in range(TERRACE_TRIES):
        terrace_of, tlevels = plan_terraces(bake, nx, nz, extra)
        worlds = []
        for lv in tlevels:
            raw_i, plan_i, dist_i, pad_i = build_world(bake, lv)
            worlds.append((lv, plan_i, raw_i, dist_i))

        # Repair the nominal course before flying it.
        #
        # The course came from flight_plan, which planned at altitude and only
        # avoided things over 55 m - so at 4 m over the ground it runs straight
        # through buildings. Measured on Abbey: terrace 9 had 23 of its 24
        # course samples sitting INSIDE an obstacle at that terrace's own level.
        #
        # That is not something the navigator can fly around, because the thing
        # it is trying to reach is the thing in the way; it thrashed for 2944
        # reversals and never closed. Raising the whole route by 9 m hid it.
        # Pushing the unreachable samples out to the nearest free cell fixes the
        # course instead, and leaves the camera where it was asked to be.
        cx, cz = np.array(nx, dtype=float), np.array(nz, dtype=float)
        moved = 0
        free_cache = {}
        for i in range(len(cx) if REPAIR_COURSE else 0):
            k = int(terrace_of[i])
            plan_k = worlds[k][1]
            c, r = bake.texel_of(cx[i], cz[i])
            ci = int(np.clip(round(c), 0, bake.w - 1))
            ri = int(np.clip(round(r), 0, bake.h - 1))
            if not plan_k[ri, ci]:
                continue
            if k not in free_cache:
                free_cache[k] = np.argwhere(~plan_k)
            free = free_cache[k]
            d = (free[:, 0] - ri) ** 2 + (free[:, 1] - ci) ** 2
            fr, fc = free[int(np.argmin(d))]
            cx[i], cz[i] = bake.world_of(fc, fr)
            moved += 1
        if verbose and moved:
            print(f"  repaired {moved} of {len(cx)} course samples that sat "
                  f"inside an obstacle at their own terrace")

        radar = Radar(bake, worlds[0][1], worlds[0][2], cell_m,
                      levels=[(w[0], w[1], w[2]) for w in worlds])

        probe = fly(bake, radar, cx, cz, two_point, record_fans=False,
                    terrace_of=terrace_of)
        if probe["closed"]:
            if verbose and extra > 0.0:
                print(f"  needed +{extra:.0f} m over the whole route to close "
                      f"at a {BODY_R:.0f} m standoff")
            return terrace_of, tlevels, worlds, radar, extra, cx, cz

        if verbose:
            print(f"  will not close at +{extra:.0f} m, raising every terrace "
                  f"by {TERRACE_LIFT:.0f} m")
        extra += TERRACE_LIFT

    return terrace_of, tlevels, worlds, radar, extra, cx, cz


def pick_level(bake, nx, nz):
    """The single Y the camera holds: the highest ground the nominal course
    crosses, plus clearance.

    Off the NOMINAL course rather than the whole map, because the map includes
    the outland mountains and a level above those would be a satellite pass.
    If the route then cannot get round at that height, main raises it - the
    starting guess only has to be close.
    """
    g = [bake.sample(bake.floor, nx[i], nz[i]) for i in range(len(nx))]
    return float(max(g)) + LEVEL_CLEAR


TRAP_STATS = {"object": 0, "terrain": 0}


def bearing_ok(radar, x, z, a, two_point):
    """Accept a bearing?

    THE TRAP RULE. A ray that gets into a gap but cannot get out is a trap - a
    courtyard, an alcove, the slot between two barns. The near point says
    'there is room here'; only the far point says 'there is a way through'.
    With two_point off this degrades to the naive one-point test, which is the
    control run.
    """
    ux, uz = math.cos(a), math.sin(a)
    if not radar.clear(x, z, ux, uz, NEAR_D):
        return False
    if not two_point:
        return True
    if radar.clear(x, z, ux, uz, FAR_D):
        return True

    # The far point is blocked - but by WHAT? Shoot past the hit and average.
    #
    # Terrain that merely grazes the flight level is not a trap, it is the
    # ground doing what ground does, and refusing those bearings is what turned
    # the trap rule from load bearing at a 4 m standoff into a cost at 8 m: the
    # far probe sits 22 m out, and with the dilation widened it lands on a
    # grazing slope constantly. Only a real object closes a bearing.
    r = radar.march(x, z, ux, uz, FAR_D)
    solid = radar.solidity(x, z, ux, uz, r)
    if solid >= SOLID_H:
        TRAP_STATS["object"] += 1
        return False
    TRAP_STATS["terrain"] += 1
    return True


def ang_norm(a):
    while a > math.pi:
        a -= 2.0 * math.pi
    while a < -math.pi:
        a += 2.0 * math.pi
    return a


# --------------------------------------------------------------------------
# The flight
# --------------------------------------------------------------------------

def load_plan(path):
    xs, zs = [], []
    with open(path) as f:
        for row in csv.DictReader(f):
            xs.append(float(row["x"]))
            zs.append(float(row["z"]))
    return np.array(xs), np.array(zs)


def fly(bake, radar, nx, nz, two_point, record_fans=True, terrace_of=None):
    n = len(nx)
    spacing = float(np.mean(np.hypot(np.diff(nx), np.diff(nz))))

    def nom(idx):
        """Nominal course point at a (wrapping, fractional) index."""
        i = idx % n
        i0 = int(math.floor(i))
        f = i - i0
        i1 = (i0 + 1) % n
        return (nx[i0] * (1 - f) + nx[i1] * f, nz[i0] * (1 - f) + nz[i1] * f)

    if terrace_of is not None:
        radar.set_level(int(terrace_of[0]))

    # start on the course, nudged off anything blocking
    sx, sz = nx[0], nz[0]
    c0, r0 = bake.texel_of(sx, sz)
    if radar.plan[int(round(r0)), int(round(c0))]:
        free = np.argwhere(~radar.plan)
        d = (free[:, 0] - r0) ** 2 + (free[:, 1] - c0) ** 2
        rr, cc = free[int(np.argmin(d))]
        sx, sz = bake.world_of(cc, rr)

    x, z = sx, sz
    heading = math.atan2(nz[1] - nz[0], nx[1] - nx[0])

    prog = 0.0            # progress along the course, in nominal-sample units
    mode = "TRACK"
    side = 0
    detour_len = 0.0
    detour_start = None

    path = [(x, z)]
    levels = [radar.level]
    fans = []
    events = []           # (x, z, kind) markers for the picture
    reversals = 0
    trap_entries = 0      # steps that had to turn more than 100 deg
    detours = 0
    guard_fires = 0
    stuck = 0
    skip = 0.0            # extra course distance to write off, set by the guard
    last_adv = 0          # step number at which progress last increased

    total_advance = 0.0
    steps = 0
    closed = False

    while steps < MAX_STEPS:
        steps += 1

        # --- progress: nearest nominal sample in a forward window only, so the
        # loop cannot be "completed" by drifting backwards or circling a point.
        best_i, best_d = prog, 1e18
        for k in range(0, 26):
            i = prog + k
            px, pz = nom(i)
            d = (px - x) ** 2 + (pz - z) ** 2
            if d < best_d:
                best_d, best_i = d, i
        if best_i > prog + 1e-6:
            total_advance += (best_i - prog) * spacing
            prog = best_i
            last_adv = steps

        # The obstacle world follows the terrace. Done off progress along the
        # NOMINAL course rather than off the camera's own position, so a long
        # detour keeps the level of the stretch it is detouring around instead
        # of picking up the level of wherever it happens to have wandered.
        if terrace_of is not None:
            radar.set_level(int(terrace_of[int(prog) % n]))

        if total_advance > (n - 2) * spacing and math.hypot(x - sx, z - sz) < 25.0:
            # Genuinely all the way round: the whole course has been passed and
            # the camera is back at the start. Fly the last few metres home if
            # the run-in is clear, so the loop closes on itself rather than
            # ending on a stub.
            gap = math.hypot(sx - x, sz - z)
            if gap > 1e-3 and radar.clear(x, z, (sx - x) / gap, (sz - z) / gap, gap):
                path.append((sx, sz))
                levels.append(radar.level)
            closed = True
            break

        # --- the aim point: LOOKAHEAD metres further along the course, pushed
        # on if it happens to sit inside a building (the nominal does, often).
        t_idx = prog + (LOOKAHEAD + skip) / spacing
        tx, tz = nom(t_idx)
        pushed = 0
        while pushed < 40:
            c, r = bake.texel_of(tx, tz)
            ci = int(np.clip(round(c), 0, bake.w - 1))
            ri = int(np.clip(round(r), 0, bake.h - 1))
            if not radar.plan[ri, ci]:
                break
            t_idx += 1.0
            tx, tz = nom(t_idx)
            pushed += 1

        want = math.atan2(tz - z, tx - x)
        fan = radar.fan(x, z, heading)
        if record_fans:
            probes = []
            for a_, rng_ in fan:
                if rng_ < RADAR_RANGE - 0.5:
                    ux_, uz_ = math.cos(a_), math.sin(a_)
                    probes.append((rng_,
                                   radar.post_samples(x, z, ux_, uz_, rng_),
                                   radar.solidity(x, z, ux_, uz_, rng_)))
            fans.append((x, z, heading, fan, mode, probes))

        # --- steering ------------------------------------------------------
        if mode == "TRACK":
            if bearing_ok(radar, x, z, want, two_point):
                new_h = want
            else:
                # Blocked. Decide the side ONCE. For each side take the first
                # bearing that passes the trap test, then ask the radar HOW FAR
                # that bearing actually runs, and prefer depth over a small
                # turn.
                #
                # Scoring on the turn alone was the first version and it is what
                # put a hook at the very start of the loop: the nearer opening
                # was a 25 m pocket, the navigator committed to it, followed its
                # wall in, and had to reverse out. Depth is the thing that says
                # 'there is a way round this side'.
                best = {}
                for s in (1, -1):
                    for k in range(1, int(180.0 / SWEEP_STEP) + 1):
                        dth = math.radians(k * SWEEP_STEP)
                        a = want + s * dth
                        if bearing_ok(radar, x, z, a, two_point):
                            depth = radar.march(x, z, math.cos(a), math.sin(a),
                                                RADAR_RANGE)
                            best[s] = (depth, dth)
                            break
                l_open = sum(r for a, r in fan if ang_norm(a - want) > 0.0)
                r_open = sum(r for a, r in fan if ang_norm(a - want) < 0.0)
                if not best:
                    side = 1 if l_open >= r_open else -1
                elif len(best) == 1:
                    side = next(iter(best))
                else:
                    def sc_side(s):
                        depth, dth = best[s]
                        return min(depth, 70.0) - 22.0 * dth
                    side = 1 if sc_side(1) >= sc_side(-1) else -1
                mode = "DETOUR"
                detours += 1
                detour_len = 0.0
                detour_start = (x, z)
                events.append((x, z, "enter", side))
                new_h = None
        else:
            new_h = None

        if mode == "DETOUR" and new_h is None:
            # rejoin as soon as the aim point is directly reachable
            d_t = math.hypot(tx - x, tz - z)
            if radar.clear(x, z, math.cos(want), math.sin(want), min(d_t, FAR_D)) \
               and bearing_ok(radar, x, z, want, two_point) and detour_len > 3.0 * STEP:
                mode = "TRACK"
                events.append((x, z, "rejoin", side))
                new_h = want
            else:
                # follow the boundary: from the bearing we WANT, rotate in the
                # committed direction until something is flyable. That is what
                # hugs the wall instead of bouncing off it.
                new_h = None
                for k in range(0, int(360.0 / SWEEP_STEP)):
                    a = want + side * math.radians(k * SWEEP_STEP)
                    if abs(ang_norm(a - heading)) > math.radians(165.0):
                        continue
                    if bearing_ok(radar, x, z, a, two_point):
                        new_h = a
                        break
                if new_h is None:
                    # boxed in. Take the longest radar return that is flyable at
                    # all, even if it means turning back.
                    cand = sorted(fan, key=lambda ar: -ar[1])
                    for a, r in cand:
                        if radar.clear(x, z, math.cos(a), math.sin(a), min(NEAR_D, r)) and r > NEAR_D:
                            new_h = a
                            break
                    events.append((x, z, "boxed", side))
                    stuck += 1
                    if new_h is None:
                        new_h = heading + math.pi   # reverse out

        if new_h is None:
            new_h = want

        turn = ang_norm(new_h - heading)
        if abs(turn) > math.radians(100.0):
            reversals += 1
            events.append((x, z, "reverse", side))
            if mode == "DETOUR":
                trap_entries += 1

        heading = ang_norm(new_h)

        # --- step, then the guards ----------------------------------------
        nx_, nz_ = x + math.cos(heading) * STEP, z + math.sin(heading) * STEP
        x, z = nx_, nz_
        path.append((x, z))
        levels.append(radar.level)
        if mode == "TRACK":
            skip = 0.0
        else:
            detour_len += STEP
            if detour_len > MAX_DETOUR:
                # the boundary follow has gone a very long way without rejoining
                # - the committed side was the wrong one. This is the only place
                # the commitment is ever revoked.
                guard_fires += 1
                side = -side
                detour_len = 0.0
                events.append((x, z, "guard", side))

        # Loop detector: progress along the course has not increased for a long
        # time, so the camera is circling something.
        #
        # The first version of this flipped the committed side, and that was
        # wrong: flipping mid-follow sends the camera back the way it came, it
        # re-enters the same pocket from the other end, and the result was a
        # tight knot of circles at the start of the loop. Instead, WRITE OFF a
        # stretch of the nominal course - aim further along it - and keep the
        # side. Circling means the piece of course being chased is unreachable,
        # not that the side was wrong.
        if steps - last_adv > 45:
            guard_fires += 1
            skip += 30.0
            last_adv = steps
            events.append((x, z, "guard", side))

    return {
        "path": path, "levels": levels, "fans": fans, "events": events, "closed": closed,
        "reversals": reversals, "trap_entries": trap_entries,
        "detours": detours, "guard_fires": guard_fires, "stuck": stuck,
        "steps": steps, "start": (sx, sz),
    }


# --------------------------------------------------------------------------
# Scoring
# --------------------------------------------------------------------------

def score(bake, radar, res, nx, nz, dist_m, worlds=None):
    """Measure the flight.

    With terraces, every test here has to use the world of the terrace that
    point was flown on. Scoring a 4 m terrace against the 28 m obstacle mask
    would report a clean flight through a building, which is the one failure
    this function exists to catch.
    """
    path = res["path"]
    lv = res.get("levels")
    by_level = {}
    if worlds:
        for (lvl, pl, rw, dm) in worlds:
            by_level[round(float(lvl), 3)] = (rw, dm)

    def world_at(k):
        if lv is not None and k < len(lv) and lv[k] is not None:
            w = by_level.get(round(float(lv[k]), 3))
            if w is not None:
                return w
        return radar.raw, dist_m
    length = sum(math.hypot(path[i + 1][0] - path[i][0], path[i + 1][1] - path[i][1])
                 for i in range(len(path) - 1))

    # clipping: every cell the flown line passes through, against the RAW mask
    clips = 0
    clip_at = []
    for i in range(len(path) - 1):
        x0, z0 = path[i]
        x1, z1 = path[i + 1]
        seg = math.hypot(x1 - x0, z1 - z0)
        if seg <= 0:
            continue
        rw, _ = world_at(i)
        rng = radar.march(x0, z0, (x1 - x0) / seg, (z1 - z0) / seg, seg, mask=rw)
        if rng < seg - 1e-6:
            clips += 1
            clip_at.append(path[i])

    # Deviation from the nominal COURSE, measured to the polyline rather than to
    # its vertices - the samples are 4 m apart and vertex distance flatters a
    # path running alongside the course by up to 2 m.
    A = np.stack([nx, nz], axis=1)
    B = np.roll(A, -1, axis=0)
    AB = B - A
    L2 = (AB ** 2).sum(axis=1)
    L2[L2 == 0] = 1e-9

    dev = []
    clr = []
    alt_margin = []          # camera altitude minus the top surface under it
    for k, (x, z) in enumerate(path):
        AP = np.stack([x - A[:, 0], z - A[:, 1]], axis=1)
        t = np.clip((AP * AB).sum(axis=1) / L2, 0.0, 1.0)
        proj = A + AB * t[:, None]
        dev.append(float(np.hypot(proj[:, 0] - x, proj[:, 1] - z).min()))

        c, r = bake.texel_of(x, z)
        ci = int(np.clip(round(c), 0, bake.w - 1))
        ri = int(np.clip(round(r), 0, bake.h - 1))
        _, dm = world_at(k)
        clr.append(float(dm[ri, ci]))

        # the flight rule itself - everything under the camera must be below it
        if lv is not None and k < len(lv) and lv[k] is not None:
            alt = lv[k]
        elif FLIGHT_Y is not None:
            alt = FLIGHT_Y
        else:
            alt = bake.floor[ri, ci] + AGL
        alt_margin.append(float(alt - bake.top[ri, ci]))

    return {
        "length": length, "clips": clips, "clip_at": clip_at,
        "max_dev": max(dev), "dev": dev, "min_clear": min(clr),
        "min_alt_margin": min(alt_margin),
        "levels_used": sorted(set(round(float(v), 1) for v in (lv or []) if v is not None)),
    }


# --------------------------------------------------------------------------
# Picture
# --------------------------------------------------------------------------

PINK = (255, 46, 168)
NOMINAL = (120, 165, 235)


def _font(size):
    from PIL import ImageFont
    for name in ("segoeui.ttf", "arial.ttf", "DejaVuSans.ttf"):
        try:
            return ImageFont.truetype(name, size)
        except Exception:
            pass
    return ImageFont.load_default()


def draw(bake, res, sc, nx, nz, out_png, worlds=None, terrace_of=None):
    """The obstacle map with the sensor picture on it.

    Shaded by obstacle HEIGHT rather than the binary mask, and split at the
    3.5 m line, because the whole point of flying at 5 m AGL is that a wall the
    camera clears and a barn it cannot are not the same thing and must not look
    the same.
    """
    # In level flight the question is not how TALL a thing is, it is whether it
    # reaches the height the camera holds - so shade by how far each cell rises
    # above or below that line. Shading by obstacle height here would colour a
    # 20 m building on a hilltop the same as one in the valley, and only one of
    # them is in the way.
    if worlds and terrace_of is not None:
        # Terraced: shade every cell against the terrace that actually passes
        # NEAREST to it, not against one level for the whole map.
        #
        # The first version shaded against the lowest terrace on the grounds
        # that it could not under-report. It was useless: at 2.7 m most of
        # Abbey is above the camera, so seventy percent of the picture came out
        # amber and the map read as a solid wall with a route threaded through
        # it. Conservative and uninformative is still wrong.
        n = len(nx)
        idx = np.full((bake.h, bake.w), -1, dtype=np.int32)
        for i in range(n):
            c, r = bake.texel_of(nx[i], nz[i])
            ci = int(np.clip(round(c), 0, bake.w - 1))
            ri = int(np.clip(round(r), 0, bake.h - 1))
            idx[ri, ci] = i
        _, inds = ndimage.distance_transform_edt(idx < 0, return_indices=True)
        nearest = idx[inds[0], inds[1]]
        lut = np.array([w[0] for w in worlds], dtype=float)
        level_img = lut[np.asarray(terrace_of)[nearest]]

        o = bake.top - (level_img - MARGIN)
        cut = 0.0
        span = 20.0
        exists = bake.top > bake.floor + 0.5
    elif FLIGHT_Y is not None:
        o = bake.top - (FLIGHT_Y - MARGIN)
        cut = 0.0
        span = 20.0
        exists = bake.top > bake.floor + 0.5
    else:
        o = bake.obstacle
        cut = BLOCK_H
        span = 30.0
        exists = o > 0.5
    img = np.zeros((bake.h, bake.w, 3), dtype=np.uint8)

    g = bake.floor
    gn = (g - g.min()) / max(1e-6, (g.max() - g.min()))
    img[..., 0] = (14 + 22 * gn).astype(np.uint8)
    img[..., 1] = (18 + 27 * gn).astype(np.uint8)
    img[..., 2] = (26 + 35 * gn).astype(np.uint8)

    # exists, but is FLOWN OVER: fences, low walls, rubble, small rocks
    low = exists & (o <= cut)
    sh = np.clip((o - cut) / -max(span, 1e-6) if FLIGHT_Y is not None
                 else o / max(cut, 1e-6), 0, 1)
    img[low, 0] = (48 + 34 * sh[low]).astype(np.uint8)
    img[low, 1] = (52 + 38 * sh[low]).astype(np.uint8)
    img[low, 2] = (58 + 42 * sh[low]).astype(np.uint8)

    # must be flown AROUND, shaded by height so a hedge and a bell tower read
    # as different obstacles
    hard = o > cut
    t = np.clip((o - cut) / span, 0, 1)
    img[hard, 0] = (146 + 109 * t[hard]).astype(np.uint8)
    img[hard, 1] = (106 + 94 * t[hard]).astype(np.uint8)
    img[hard, 2] = (18 + 42 * t[hard]).astype(np.uint8)

    base = Image.fromarray(img, "RGB")

    # crop to the action, with enough margin that the map still gives context
    allx = [p[0] for p in res["path"]] + list(nx)
    allz = [p[1] for p in res["path"]] + list(nz)
    m = 170.0
    c0, r1 = bake.texel_of(min(allx) - m, min(allz) - m)
    c1, r0 = bake.texel_of(max(allx) + m, max(allz) + m)
    c0 = int(max(0, c0)); r0 = int(max(0, r0))
    c1 = int(min(bake.w, c1)); r1 = int(min(bake.h, r1))
    side_px = max(c1 - c0, r1 - r0)
    c0 = max(0, min(c0, bake.w - side_px)); r0 = max(0, min(r0, bake.h - side_px))

    SC = 1800.0 / side_px
    base = base.crop((c0, r0, c0 + side_px, r0 + side_px)).resize(
        (int(side_px * SC), int(side_px * SC)), Image.LANCZOS)
    W, H = base.size

    def T(x, z):
        c, r = bake.texel_of(x, z)
        return ((c - c0) * SC, (r - r0) * SC)

    im = base.convert("RGBA")

    # --- radar sweeps, on their own layer so they sit UNDER the path and read
    # as sensor returns rather than as geometry
    ov = Image.new("RGBA", im.size, (0, 0, 0, 0))
    do = ImageDraw.Draw(ov)
    fans = res["fans"]
    for k in range(0, len(fans), 16):
        x, z, heading, fan, mode, probes = fans[k]
        a0 = T(x, z)
        for a, rng in fan:
            a1 = T(x + math.cos(a) * rng, z + math.sin(a) * rng)
            hit = rng < RADAR_RANGE - 0.5
            do.line([a0, a1], fill=(255, 205, 105, 78) if hit else (110, 240, 230, 52),
                    width=1)
            if hit:
                do.ellipse([a1[0] - 1.6, a1[1] - 1.6, a1[0] + 1.6, a1[1] + 1.6],
                           fill=(255, 215, 120, 170))

        # The three extra samples taken PAST each hit, drawn in red. This is the
        # object-versus-terrain test made visible: bright red where the average
        # says something is standing there, dim orange where it says the ground
        # has merely drifted over the flight level and will drift back.
        for (rng, spts, solid) in probes:
            solid_hit = solid >= SOLID_H
            tail = [T(px, pz) for (px, pz) in spts]
            do.line(tail, fill=(255, 40, 40, 170) if solid_hit else (255, 150, 90, 70),
                    width=2 if solid_hit else 1)
            for j, pt in enumerate(tail):
                if j == 0:
                    continue
                rr = 2.0 if solid_hit else 1.3
                do.ellipse([pt[0] - rr, pt[1] - rr, pt[0] + rr, pt[1] + rr],
                           fill=(255, 45, 45, 215) if solid_hit else (255, 160, 100, 110))
    im = Image.alpha_composite(im, ov)
    d = ImageDraw.Draw(im)

    # --- the nominal course, dashed and dim, so every deviation reads as one
    npts = [T(nx[i], nz[i]) for i in range(len(nx))]
    npts.append(npts[0])
    for i in range(len(npts) - 1):
        if i % 2 == 0:
            d.line([npts[i], npts[i + 1]], fill=NOMINAL + (215,), width=2)

    # --- the flown path
    pts = [T(x, z) for (x, z) in res["path"]]
    d.line(pts, fill=(58, 0, 38, 255), width=7, joint="curve")
    d.line(pts, fill=PINK + (255,), width=3, joint="curve")

    # --- big detours: ring the stretches that went a long way off course
    dev = sc["dev"]
    run = None
    for i in range(len(dev)):
        if dev[i] > 35.0 and run is None:
            run = i
        elif dev[i] <= 35.0 and run is not None:
            j = run + int(np.argmax(dev[run:i]))
            x, z = res["path"][j]
            px, py = T(x, z)
            rad = 15.0 + dev[j] * SC * 0.30
            d.ellipse([px - rad, py - rad, px + rad, py + rad],
                      outline=(120, 255, 205, 200), width=2)
            d.text((px + rad + 4, py - 8), "%.0f m off course" % dev[j],
                   fill=(150, 255, 215, 235), font=_font(15))
            run = None

    # --- what the navigator did, and where
    for (ex, ez, kind, sd) in res["events"]:
        px, py = T(ex, ez)
        if kind == "enter":
            d.ellipse([px - 5, py - 5, px + 5, py + 5],
                      outline=(255, 175, 55, 255), width=2)
        elif kind == "boxed":
            d.line([(px - 8, py - 8), (px + 8, py + 8)], fill=(255, 70, 70, 255), width=2)
            d.line([(px - 8, py + 8), (px + 8, py - 8)], fill=(255, 70, 70, 255), width=2)
        elif kind == "guard":
            d.ellipse([px - 11, py - 11, px + 11, py + 11],
                      outline=(255, 245, 90, 255), width=2)
        elif kind == "reverse":
            d.ellipse([px - 3, py - 3, px + 3, py + 3], fill=(255, 90, 150, 255))

    for (cx, cz) in sc["clip_at"]:
        px, py = T(cx, cz)
        d.ellipse([px - 7, py - 7, px + 7, py + 7], fill=(255, 0, 0, 255))

    # --- start marker and initial heading
    sx, sz = res["start"]
    px, py = T(sx, sz)
    hx, hz = res["path"][3]
    hl = math.hypot(hx - sx, hz - sz) or 1.0
    hp = T(sx + (hx - sx) / hl * 60.0, sz + (hz - sz) / hl * 60.0)
    d.line([(px, py), hp], fill=(90, 255, 140, 255), width=4)
    d.ellipse([hp[0] - 5, hp[1] - 5, hp[0] + 5, hp[1] + 5], fill=(90, 255, 140, 255))
    d.ellipse([px - 9, py - 9, px + 9, py + 9], fill=(90, 255, 140, 255),
              outline=(255, 255, 255, 255), width=2)
    d.text((px + 13, py - 26), "start / finish", fill=(150, 255, 180, 255), font=_font(16))

    # --- legend
    f = _font(17)
    fb = _font(21)
    rows = [
        (PINK, ("flown terraced, %.0f m over each terrace   %.0f m" % (AGL, sc["length"]))
               if (sc.get("levels_used"))
               else (("flown level at Y = %.1f m   %.0f m" % (FLIGHT_Y, sc["length"]))
                     if FLIGHT_Y is not None
                     else ("flown at %.0f m AGL   %.0f m" % (AGL, sc["length"])))),
        (NOMINAL, "nominal course   1345 m"),
        ((196, 146, 40), "must fly around   reaches its own terrace's level"
                         if (sc.get("levels_used"))
                         else (("must fly around   reaches above Y - %.1f m" % MARGIN)
                               if FLIGHT_Y is not None
                               else ("must fly around   obstacle > %.1f m" % BLOCK_H))),
        ((70, 76, 86), "flown over   everything below that line"
                       if FLIGHT_Y is not None
                       else ("flown over   obstacle < %.1f m" % BLOCK_H)),
        ((110, 240, 230), "radar fan, no return in %.0f m" % RADAR_RANGE),
        ((255, 45, 45), "%d samples past a hit - OBJECT (avg > %.0f m over level)"
                        % (POST_SAMPLES, SOLID_H)),
        ((255, 160, 100), "%d samples past a hit - terrain grazing the level"
                          % POST_SAMPLES),
        ((255, 205, 105), "radar return - first blocking cell"),
        ((255, 175, 55), "side committed here"),
        ((255, 90, 150), "reversal"),
    ]
    bw, bh = 340, 44 + 24 * len(rows)
    box = Image.new("RGBA", (bw, bh), (10, 12, 20, 205))
    im.alpha_composite(box, (14, 14))
    lu = sc.get("levels_used") or []
    d.text((26, 22),
           ("terraced radar navigator   %d levels, Y = %s" %
            (len(lu), ", ".join("%.0f" % v for v in lu))) if lu
           else (("level radar navigator   Y = %.1f m" % FLIGHT_Y) if FLIGHT_Y is not None
                 else "5 m AGL radar navigator"),
           fill=(240, 240, 250, 255), font=fb)
    for i, (col, label) in enumerate(rows):
        y = 54 + 24 * i
        d.rectangle([26, y + 4, 46, y + 12], fill=col + (255,))
        d.text((54, y), label, fill=(206, 210, 224, 255), font=f)

    stat = ("loop closed %s    clips %d    max deviation %.0f m    "
            "min clearance %.1f m    detours %d    reversals %d    guard fires %d"
            % (res["closed"], sc["clips"], sc["max_dev"], sc["min_clear"],
               res["detours"], res["reversals"], res["guard_fires"]))
    sb = Image.new("RGBA", (W, 34), (10, 12, 20, 205))
    im.alpha_composite(sb, (0, H - 34))
    d.text((16, H - 27), stat, fill=(215, 219, 232, 255), font=f)

    im.convert("RGB").save(out_png)


# --------------------------------------------------------------------------

def build_world(bake, level):
    """Blocked mask, dilated planning mask and clearance field for one level."""
    cell_m = bake.mx
    if level is not None:
        raw = bake.top > (level - MARGIN)
    else:
        raw = bake.obstacle > BLOCK_H

    # Two standoffs, not one. Split the blocked set by whether something is
    # STANDING there or the ground itself has reached the flight level, and
    # dilate them separately.
    standing = (bake.top - bake.floor) > OBJECT_MIN_H
    pad = max(1, int(round(BODY_R / cell_m)))
    tpad = max(1, int(round(TERRAIN_R / cell_m)))
    plan = (ndimage.binary_dilation(raw & standing, iterations=pad)
            | ndimage.binary_dilation(raw & ~standing, iterations=tpad))
    plan[:4, :] = plan[-4:, :] = True
    plan[:, :4] = plan[:, -4:] = True
    dist_m = ndimage.distance_transform_edt(~raw) * cell_m
    return raw, plan, dist_m, pad


def main():
    global FLIGHT_Y
    two_point = "--no-trap-rule" not in sys.argv

    bake = Bake(FOLDER, MAP)
    cell_m = bake.mx
    nx, nz = load_plan(os.path.join(FOLDER, MAP + "_plan.csv"))

    terrace_of = None
    worlds = None

    if TERRACED:
        terrace_of, tlevels, worlds, radar, extra, nx, nz = plan_flight(bake, nx, nz, two_point)

        spacing = float(np.mean(np.hypot(np.diff(nx, append=nx[0]),
                                         np.diff(nz, append=nz[0]))))
        print(f"{len(tlevels)} terrace(s) at {AGL:.0f} m over their ground, "
              f"band {TERRACE_BAND:.0f} m:")
        for k, lv in enumerate(tlevels):
            m = terrace_of == k
            print(f"  terrace {k}: Y = {lv:6.1f} m over {int(m.sum()) * spacing:5.0f} m "
                  f"of course, {100.0 * worlds[k][2].mean():5.2f}% of the map reaches it")

        dist_m = worlds[0][3]
        FLIGHT_Y = None

        runs = {}
        for tag, tp in (("trap rule OFF", False), ("trap rule ON", True)):
            TRAP_STATS["object"] = TRAP_STATS["terrain"] = 0
            res = fly(bake, radar, nx, nz, tp, record_fans=(tp == two_point),
                      terrace_of=terrace_of)
            if tp:
                tot = TRAP_STATS["object"] + TRAP_STATS["terrain"]
                if tot:
                    print(f"  far-probe hits: {TRAP_STATS['object']} object "
                          f"({100.0 * TRAP_STATS['object'] / tot:.0f}%), "
                          f"{TRAP_STATS['terrain']} terrain graze - "
                          f"grazes no longer close a bearing")
            sc = score(bake, radar, res, nx, nz, dist_m, worlds=worlds)
            runs[tp] = (res, sc)
            print(f"{tag:14s} closed={res['closed']} len={sc['length']:7.0f} m  "
                  f"clips={sc['clips']}  reversals={res['reversals']}  "
                  f"trap_entries={res['trap_entries']}  detours={res['detours']}  "
                  f"boxed={res['stuck']}  guard={res['guard_fires']}  "
                  f"maxdev={sc['max_dev']:.1f} m  minclr={sc['min_clear']:.2f} m  "
                  f"worst headroom under the camera={sc['min_alt_margin']:.2f} m")

        res, sc = runs[two_point]
        draw(bake, res, sc, nx, nz, OUT_PNG, worlds=worlds, terrace_of=terrace_of)
        print("wrote " + OUT_PNG)
        return

    level = None
    if LEVEL_FLIGHT:
        level = LEVEL_Y if LEVEL_Y is not None else pick_level(bake, nx, nz)

    # Raise the level until the loop actually closes, rather than picking one
    # and reporting a failure. A level low enough to be interesting is often a
    # metre or two below feasible, and the search costs one flight per try.
    radar = None
    for attempt in range(LEVEL_TRIES):
        FLIGHT_Y = level
        raw, plan, dist_m, pad = build_world(bake, level)

        if level is None:
            print(f"blocked at {BLOCK_H:.1f} m: {100.0 * raw.mean():.2f}% of the map, "
                  f"{100.0 * plan.mean():.2f}% after {pad}-cell standoff")
            radar = Radar(bake, plan, raw, cell_m)
            break

        print(f"level Y = {level:6.1f} m: {100.0 * raw.mean():5.2f}% of the map reaches it, "
              f"{100.0 * plan.mean():5.2f}% after {pad}-cell standoff", end="")

        radar = Radar(bake, plan, raw, cell_m)
        probe = fly(bake, radar, nx, nz, two_point, record_fans=False)
        if probe["closed"]:
            print("  -> loop closes")
            break
        print(f"  -> boxed in, raising by {LEVEL_STEP:.0f} m")
        level += LEVEL_STEP
    else:
        print("could not find a level the loop closes at")
        return

    runs = {}
    for tag, tp in (("trap rule OFF", False), ("trap rule ON", True)):
        res = fly(bake, radar, nx, nz, tp, record_fans=(tp == two_point))
        sc = score(bake, radar, res, nx, nz, dist_m)
        runs[tp] = (res, sc)
        print(f"{tag:14s} closed={res['closed']} len={sc['length']:7.0f} m  "
              f"clips={sc['clips']}  reversals={res['reversals']}  "
              f"trap_entries={res['trap_entries']}  detours={res['detours']}  "
              f"boxed={res['stuck']}  guard={res['guard_fires']}  "
              f"maxdev={sc['max_dev']:.1f} m  minclr={sc['min_clear']:.2f} m  "
              f"worst headroom under the camera={sc['min_alt_margin']:.2f} m")

    res, sc = runs[two_point]
    draw(bake, res, sc, nx, nz, OUT_PNG)
    print("wrote " + OUT_PNG)


if __name__ == "__main__":
    main()
