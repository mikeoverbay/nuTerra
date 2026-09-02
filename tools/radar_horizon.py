"""
radar_horizon.py - low-level camera navigation over the Abbey (19_monastery) bake.

Fly the nominal 1345 m circuit at 5 m above ground, using a forward RADAR fan and
a miniature MPC to steer around anything the camera cannot clear.

The whole point of 5 m AGL is that "obstacle" stops being a property of the map
and becomes a property of the AIRCRAFT: a 2 m wall is scenery you fly over, a 9 m
barn is a thing you fly around. So the blocking test is

    blocked = (top - floor) > (AGL - MARGIN)          # 5.0 - 1.5 = 3.5 m

and the terrain itself never blocks, because the camera rides the terrain.

Steering, in order of authority:

  1. RADAR.   A fan of rays, marched cell by cell, gives the range to the first
              blocking cell on each bearing. A bearing whose radar return is
              short is not a candidate, full stop.
  2. TRAP.    Two points ahead, not one. A bearing whose NEXT point is clear but
              whose point AFTER THAT is blocked leads into a pocket - a
              courtyard, an alcove, the gap between two barns. Rejected before
              it is ever scored. Toggleable so its effect can be measured.
  3. ARC.     Each surviving steering angle is held for K steps (~32 m) and the
              whole arc is collision-tested, cell by cell. An arc that touches a
              blocked cell is rejected AT THAT LENGTH - the shorter arc along
              the same bearing is a different arc, still valid, and is scored
              separately with a charge for being short. Constant curvature
              cannot represent a corridor with a bend in it, and testing only
              the full 32 m made the camera circle in open ground rather than
              enter one.
  4. SCORE.   Survivors are ranked by progress along the nominal course, minus
              lateral deviation from it, minus turn effort, minus a charge for
              revisiting somewhere it has just been. "Progress" is measured
              through a geodesic navigation field over the SAME mask the arc
              test uses, so a detour the long way round a building is correctly
              seen as progress and not as retreat.

If nothing survives, the gates come off in order - trap rule, then radar gate,
then the +/-60 degree limit on the candidate fan - before the camera will admit
to being boxed in.

Only the FIRST step of the winning arc is flown. Then the whole thing runs again.

    python radar_horizon.py
"""

import csv
import math
import os
import time
import heapq

import numpy as np
from scipy import ndimage, sparse
from scipy.sparse import csgraph
from PIL import Image, ImageDraw

# ---------------------------------------------------------------------------
# Flight envelope
# ---------------------------------------------------------------------------

AGL = 5.0            # metres above bare terrain the camera holds
MARGIN = 1.5         # of that 5 m, keep 1.5 m in hand
BLOCK_H = AGL - MARGIN   # 3.5 m - taller than this must be flown AROUND

BODY_R = 2.6         # metres of horizontal standoff kept from a blocking cell

DS = 4.0             # metres flown per step (matches the plan's 4 m sampling)
K_ARC = 8            # steps simulated per candidate  -> 32 m of lookahead arc

STEER_DEG = [0, 5, 10, 16, 24, 34, 46, 60]        # candidate turns, mirrored
STEER_WIDE = [72, 88, 105, 125]                   # only offered when desperate

RADAR_N = 31         # rays in the fan
RADAR_HALF = 80.0    # degrees either side of heading
RADAR_RANGE = 90.0   # metres

LOOKAHEAD = 68.0     # metres ahead on the course the navigation goal sits.
                     # Short lookaheads knot: the goal crosses a building
                     # while the camera is already committed, the geodesic
                     # flips to the other side of it, and the camera turns
                     # round. 34 m gave 1764 m and three of those; 68 m
                     # gives 1484 m and one.
PROG_WIN = 44        # course samples searched forward when updating progress

W_NAV = 1.00         # weight on geodesic distance-to-goal   (metres)
W_PROG = 0.25        # weight on raw along-course progress   (metres)
W_DEV = 0.06         # weight on lateral deviation from the course (metres)
W_TURN = 1.5         # weight on |steer| (radians) - keeps the line calm
W_JERK = 1.0         # weight on change of steer between steps
W_TABU = 4.0         # weight on "I have just been here" - kills limit cycles
TABU_CELL = 7.0      # metres per short-term-memory cell
TABU_MIN = 14        # steps that must have passed before a revisit counts
TABU_MAX = 110       # ... and after this many it is forgotten again
DETOUR_MARK = 12.0   # metres off course before the picture marks a detour
W_SHORT = 4.0        # charge per arc step the candidate could NOT validate
W_CLEAR = 0.5        # weight on the squared shortfall inside CLEAR_WANT
CLEAR_WANT = 5.0     # metres of elbow room the scorer would like to have

MAX_STEPS = 4000
STALL_LIMIT = 25     # steps without progress before escape mode
ABORT_STALL = 400

DEBUG = 0            # set to N to trace the first N steering decisions

PINK = (255, 46, 168)


# ---------------------------------------------------------------------------
# The bake
# ---------------------------------------------------------------------------

class Bake:
    """The two baked layers plus the world<->texel mapping from the header.

    Same class as tools/flight_plan.py, trimmed to what this program needs."""

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
        self.cell_m = 0.5 * (self.mx + self.mz)

    def _r32(self, path):
        a = np.fromfile(path, dtype="<f4").astype(np.float64)
        return a.reshape(self.h, self.w).copy()

    def world_of(self, col, row):
        return (self.wx_min + (col + 0.5) * self.mx,
                self.wz_max - (row + 0.5) * self.mz)

    def texel_of(self, x, z):
        return ((x - self.wx_min) / self.mx - 0.5,
                (self.wz_max - z) / self.mz - 0.5)

    def rc(self, x, z):
        c, r = self.texel_of(x, z)
        ci = int(c + 0.5)
        ri = int(r + 0.5)
        if ci < 0: ci = 0
        elif ci >= self.w: ci = self.w - 1
        if ri < 0: ri = 0
        elif ri >= self.h: ri = self.h - 1
        return ri, ci

    def sample(self, field, x, z):
        r, c = self.rc(x, z)
        return field[r, c]


# ---------------------------------------------------------------------------
# What blocks, and how far it is
# ---------------------------------------------------------------------------

def build_masks(bake):
    """Three related things, and keeping them apart is the whole safety story.

      hard    - the truth. (top-floor) > 3.5 m. Clip counting uses ONLY this.
      safe    - hard, grown by the body radius. Everything the planner tests
                against, so the flown line keeps its distance instead of
                shaving corners at a metre.
      dist_m  - metres to the nearest hard cell, for the clearance score and
                for the honest min-clearance number in the report.
    """
    hard = (bake.obstacle > BLOCK_H).copy()

    # The map edge is outland decoration with nothing to see; treat it as solid
    # so the navigator cannot cheat a detour off the edge of the world.
    hard[:6, :] = hard[-6:, :] = True
    hard[:, :6] = hard[:, -6:] = True

    # A DISC, not the default cross. Two iterations of the cross give a
    # diamond, and the corner of a diamond is only 1.9 m from the cell it was
    # meant to keep 2.6 m away - the measured min clearance said so before this
    # was fixed.
    rad = BODY_R / bake.cell_m
    k = int(math.ceil(rad))
    yy, xx = np.mgrid[-k:k + 1, -k:k + 1]
    disc = (yy * yy + xx * xx) <= rad * rad
    safe = ndimage.binary_dilation(hard, structure=disc)

    dist_m = ndimage.distance_transform_edt(~hard) * bake.cell_m
    return hard, safe, dist_m


class Nav:
    """Geodesic distance-to-goal over free space, at the resolution the arc
    test actually flies in.

    This is what makes "progress" mean the right thing. Straight-line distance
    to a goal on the far side of a barn says a detour is losing ground, and a
    scorer that believes it will sit in front of the barn nodding. A geodesic
    field says the way round IS the way there, so the detour scores as
    progress and the camera commits to it.

    It is built on exactly the mask the arc test uses - the blocked cells
    grown by the body radius - and NOT on a coarse pooled version of it. The
    coarse version was the second bug in this program and it was expensive:
    pooling 4x4 blocks joins cells that the body radius keeps apart, so the
    field promised a 40 m route down a corridor whose real length was 83 m,
    and the camera spent forty steps circling in open ground trying to take a
    door that was not there. A field and a collision test that disagree do not
    average out; they oscillate.

    scipy's C Dijkstra over the 3.5 M-edge lattice costs about 0.2 s, which is
    cheap enough to just do it properly.
    """

    def __init__(self, bake, safe):
        self.bake = bake
        self.free = ~safe
        h, w = self.free.shape
        n = int(self.free.sum())
        self.idx = np.full(self.free.shape, -1, np.int32)
        self.idx[self.free] = np.arange(n, dtype=np.int32)

        rows, cols, wts = [], [], []
        for dr, dc, wt in ((0, 1, 1.0), (1, 0, 1.0),
                           (1, 1, 1.41421), (1, -1, 1.41421)):
            u = self.idx[max(0, -dr):h - max(0, dr),
                         max(0, -dc):w - max(0, dc)]
            v = self.idx[max(0, dr):h + min(0, dr),
                         max(0, dc):w + min(0, dc)]
            m = (u >= 0) & (v >= 0)
            rows.append(u[m])
            cols.append(v[m])
            wts.append(np.full(int(m.sum()), wt * bake.cell_m))
        self.graph = sparse.coo_matrix(
            (np.concatenate(wts),
             (np.concatenate(rows), np.concatenate(cols))),
            shape=(n, n)).tocsr()

        # nearest free cell for anything that is not one, so a goal that lands
        # inside a building still resolves to somewhere reachable
        _, ind = ndimage.distance_transform_edt(~self.free, return_indices=True)
        self.snap_r, self.snap_c = ind

        self.cache = {}
        self.order = []
        self.builds = 0

    def cell_of(self, x, z):
        r, c = self.bake.rc(x, z)
        if not self.free[r, c]:
            r, c = int(self.snap_r[r, c]), int(self.snap_c[r, c])
        return r, c

    def field(self, rc):
        """Distance from every free cell to the goal, in metres."""
        if rc in self.cache:
            return self.cache[rc]

        d = csgraph.dijkstra(self.graph, directed=False,
                             indices=int(self.idx[rc]))
        f = np.full(self.free.shape, np.inf, np.float32)
        f[self.free] = d

        # Bleed the field a couple of cells into the blocked space. Arc ends
        # are always in free cells, but a BILINEAR read of one touches its
        # neighbours, and a neighbour holding infinity would poison the score
        # of a perfectly good position next to a wall.
        big = np.where(np.isfinite(f), f, np.float32(1e9))
        for _ in range(2):
            big = np.minimum(big, ndimage.grey_erosion(big, size=3)
                             + np.float32(self.bake.cell_m))
        f = np.where(np.isfinite(f), f, big).astype(np.float32)
        f = np.where(f > 5e8, np.float32(4000.0), f)

        # Keep only a handful of fields alive - each is 4 MB and the goal
        # walks forward, so old ones are never wanted again.
        self.cache[rc] = f
        self.order.append(rc)
        while len(self.order) > 6:
            del self.cache[self.order.pop(0)]
        self.builds += 1
        return f

    def value(self, field, x, z):
        """Bilinear read.

        Nearest-cell was the first version of this and it is why the camera
        circled the first time: a field quantised to the cell makes most
        candidate steerings score IDENTICALLY on distance-to-goal, and the tie
        is then settled by the turn penalty - which prefers whatever the
        camera was already doing. A smooth read gives the scorer a gradient to
        actually follow."""
        c, r = self.bake.texel_of(x, z)
        r0 = int(np.clip(math.floor(r), 0, self.bake.h - 2))
        c0 = int(np.clip(math.floor(c), 0, self.bake.w - 2))
        tr = float(np.clip(r - r0, 0.0, 1.0))
        tc = float(np.clip(c - c0, 0.0, 1.0))
        return float((1 - tr) * ((1 - tc) * field[r0, c0]
                                 + tc * field[r0, c0 + 1])
                     + tr * ((1 - tc) * field[r0 + 1, c0]
                             + tc * field[r0 + 1, c0 + 1]))


# ---------------------------------------------------------------------------
# The nominal course
# ---------------------------------------------------------------------------

class Course:
    """x,z from the high-altitude plan, used as the nominal course only. Its y
    column is ignored on purpose - that flight was 26-49 m up and only avoided
    things over 55 m tall, which is exactly why this one has work to do."""

    def __init__(self, path):
        xs, zs, hs = [], [], []
        with open(path) as f:
            for row in csv.DictReader(f):
                xs.append(float(row["x"]))
                zs.append(float(row["z"]))
                hs.append(float(row["heading_rad"]))
        self.x = np.array(xs)
        self.z = np.array(zs)
        self.h0 = hs[0]
        self.n = len(xs)
        seg = np.hypot(np.diff(np.append(self.x, self.x[0])),
                       np.diff(np.append(self.z, self.z[0])))
        self.s = np.concatenate([[0.0], np.cumsum(seg)[:-1]])
        self.total = float(seg.sum())

    def at(self, s):
        """Position at unwrapped arc length s, linearly interpolated."""
        u = s % self.total
        i = int(np.searchsorted(self.s, u, side="right") - 1)
        j = (i + 1) % self.n
        s0 = self.s[i]
        s1 = self.s[j] if j else self.total
        t = 0.0 if s1 <= s0 else (u - s0) / (s1 - s0)
        return (self.x[i] + t * (self.x[j] - self.x[i]),
                self.z[i] + t * (self.z[j] - self.z[i]))

    def project(self, x, z, s_from):
        """Nearest course point in a forward window, as unwrapped arc length.

        Forward-only and windowed, so progress cannot be claimed by drifting
        backwards onto the leg already flown, and cannot leap a whole side of
        the loop just because the circuit passes near itself."""
        i0 = int(math.floor((s_from % self.total) / self.total * self.n))
        best = 0
        best_d = 1e18
        for k in range(PROG_WIN):
            i = (i0 + k) % self.n
            d = (self.x[i] - x) ** 2 + (self.z[i] - z) ** 2
            if d < best_d:
                best_d = d
                best = k
        idx = i0 + best
        laps = idx // self.n
        s_hit = (s_from - (s_from % self.total)) + laps * self.total \
            + self.s[idx % self.n]
        # The window starts at the sample at or just BEHIND s_from, so the
        # winner can land a metre or two back. Clamp - do not "wrap", or the
        # flight books a whole extra lap for standing still. (That bug read as
        # 1.38 million metres of progress and a camera that never went home.)
        if s_hit < s_from:
            s_hit = s_from
        return s_hit, math.sqrt(best_d)


# ---------------------------------------------------------------------------
# Radar
# ---------------------------------------------------------------------------

def radar(bake, hard, x, z, heading):
    """A fan of rays, marched cell by cell, out to the first blocking cell.

    Integer stepping on the texel grid rather than a fixed metre step: a fixed
    step either walks past the corner of a building or costs ten times as much
    to be sure it did not. Stepping cell to cell cannot miss one."""
    bearings = np.linspace(heading - math.radians(RADAR_HALF),
                           heading + math.radians(RADAR_HALF), RADAR_N)
    ranges = np.full(RADAR_N, RADAR_RANGE)

    c0f, r0f = bake.texel_of(x, z)
    W, H = bake.w, bake.h
    cell = bake.cell_m
    nmax = int(RADAR_RANGE / cell) + 2

    for i in range(RADAR_N):
        b = bearings[i]
        dc = math.sin(b)          # world +x is +col
        dr = -math.cos(b)         # world +z is -row
        hit = RADAR_RANGE
        for step in range(1, nmax + 1):
            ci = int(c0f + dc * step + 0.5)
            ri = int(r0f + dr * step + 0.5)
            if ci < 0 or ci >= W or ri < 0 or ri >= H or hard[ri, ci]:
                hit = step * cell
                break
        ranges[i] = min(hit, RADAR_RANGE)
    return bearings, ranges


def radar_range_at(bearings, ranges, b):
    """Return on the nearest fan bearing to b. Steering is only allowed to read
    what the sensor actually measured, not to re-query the map."""
    return ranges[int(np.argmin(np.abs(bearings - b)))]


# ---------------------------------------------------------------------------
# Collision tests
# ---------------------------------------------------------------------------

def cells_on(bake, x0, z0, x1, z1):
    """Every texel the segment passes through - Amanatides-Woo, not sampling.

    Point sampling along the segment was the third bug here. At 0.7 m steps in
    a 1.37 m grid a segment can cut the corner of a cell without any sample
    landing in it, so the planner declared an arc clear and the verifier then
    measured the flown line passing 1.93 m from a building it was supposed to
    hold 2.6 m off. Walking cells cannot miss one, and it lets the planner and
    the verifier use the SAME test, which is the only way the clearance number
    means anything."""
    c0, r0 = bake.texel_of(x0, z0)
    c1, r1 = bake.texel_of(x1, z1)
    u0, v0 = c0 + 0.5, r0 + 0.5      # cell k covers [k, k+1)
    u1, v1 = c1 + 0.5, r1 + 0.5
    iu, iv = math.floor(u0), math.floor(v0)
    eu, ev = math.floor(u1), math.floor(v1)
    du, dv = u1 - u0, v1 - v0

    su = 1 if du > 0 else -1
    sv = 1 if dv > 0 else -1
    inf = float("inf")
    tmu = ((iu + (1 if du > 0 else 0)) - u0) / du if du else inf
    tmv = ((iv + (1 if dv > 0 else 0)) - v0) / dv if dv else inf
    tdu = abs(1.0 / du) if du else inf
    tdv = abs(1.0 / dv) if dv else inf

    out = [(iv, iu)]
    for _ in range(4096):
        if iu == eu and iv == ev:
            break
        if tmu < tmv:
            if tmu > 1.0:
                break
            iu += su
            tmu += tdu
        else:
            if tmv > 1.0:
                break
            iv += sv
            tmv += tdv
        out.append((iv, iu))
    return out


def seg_clear(bake, mask, x0, z0, x1, z1):
    W, H = bake.w, bake.h
    for r, c in cells_on(bake, x0, z0, x1, z1):
        if r < 0 or r >= H or c < 0 or c >= W or mask[r, c]:
            return False
    return True


def point_clear(bake, mask, x, z):
    r, c = bake.rc(x, z)
    return not mask[r, c]


# ---------------------------------------------------------------------------
# The flight
# ---------------------------------------------------------------------------

def fly(bake, hard, safe, dist_m, nav, course, trap_on,
        karc_max=K_ARC, verbose=True):
    """Run the loop. Returns everything the report and the picture need."""

    x, z = float(course.x[0]), float(course.z[0])
    heading = float(course.h0)

    # If the plan's own start sits inside something at this altitude, step off
    # it before launching rather than declaring a clip on sample zero. How far
    # it had to move is reported - a start shoved 20 m is a fact about the
    # course, not a detail to bury.
    start_nudge = 0.0
    if not point_clear(bake, safe, x, z):
        for rad in np.arange(2.0, 40.0, 1.5):
            found = False
            for a in np.linspace(0, 2 * math.pi, 48, endpoint=False):
                tx, tz = x + rad * math.sin(a), z + rad * math.cos(a)
                if point_clear(bake, safe, tx, tz):
                    x, z, start_nudge, found = tx, tz, float(rad), True
                    break
            if found:
                break
    home = (x, z)

    path = [(x, z)]
    fans = []
    steers = []
    events = []          # (x, z, kind)
    s_prog = 0.0
    prev_steer = 0.0
    stall = 0
    trap_rejects = 0
    boxed = 0
    reversals = 0
    escape_steps = 0
    overrides = 0
    gate_overrides = 0
    wide_overrides = 0
    horizons = []
    goal_rc = None
    field = None
    closing = False
    seen = {}            # short-term memory: cell -> steps it was occupied

    t0 = time.time()
    for step in range(MAX_STEPS):
        bearings, ranges = radar(bake, hard, x, z, heading)
        fans.append((x, z, bearings.copy(), ranges.copy()))
        seen.setdefault((int(x / TABU_CELL), int(z / TABU_CELL)), []).append(step)

        # ---- where are we trying to get to -------------------------------
        if closing:
            gx, gz = home
        else:
            look = LOOKAHEAD + (26.0 if stall > STALL_LIMIT else 0.0)
            gx, gz = course.at(s_prog + look)
            # If the course itself dives into a building, slide the goal along
            # it until it is somewhere the camera could actually stand.
            extra = 0.0
            while extra < 160.0 and not point_clear(bake, safe, gx, gz):
                extra += 6.0
                gx, gz = course.at(s_prog + look + extra)

        rc = nav.cell_of(gx, gz)
        if rc != goal_rc:
            goal_rc = rc
            field = nav.field(rc)

        # ---- candidate steering angles -----------------------------------
        desperate = stall > STALL_LIMIT
        angles = [0.0]
        for dg in STEER_DEG[1:]:
            angles += [math.radians(dg), -math.radians(dg)]
        wide = list(angles)
        for dg in STEER_WIDE:
            wide += [math.radians(dg), -math.radians(dg)]
        if desperate:
            angles = wide
            escape_steps += 1

        best = None
        best_score = -1e18
        best_h = 0
        trap_override = False
        gate_override = False
        wide_override = False

        # Three passes, each only run if the one before it found nothing.
        #   1  everything on
        #   2  trap rule dropped - better to enter a pocket knowingly than stop
        #   3  radar gate dropped too. The gate is a cheap heuristic on the raw
        #      blocked mask; the arc test on the body-radius mask is the truth.
        #      Keeping the gate to the bitter end declared the camera boxed in
        #      six times a lap in places it could in fact fly.
        #   4  and finally the wide steering set. A corridor that turns more
        #      sharply than 60 degrees is invisible to a +/-60 degree fan of
        #      candidates, and the camera called itself boxed in six times a
        #      lap in places where a 90 degree turn was clear.
        passes = [(trap_on, True, angles), (False, True, angles),
                  (False, False, angles), (False, False, wide)]
        for pass_trap, pass_gate, pass_angles in passes:
            for a in pass_angles:
                # ---- 1. RADAR GATE -------------------------------------
                if pass_gate and \
                        radar_range_at(bearings, ranges, heading + a) < 2.0 * DS:
                    continue

                # ---- 2. TRAP RULE: two points ahead, not one -----------
                # The near point being clear says nothing about whether there
                # is room to keep going. A gap you can enter and not leave is
                # a trap, and the only way to see it is to look past the gap.
                if pass_trap:
                    h1 = heading + a
                    p1x = x + DS * math.sin(h1)
                    p1z = z + DS * math.cos(h1)
                    h2 = h1 + a
                    p2x = p1x + DS * math.sin(h2)
                    p2z = p1z + DS * math.cos(h2)
                    if not point_clear(bake, safe, p1x, p1z):
                        continue
                    if not point_clear(bake, safe, p2x, p2z):
                        trap_rejects += 1
                        continue

                # ---- 3. ARC TEST ---------------------------------------
                # Hold this steering and walk the arc, remembering how far it
                # got before it touched anything. An arc that hits a blocked
                # cell IS rejected at that length - but the shorter arc along
                # the same bearing is a different arc and still a valid one,
                # so it is scored separately and charged for being short.
                #
                # Constant curvature alone cannot represent a corridor with a
                # bend in it, and testing only the full 32 m made the camera
                # circle in open ground for forty steps outside exactly such
                # a corridor: every arc that pointed down it was rejected,
                # and the only survivors pointed away.
                cx, cz, ch = x, z, heading
                ends = []
                for k in range(1, karc_max + 1):
                    nh = ch + a
                    nx = cx + DS * math.sin(nh)
                    nz = cz + DS * math.cos(nh)
                    if not seg_clear(bake, safe, cx, cz, nx, nz):
                        break
                    cx, cz, ch = nx, nz, nh
                    ends.append((k, cx, cz))
                if not ends:
                    continue

                # ---- 4. SCORE ------------------------------------------
                for k, ex_, ez_ in ends:
                    if k != karc_max and k % 2:
                        continue          # every other horizon is plenty
                    r, c = bake.rc(ex_, ez_)
                    nd = nav.value(field, ex_, ez_)
                    s_end, dev = course.project(ex_, ez_, s_prog)
                    prog = s_end - s_prog
                    short = max(0.0, CLEAR_WANT - dist_m[r, c])

                    # Short-term memory. Ending the arc where the camera
                    # already was, a little while ago, is the signature of a
                    # limit cycle - and a limit cycle is what happens when
                    # the way forward is pinched and every steering scores
                    # alike. Charging for a revisit breaks the tie towards
                    # somewhere new.
                    hits = seen.get((int(ex_ / TABU_CELL), int(ez_ / TABU_CELL)))
                    tabu = 0
                    if hits:
                        tabu = sum(1 for t in hits
                                   if TABU_MIN < step - t <= TABU_MAX)

                    dev_w = W_DEV * (0.25 if desperate else 1.0)
                    score = (-W_NAV * min(nd, 4000.0)
                             + W_PROG * prog
                             - dev_w * dev
                             - W_CLEAR * short * short
                             - W_TABU * min(tabu, 6)
                             - W_TURN * abs(a)
                             - W_JERK * abs(a - prev_steer)
                             - W_SHORT * (karc_max - k))
                    if score > best_score:
                        best_score = score
                        best = a
                        best_h = k
            if best is not None:
                break
            if pass_trap:
                trap_override = True
            elif pass_gate:
                gate_override = True
            else:
                wide_override = True
        if trap_override:
            overrides += 1
        if gate_override:
            gate_overrides += 1
        if wide_override:
            wide_overrides += 1

        # ---- boxed in: nothing survives at any arc length -----------------
        if best is None:
            boxed += 1
            events.append((x, z, "boxed"))
            # Fallback: sweep a full 300 degrees for ANY clear single step
            # and take the one that gets closest to the goal. Only if not one
            # of thirty-one bearings admits a 4 m step does the camera rewind
            # a step and spin - that is a hard failure of the planner, so it
            # is counted separately and drawn as a red cross.
            turn = None
            best_nd = 1e18
            for dg in range(-150, 151, 10):
                nh = heading + math.radians(dg)
                nx = x + DS * math.sin(nh)
                nz = z + DS * math.cos(nh)
                if not seg_clear(bake, safe, x, z, nx, nz):
                    continue
                nd = nav.value(field, nx, nz)
                if nd < best_nd:
                    best_nd = nd
                    turn = math.radians(dg)
            if turn is not None:
                nh = heading + turn
                heading = nh
                x = x + DS * math.sin(nh)
                z = z + DS * math.cos(nh)
            else:
                turn = 0.0
                reversals += 1
                if len(path) > 1:
                    path.pop()
                    x, z = path[-1]
                heading += math.radians(60.0)
            prev_steer = 0.0
            path.append((x, z))
            steers.append(turn)
            stall += 1
            if stall > ABORT_STALL:
                break
            continue

        horizons.append(best_h)
        if DEBUG and step < DEBUG:
            print(f"    {step:4d} s={s_prog:7.1f} goal=({gx:6.1f},{gz:6.1f}) "
                  f"nav={nav.value(field, x, z):7.1f} "
                  f"steer={math.degrees(best):6.1f} h={best_h} stall={stall:3d}")

        # ---- fly the FIRST step of the winning arc only -------------------
        heading = (heading + best + math.pi) % (2 * math.pi) - math.pi
        x += DS * math.sin(heading)
        z += DS * math.cos(heading)
        path.append((x, z))
        steers.append(best)
        prev_steer = best

        s_new, dev = course.project(x, z, s_prog)
        stall = 0 if s_new > s_prog + 0.35 else stall + 1
        s_prog = max(s_prog, s_new)

        if dev > DETOUR_MARK and (not events or events[-1][2] != "detour"
                           or math.hypot(x - events[-1][0],
                                         z - events[-1][1]) > 45.0):
            events.append((x, z, "detour"))

        if not closing and s_prog >= course.total - 6.0:
            closing = True
        if closing and step > 40 and math.hypot(x - home[0], z - home[1]) < 6.0:
            break
        if stall > ABORT_STALL:
            break

    dt = time.time() - t0
    px = np.array([p[0] for p in path])
    pz = np.array([p[1] for p in path])
    length = float(np.hypot(np.diff(px), np.diff(pz)).sum())

    # ---- verification, on the HARD mask, densely -------------------------
    # Not on the dilated mask the planner used: the question is whether the
    # camera went through anything, not whether it kept its manners.
    clips = 0
    clip_pts = []
    worst_clear = 1e9
    for i in range(len(px)):
        r, c = bake.rc(px[i], pz[i])
        worst_clear = min(worst_clear, dist_m[r, c])
        if i:
            for rr, cc in cells_on(bake, px[i - 1], pz[i - 1], px[i], pz[i]):
                rr = min(max(rr, 0), bake.h - 1)
                cc = min(max(cc, 0), bake.w - 1)
                if hard[rr, cc]:
                    clips += 1
                    clip_pts.append((px[i], pz[i]))
                worst_clear = min(worst_clear, dist_m[rr, cc])

    # deviation of every flown sample from the nominal course - true nearest,
    # not the windowed progress projection, so the number is honest about how
    # far out the detours actually went
    devs = np.array([math.sqrt(np.min((course.x - px[i]) ** 2
                                      + (course.z - pz[i]) ** 2))
                     for i in range(len(px))])

    closed = (s_prog >= course.total - 12.0 and
              math.hypot(px[-1] - home[0], pz[-1] - home[1]) < 12.0)

    alt = np.array([bake.sample(bake.floor, px[i], pz[i]) + AGL
                    for i in range(len(px))])

    if verbose:
        print(f"  {len(path)} steps, {length:.0f} m, {dt:.1f}s, "
              f"{nav.builds} nav fields")
        print(f"  progress {s_prog:.0f} / {course.total:.0f} m   closed={closed}")
        print(f"  clips {clips}   min clearance {worst_clear:.2f} m")
        print(f"  deviation max {devs.max():.1f} m  mean {devs.mean():.1f} m")
        print(f"  trap rule fired in flight {trap_rejects}   boxed {boxed}   "
              f"reversals {reversals}   escape steps {escape_steps}   "
              f"trap overrides {overrides}   "
              f"radar-gate {gate_overrides}  wide-steer {wide_overrides}")
        if horizons:
            print(f"  arc horizon: mean {np.mean(horizons) * DS:.1f} m, "
                  f"{100.0 * np.mean([h == karc_max for h in horizons]):.0f}%"
                  f" at the full {karc_max * DS:.0f} m")

    return dict(px=px, pz=pz, alt=alt, fans=fans, events=events, length=length,
                clips=clips, clip_pts=clip_pts, min_clear=worst_clear,
                devs=devs, closed=closed, boxed=boxed, reversals=reversals,
                escape=escape_steps, steps=len(path), s_prog=s_prog,
                overrides=overrides, gate_overrides=gate_overrides,
                wide_overrides=wide_overrides,
                horizon_mean=(float(np.mean(horizons)) if horizons else 0.0),
                horizon_full=(float(np.mean([h == karc_max for h in horizons]))
                              if horizons else 0.0),
                seconds=dt, trap_fired=trap_rejects, home=home,
                start_nudge=start_nudge, karc=karc_max)


def count_backouts(px, pz, near=9.0, min_loop=44.0, window=34):
    """Trap entries, measured directly on the flown track.

    A trap entry is where the camera comes back to within `near` metres of
    somewhere it was, having flown at least `min_loop` metres in between: it
    went into a pocket and came out again. This is the thing the two-points-
    ahead rule exists to prevent, so it is what the A/B should be counted in -
    "boxed in" and "reversals" only catch the cases bad enough to defeat the
    fallback as well, which undercounts the problem badly.

    Consecutive entries inside one pocket are collapsed, so a long dead end is
    one trap and not thirty."""
    n = len(px)
    seg = [0.0]
    for i in range(1, n):
        seg.append(seg[-1] + ((px[i] - px[i - 1]) ** 2
                              + (pz[i] - pz[i - 1]) ** 2) ** 0.5)
    hits = []
    for i in range(n):
        for j in range(i + 1, min(n, i + window + 1)):
            if seg[j] - seg[i] < min_loop:
                continue
            if (px[i] - px[j]) ** 2 + (pz[i] - pz[j]) ** 2 < near * near:
                hits.append(i)
                break
    out = 0
    last = -999
    for i in hits:
        if i - last > 12:
            out += 1
        last = i
    return out


def count_trap_rejects(bake, safe, res):
    """Replay the flown track and count how many candidate bearings the
    two-points-ahead rule would throw out that a one-point test would accept.
    Measured on the actual flight rather than asserted."""
    n = 0
    px, pz = res["px"], res["pz"]
    for i in range(1, len(px)):
        h = math.atan2(px[i] - px[i - 1], pz[i] - pz[i - 1])
        for dg in STEER_DEG:
            for sgn in (1, -1):
                a = math.radians(dg) * sgn
                h1 = h + a
                p1x = px[i - 1] + DS * math.sin(h1)
                p1z = pz[i - 1] + DS * math.cos(h1)
                h2 = h1 + a
                p2x = p1x + DS * math.sin(h2)
                p2z = p1z + DS * math.cos(h2)
                if (point_clear(bake, safe, p1x, p1z)
                        and not point_clear(bake, safe, p2x, p2z)):
                    n += 1
    return n


# ---------------------------------------------------------------------------
# Picture
# ---------------------------------------------------------------------------

def draw(bake, hard, course, res, path_png, fan_every=15):
    """The obstacle map with the sensor picture on it.

    House style from tools/flight_plan.draw_plan: dark terrain relief, grey
    obstacle shaded by height, yellow for what cannot be passed. Two things are
    new. The radar fans are drawn to the range each ray actually MEASURED, so
    the picture shows what the navigator could see and not merely where it went.
    And the nominal course is drawn dashed OVER the flown path, because a dim
    line underneath a 3 px pink one is invisible exactly where it matters - a
    deviation only reads as a deviation if the thing deviated from is visible.
    """
    o = bake.obstacle
    img = np.zeros((bake.h, bake.w, 3), dtype=np.uint8)

    g = bake.floor
    gn = (g - g.min()) / max(1e-6, (g.max() - g.min()))
    img[..., 0] = (16 + 24 * gn).astype(np.uint8)
    img[..., 1] = (20 + 30 * gn).astype(np.uint8)
    img[..., 2] = (28 + 38 * gn).astype(np.uint8)

    # low stuff - flown OVER, so drawn as texture, not as threat
    low = (o > 0.7) & (o <= BLOCK_H)
    sh = np.clip(o / BLOCK_H, 0, 1)
    img[low, 0] = (52 + 42 * sh[low]).astype(np.uint8)
    img[low, 1] = (57 + 45 * sh[low]).astype(np.uint8)
    img[low, 2] = (63 + 49 * sh[low]).astype(np.uint8)

    # blocking - yellow, shaded by how far over the limit it is
    blk = o > BLOCK_H
    t = np.clip((o - BLOCK_H) / 26.0, 0, 1)
    img[blk, 0] = (150 + 105 * t[blk]).astype(np.uint8)
    img[blk, 1] = (112 + 88 * t[blk]).astype(np.uint8)
    img[blk, 2] = (30 + 30 * t[blk]).astype(np.uint8)

    base = Image.fromarray(img, "RGB")

    # ---- radar fans on their own layer, ADDED in, so they read as a sweep
    # over the map rather than paint on top of it -------------------------
    fan = Image.new("RGB", base.size, (0, 0, 0))
    fd = ImageDraw.Draw(fan)
    for i in range(0, len(res["fans"]), fan_every):
        fx, fz, bs, rs = res["fans"][i]
        c0, r0 = bake.texel_of(fx, fz)
        for b, rng in zip(bs, rs):
            ex = fx + rng * math.sin(b)
            ez = fz + rng * math.cos(b)
            c1, r1 = bake.texel_of(ex, ez)
            free = rng >= RADAR_RANGE - 0.1
            fd.line([(c0, r0), (c1, r1)],
                    fill=(18, 58, 68) if free else (32, 122, 128), width=1)
            if not free:
                fd.ellipse([c1 - 1.4, r1 - 1.4, c1 + 1.4, r1 + 1.4],
                           fill=(92, 214, 205))
    both = np.clip(np.asarray(base, np.int16) + np.asarray(fan, np.int16), 0, 255)
    im = Image.fromarray(both.astype(np.uint8), "RGB")
    d = ImageDraw.Draw(im)

    # ---- flown path ------------------------------------------------------
    pts = [bake.texel_of(res["px"][i], res["pz"][i])
           for i in range(len(res["px"]))]
    d.line(pts, fill=(78, 0, 52), width=7, joint="curve")
    d.line(pts, fill=PINK, width=3, joint="curve")

    # ---- nominal course, dashed, on top ----------------------------------
    for i in range(course.n):
        if (i % 3) == 2:
            continue
        j = (i + 1) % course.n
        c0, r0 = bake.texel_of(course.x[i], course.z[i])
        c1, r1 = bake.texel_of(course.x[j], course.z[j])
        d.line([(c0, r0), (c1, r1)], fill=(178, 174, 208), width=2)

    # where the nominal course itself is inside something - the reason for the
    # biggest deviations, in the same picture as the deviations
    for i in range(course.n):
        if hard[bake.rc(course.x[i], course.z[i])]:
            c, r = bake.texel_of(course.x[i], course.z[i])
            d.ellipse([c - 3, r - 3, c + 3, r + 3], fill=(255, 70, 70))

    # ---- events ----------------------------------------------------------
    for ex, ez, kind in res["events"]:
        c, r = bake.texel_of(ex, ez)
        if kind == "detour":
            d.ellipse([c - 11, r - 11, c + 11, r + 11],
                      outline=(255, 160, 45), width=2)
        else:
            d.line([(c - 8, r - 8), (c + 8, r + 8)], fill=(255, 60, 60), width=3)
            d.line([(c - 8, r + 8), (c + 8, r - 8)], fill=(255, 60, 60), width=3)

    for cx, cz in res["clip_pts"][:200]:
        c, r = bake.texel_of(cx, cz)
        d.ellipse([c - 5, r - 5, c + 5, r + 5], outline=(255, 0, 0), width=2)

    # ---- start and initial heading ---------------------------------------
    c, r = bake.texel_of(res["px"][0], res["pz"][0])
    d.ellipse([c - 8, r - 8, c + 8, r + 8], fill=(80, 255, 130),
              outline=(255, 255, 255))
    hx = res["px"][3] - res["px"][0]
    hz = res["pz"][3] - res["pz"][0]
    hl = math.hypot(hx, hz) or 1.0
    c2, r2 = bake.texel_of(res["px"][0] + hx / hl * 52.0,
                           res["pz"][0] + hz / hl * 52.0)
    d.line([(c, r), (c2, r2)], fill=(80, 255, 130), width=3)

    # ---- crop to the action ----------------------------------------------
    # The circuit uses a third of the map; printed whole, the monastery is a
    # thumbnail in a field of empty ground and none of this is judgeable.
    cs = [p[0] for p in pts]
    rs_ = [p[1] for p in pts]
    pad = 80.0 / bake.cell_m
    x0 = max(0.0, min(cs) - pad)
    x1 = min(float(bake.w), max(cs) + pad)
    y0 = max(0.0, min(rs_) - pad)
    y1 = min(float(bake.h), max(rs_) + pad)
    side = int(max(x1 - x0, y1 - y0))
    cx0 = int(np.clip((x0 + x1) / 2 - side / 2, 0, bake.w - side))
    cy0 = int(np.clip((y0 + y1) / 2 - side / 2, 0, bake.h - side))
    im = im.crop((cx0, cy0, cx0 + side, cy0 + side))
    im = im.resize((1100, 1100), Image.LANCZOS)
    d = ImageDraw.Draw(im)

    # ---- legend, drawn after the crop so it can never be cropped ---------
    d.rectangle([10, 10, 452, 148], fill=(9, 11, 17), outline=(72, 76, 92))
    rows = [(PINK, f"flown  {res['length']:.0f} m at {AGL:.0f} m AGL   "
                   f"clips {res['clips']}"),
            ((178, 174, 208), "nominal course (plan.csv x,z), dashed"),
            ((32, 122, 128), f"radar fan  {RADAR_N} rays  +/-{RADAR_HALF:.0f} deg"
                             f"  {RADAR_RANGE:.0f} m, every {fan_every}th sample"),
            ((200, 150, 40), f"blocking: obstacle taller than {BLOCK_H:.1f} m"),
            ((255, 70, 70), "nominal course inside a blocking cell"),
            ((255, 160, 45), f"detour marker, >{DETOUR_MARK:.0f} m off course   "
                             f"(max {res['devs'].max():.0f} m)")]
    for i, (col, txt) in enumerate(rows):
        y = 18 + i * 21
        d.rectangle([18, y + 4, 38, y + 12], fill=col)
        d.text((46, y + 2), txt, fill=(216, 219, 231))

    im.save(path_png)
    return im


# ---------------------------------------------------------------------------

def main():
    folder = os.path.join(os.environ.get("TEMP", "."), "nuTerra", "flight")
    here = os.path.dirname(os.path.abspath(__file__))
    map_name = "19_monastery"
    out = os.path.join(here, "radar_horizon.png")

    print("bake")
    bake = Bake(folder, map_name)
    hard, safe, dist_m = build_masks(bake)
    print(f"  {bake.w}x{bake.h} at {bake.cell_m:.3f} m/texel")
    print(f"  blocking cells (> {BLOCK_H:.1f} m): {100.0 * hard.mean():.2f}%   "
          f"grown by the {BODY_R:.1f} m body radius: {100.0 * safe.mean():.2f}%")

    course = Course(os.path.join(folder, map_name + "_plan.csv"))
    print(f"  course {course.n} samples, {course.total:.0f} m")
    n_bad = sum(1 for i in range(course.n)
                if hard[bake.rc(course.x[i], course.z[i])])
    n_bad_safe = sum(1 for i in range(course.n)
                     if safe[bake.rc(course.x[i], course.z[i])])
    print(f"  nominal course samples inside a blocking cell: {n_bad} / {course.n}"
          f"   (within the body radius of one: {n_bad_safe})")

    nav = Nav(bake, safe)

    def run(label, trap, karc):
        print(f"fly  [{label}]")
        nav.builds = 0
        r = fly(bake, hard, safe, dist_m, nav, course, trap, karc_max=karc)
        r["label"] = label
        r["trap_would_fire"] = count_trap_rejects(bake, safe, r)
        r["backouts"] = count_backouts(r["px"], r["pz"])
        print(f"  trap entries (went in, came back out): "
              f"{r['backouts']}")
        print(f"  two-point rule would fire on {r['trap_would_fire']} candidate"
              f" bearings along this track")
        return r

    a = run("arc 32 m, trap OFF", False, K_ARC)
    b = run("arc 32 m, trap ON", True, K_ARC)
    # Ablation. With a 32 m arc test the trap rule is largely subsumed, so the
    # only way to measure what it is worth on its own is to take the arc away
    # and leave a single-step check - which is exactly the naive navigator the
    # trap rule exists to beat.
    c = run("arc 4 m (ablated), trap OFF", False, 1)
    e = run("arc 4 m (ablated), trap ON", True, 1)

    draw(bake, hard, course, b, out)
    print("wrote " + out)

    print("\n" + "=" * 89)
    print(f"{'run':<30}{'steps':>7}{'len m':>8}{'clips':>7}{'traps':>7}"
          f"{'boxed':>7}{'rev':>6}{'esc':>6}{'maxdev':>8}{'clear':>7}"
          f"{'closed':>8}")
    for r in (a, b, c, e):
        print(f"{r['label']:<30}{r['steps']:>7}{r['length']:>8.0f}"
              f"{r['clips']:>7}{r['backouts']:>7}{r['boxed']:>7}"
              f"{r['reversals']:>6}{r['escape']:>6}{r['devs'].max():>8.1f}"
              f"{r['min_clear']:>7.2f}{str(r['closed']):>8}")
    print(f"\ntrap rule fired in flight:  full arc {b['trap_fired']},"
          f"  ablated {e['trap_fired']}")
    same_full = (len(a['px']) == len(b['px']) and np.allclose(a['px'], b['px'])
                 and np.allclose(a['pz'], b['pz']))
    same_abl = (len(c['px']) == len(e['px']) and np.allclose(c['px'], e['px'])
                and np.allclose(c['pz'], e['pz']))
    print(f"identical track, trap off vs on:  full arc {same_full},"
          f"  ablated {same_abl}")
    print(f"start nudged off the nominal course by {b['start_nudge']:.1f} m")
    print(f"altitude flown: {b['alt'].min():.1f} .. {b['alt'].max():.1f} m")


if __name__ == "__main__":
    main()
