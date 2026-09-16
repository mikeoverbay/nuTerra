"""The tank brain, and the smallest world that can be wrong in front of you.

    "it runs. it's ai is dumb. we want to do this the tank path studio"
        - the owner, 2026-09-16

WHY HERE AND NOT IN THE APP. Tank Path Studio's own docstring says why, about a
different algorithm: "An algorithm you are still designing wants a loop you can
go round in a second. The VB version takes forty seconds to build and run
before a single pixel appears, which is why the resolver spent an afternoon
being wrong in ways nobody could see."

The brain is now that algorithm. nuTerra's driver has had a week of afternoons
like that - a 20 m boolean that quietly stopped every reverse, a jam branch
that counted a timer and never read it - and each one cost a rebuild, a
relaunch, a run and a log read to see. Here it costs a restart.

NO PYGAME IN THIS FILE. The sim has to run headless so it can be measured
without a window, and the window has to be able to draw it without the sim
knowing. That separation is what lets a change be tested in a loop rather than
watched.

WHAT IT IS NOT. This is not a port of nuTerra/Tanks/TankDrive.vb. That file is
885 lines that grew a branch at a time against symptoms, and copying its shape
would copy its history. The parts worth keeping are the MEASUREMENTS it
produced - stopping distance against ray reach, the per-ray limits, what the
black box showed - and those are constants here, not code.

Added 2026-09-16 by Tank AI work.
"""

import math

import numpy as np

# ---- the hull -----------------------------------------------------------
#
# From the game's own boxes, via TankRoster's fallback for a mid tier 10:
# 3.5 m by 1.7 m half-extents, so 7 m long and 3.4 m wide.
HALF_LEN_M = 3.5
HALF_WID_M = 1.7

# Half-diagonal plus a little. The radius at which a hull can TURN, which is
# not the radius at which it can drive straight - that distinction cost an
# afternoon when the planner's 2.25 m and the driver's 4.5 m turned out to be
# the same number meaning different things.
TURN_R_M = math.hypot(HALF_LEN_M, HALF_WID_M) + 0.3
DRIVE_R_M = HALF_WID_M + 0.3

# ---- how it moves -------------------------------------------------------
#
# TOP SPEED IS SET BY THE RAYS, not by taste. Stopping distance is
# v^2 / (2*BRAKE), and a hull must stop inside what its front ray calls STOP
# or the measurement is decoration.
SPEED_MS = 11.0          # 40 km/h, a real medium
ACCEL_MS2 = 7.0
BRAKE_MS2 = 8.0
TURN_RATE = math.radians(45.0)     # per second, full lock

# ---- what it can see ----------------------------------------------------
#
# Eight rays, named, all reaching the same distance. They differ in what they
# MEAN, not in how far they see: each reads its own distance against its own
# pair of limits. That is the whole lesson of the 20 m change - a ray that
# answers "did I hit something" cannot express "I see it and it does not
# matter yet".
R_FL, R_FR, R_RL, R_RR, R_FRONT, R_REAR, R_RIGHT, R_LEFT = range(8)
RAY_COUNT = 8
RAY_LEN_M = 20.0

RAY_NAME = ("fl", "fr", "rl", "rr", "front", "rear", "right", "left")

# Where each ray starts on the hull, and which way it looks, in hull-local
# metres with +Z forward.
RAY_ORIGIN = ((-HALF_WID_M, HALF_LEN_M), (HALF_WID_M, HALF_LEN_M),
              (-HALF_WID_M, -HALF_LEN_M), (HALF_WID_M, -HALF_LEN_M),
              (0.0, HALF_LEN_M), (0.0, -HALF_LEN_M),
              (HALF_WID_M, 0.0), (-HALF_WID_M, 0.0))
RAY_ANGLE = (math.radians(-35.0), math.radians(35.0),
             math.radians(-145.0), math.radians(145.0),
             0.0, math.pi, math.radians(90.0), math.radians(-90.0))

# Below STOP the hull must act; above CLEAR it ignores the hit; between, it
# knows and has not acted.
STOP_M = (3.0, 3.0, 2.0, 2.0, 9.0, 2.0, 2.5, 2.5)
CLEAR_M = (8.0, 8.0, 5.0, 5.0, 18.0, 5.0, 6.0, 6.0)

RAY_CLEAR, RAY_CAUTION, RAY_STOP = 0, 1, 2


def ray_state(i, d):
    if d <= STOP_M[i]:
        return RAY_STOP
    if d >= CLEAR_M[i]:
        return RAY_CLEAR
    return RAY_CAUTION


# ---- arrival ------------------------------------------------------------
ARRIVE_M = 6.0

# How close terrain has to be before it stops a hull, as opposed to being
# steered around. Half a hull length plus a margin - close enough that the
# nose cannot come round in time - NOT a braking distance. See road_brain.
GROUND_STOP_M = HALF_LEN_M + 1.0
GROUND_SLOW_M = 10.0


def wrap_pi(a):
    while a > math.pi:
        a -= 2.0 * math.pi
    while a < -math.pi:
        a += 2.0 * math.pi
    return a


class Hull(object):
    """One tank. Position, heading, speed, and the route it was given.

    The brain does NOT live in here. A hull is what the world knows about a
    tank; what it decides is the brain's, and keeping them apart is what lets
    two brains be compared on the same hulls.
    """

    __slots__ = ("x", "z", "heading", "speed", "team", "route", "wp",
                 "name", "travelled", "stopped_s", "why", "alive")

    def __init__(self, x, z, heading=0.0, team=1, name=""):
        self.x = float(x)
        self.z = float(z)
        self.heading = float(heading)
        self.speed = 0.0
        self.team = int(team)
        self.route = []
        self.wp = 0
        self.name = name or "hull"
        self.travelled = 0.0
        self.stopped_s = 0.0
        self.why = "idle"
        self.alive = True

    @property
    def goal(self):
        if 0 <= self.wp < len(self.route):
            return self.route[self.wp]
        return None

    def arrived(self):
        return self.wp >= len(self.route)


class World(object):
    """The hulls, the ground, and one tick.

    THE GROUND COMES IN AS A CALLABLE, not as a grid. The sim should not care
    whether "can a tank be here" is answered by the .blk, by the bake, or by a
    test fixture with three walls in it - and making that a parameter is what
    lets the brain be exercised on a hand-built corner instead of a 39 MB map.
    """

    def __init__(self, blocked_at, hulls=None, bounds=None):
        # blocked_at(x, z) -> True if a hull cannot stand there.
        self.blocked_at = blocked_at
        self.hulls = list(hulls or ())
        self.bounds = bounds          # (x0, z0, x1, z1) or None
        self.grid = None              # a Grid, when the ground is an array
        self.t = 0.0
        self.brain = None             # set it and you are driving

    # ---- sensing --------------------------------------------------------
    def ray_ends(self, h):
        """Each ray as (origin_xz, direction_xz), in world metres."""
        ca, sa = math.cos(h.heading), math.sin(h.heading)
        out = []
        for i in range(RAY_COUNT):
            lx, lz = RAY_ORIGIN[i]
            # hull-local -> world. +Z forward, +X right.
            ox = h.x + lx * ca + lz * sa
            oz = h.z - lx * sa + lz * ca
            a = h.heading + RAY_ANGLE[i]
            out.append(((ox, oz), (math.sin(a), math.cos(a))))
        return out

    # Sample distances down a ray, built once. A quarter metre against a
    # half-metre cell is two samples a cell, which cannot step over one.
    _T = np.arange(0.25, RAY_LEN_M + 1e-9, 0.25)

    def sense(self, h):
        """Distance down each ray to the first thing that stops a hull.

        INDEXED, NOT WALKED. "you have the squares map" - the owner, watching
        this take 190 seconds to simulate 120. The first version stepped each
        ray in a Python loop and asked blocked_at() per sample: 8 rays x 80
        samples x 32 hulls x 30 ticks a second is six hundred thousand calls a
        simulated second, and every one of them recomputed the same row and
        column arithmetic.

        The grid is an ARRAY. All 640 sample points for a hull are built as
        one set of coordinates, converted to row/column in one operation and
        looked up in one indexing call - which is what he meant by "we index
        in to the data so its cheap and fast. row stride * Y, X".

        Falls back to the slow path when there is no grid, because a test
        fixture with three walls in it is a callable and not an array, and
        that is worth keeping.
        """
        if self.grid is None:
            return self._sense_slow(h)

        T = World._T
        ca, sa = math.cos(h.heading), math.sin(h.heading)
        # Origins and directions for all eight rays, in world metres.
        ox = np.empty(RAY_COUNT); oz = np.empty(RAY_COUNT)
        dx = np.empty(RAY_COUNT); dz = np.empty(RAY_COUNT)
        for i in range(RAY_COUNT):
            lx, lz = RAY_ORIGIN[i]
            ox[i] = h.x + lx * ca + lz * sa
            oz[i] = h.z - lx * sa + lz * ca
            a = h.heading + RAY_ANGLE[i]
            dx[i] = math.sin(a); dz[i] = math.cos(a)

        # (RAY_COUNT, len(T)) of sample positions, then one lookup.
        px = ox[:, None] + dx[:, None] * T[None, :]
        pz = oz[:, None] + dz[:, None] * T[None, :]
        blocked = self.grid.blocked_many(px, pz)

        d = [RAY_LEN_M] * RAY_COUNT
        hit = [None] * RAY_COUNT
        any_hit = blocked.any(axis=1)
        first = blocked.argmax(axis=1)
        for i in range(RAY_COUNT):
            if any_hit[i]:
                d[i] = float(T[first[i]])
                hit[i] = "ground"

        # HULLS ARE FEW AND THE MAP IS NOT. Thirty boxes tested against eight
        # ray segments is nothing; doing it per sample point was most of the
        # cost of the old version for none of the accuracy.
        for o in self.hulls:
            if o is h or not o.alive:
                continue
            if (o.x - h.x) ** 2 + (o.z - h.z) ** 2 > (RAY_LEN_M + 8.0) ** 2:
                continue
            for i in range(RAY_COUNT):
                t = self._ray_hits_hull(ox[i], oz[i], dx[i], dz[i], o, d[i])
                if t is not None and t < d[i]:
                    d[i] = t
                    hit[i] = o
        return d, hit

    @staticmethod
    def _ray_hits_hull(ox, oz, dx, dz, o, limit):
        """Nearest point along the ray inside o's box, or None. Slab test in
        the box's own frame."""
        ca, sa = math.cos(-o.heading), math.sin(-o.heading)
        rx = ox - o.x, oz - o.z
        lx = rx[0] * ca + rx[1] * sa
        lz = -rx[0] * sa + rx[1] * ca
        vx = dx * ca + dz * sa
        vz = -dx * sa + dz * ca
        t0, t1 = 0.0, limit
        for p, v, half in ((lx, vx, HALF_WID_M), (lz, vz, HALF_LEN_M)):
            if abs(v) < 1e-9:
                if abs(p) > half:
                    return None
                continue
            a = (-half - p) / v
            b = (half - p) / v
            if a > b:
                a, b = b, a
            t0 = max(t0, a)
            t1 = min(t1, b)
            if t0 > t1:
                return None
        return t0 if t0 > 0.0 else None

    def _sense_slow(self, h, step_m=0.25):
        """The callable path, for a world whose ground is a function."""
        d = [RAY_LEN_M] * RAY_COUNT
        hit = [None] * RAY_COUNT
        for i, ((ox, oz), (dx, dz)) in enumerate(self.ray_ends(h)):
            t = step_m
            while t <= RAY_LEN_M:
                px, pz = ox + dx * t, oz + dz * t
                if self.blocked_at(px, pz):
                    d[i] = t; hit[i] = "ground"; break
                other = self._hull_at(px, pz, h)
                if other is not None:
                    d[i] = t; hit[i] = other; break
                t += step_m
        return d, hit

    def _hull_at(self, x, z, ignore):
        """Which other hull covers this point, if any. Box test in its frame."""
        for o in self.hulls:
            if o is ignore or not o.alive:
                continue
            dx, dz = x - o.x, z - o.z
            if dx * dx + dz * dz > 36.0:          # 6 m, cheap reject
                continue
            ca, sa = math.cos(-o.heading), math.sin(-o.heading)
            lx = dx * ca + dz * sa
            lz = -dx * sa + dz * ca
            if abs(lx) <= HALF_WID_M and abs(lz) <= HALF_LEN_M:
                return o
        return None

    # ---- one tick -------------------------------------------------------
    def step(self, dt):
        self.t += dt
        for h in self.hulls:
            if not h.alive or h.arrived():
                h.speed = 0.0
                h.why = "arrived" if h.arrived() else "dead"
                continue
            d, hit = self.sense(h)
            throttle, steer, why = (self.brain or road_brain)(self, h, d, hit)
            h.why = why
            self._advance(h, throttle, steer, dt)

    def _advance(self, h, throttle, steer, dt):
        # Steer first, then move along the new heading. A tank turns on its
        # tracks; there is no slip angle to model here.
        h.heading = wrap_pi(h.heading + steer * TURN_RATE * dt)

        want = SPEED_MS * max(-1.0, min(1.0, throttle))
        if want > h.speed:
            h.speed = min(want, h.speed + ACCEL_MS2 * dt)
        else:
            h.speed = max(want, h.speed - BRAKE_MS2 * dt)

        if abs(h.speed) < 1e-4:
            h.stopped_s += dt
            return
        h.stopped_s = 0.0

        stride = h.speed * dt
        nx = h.x + math.sin(h.heading) * stride
        nz = h.z + math.cos(h.heading) * stride
        # DO NOT DRIVE INTO GROUND. The rays are how the brain avoids this;
        # this is the backstop that keeps a bug from becoming a hull inside a
        # wall, where every later measurement is meaningless.
        if self.blocked_at(nx, nz):
            h.speed = 0.0
            h.why = "wall"
            return
        h.x, h.z = nx, nz
        h.travelled += abs(stride)

        g = h.goal
        if g is not None and math.hypot(g[0] - h.x, g[1] - h.z) < ARRIVE_M:
            h.wp += 1


# ---- brains -------------------------------------------------------------
#
# A brain is a function: (world, hull, ray distances, what each ray hit) ->
# (throttle, steer, why). Throttle is -1..1 of SPEED_MS, steer is -1..1 of
# TURN_RATE, and `why` is a short string that goes in the log and on screen.
#
# THAT SIGNATURE IS THE POINT. It takes measurements and returns intent, so a
# brain cannot quietly move a hull, rewrite a route or edit the map - three
# things nuTerra's driver does, and the reason its behaviour is hard to reason
# about. Anything a brain wants to remember, it remembers in its own instance.


def null_brain(w, h, d, hit):
    """Parks. The control: any brain that cannot beat this is not working."""
    return 0.0, 0.0, "parked"


def road_brain(w, h, d, hit):
    """Follow the route. Ease off for what is ahead. Stop before hitting it.

    DELIBERATELY NOT CLEVER. There is no go-around, no reverse, no boundary
    trace. This is the baseline the clever ones have to beat, and it exists
    because last night's capture could not tell whether the driving was bad or
    the ROUTE was - 46% of rows stopped in Traffic, against a path file that
    turned out to be the graph-walk fallback rather than the roads.

    A brain that follows a good route and stops for things answers that: if it
    drives a road end to end, the route is good and every later failure is the
    brain's.
    """
    g = h.goal
    if g is None:
        return 0.0, 0.0, "arrived"

    # STEER AT THE WAYPOINT. Bearing to it, in the hull's frame.
    want = math.atan2(g[0] - h.x, g[1] - h.z)
    err = wrap_pi(want - h.heading)
    steer = max(-1.0, min(1.0, err / math.radians(20.0)))

    # DO NOT DRIVE AND TURN AT ONCE when the turn is sharp. A tank that tries
    # both leaves the road on the outside of every corner, and the road is the
    # thing that was carefully placed.
    if abs(err) > math.radians(50.0):
        return 0.0, steer, "turning"

    # GROUND AND HULLS ARE DIFFERENT QUESTIONS, and conflating them is what
    # made the first version of this brain stop 21 of 30 hulls dead.
    #
    # Measured: the stopped hulls had ground a median of 8.50 m ahead against
    # a front STOP of 9.0 m, while 0 of 2,357 route waypoints stood in a
    # blocked cell. The route was fine. The RAY was fine. What was wrong was
    # reading them together.
    #
    # 9 m is the stopping distance from 11 m/s, and that is the right limit
    # for something that might move - another tank. It is the wrong limit for
    # terrain, because the front ray looks straight down the nose and on a
    # curving road the nose points at the OUTSIDE of the bend. A wall 8.5 m
    # ahead on a road that turns is not an obstacle; it is the corner, and the
    # route already goes round it.
    #
    # So: a hull ahead stops this hull at the full braking distance. Ground
    # ahead only stops it when it is close enough to actually hit, and the
    # answer to ground further off is to STEER - which the waypoint above is
    # already doing.
    fwd = (R_FRONT, R_FL, R_FR)
    near_hull = min([d[i] for i in fwd if isinstance(hit[i], Hull)] or [RAY_LEN_M])
    near_ground = min([d[i] for i in fwd if hit[i] == "ground"] or [RAY_LEN_M])

    # Close enough to hit before the nose comes round. Half a length plus a
    # margin, not a braking distance.
    if near_ground <= GROUND_STOP_M:
        return 0.0, steer, "wall ahead"

    stop_at, clear_at = STOP_M[R_FRONT], CLEAR_M[R_FRONT]
    if near_hull <= stop_at:
        return 0.0, steer, "traffic"

    # EASE OFF THROUGH THE CAUTION BAND, which is what the band is for. It
    # existed in the app for a day and nothing read it, so hulls ran at full
    # speed until the stop limit and then braked hard.
    throttle = 1.0
    if near_hull < clear_at:
        throttle = (near_hull - stop_at) / (clear_at - stop_at)
        throttle = max(0.25, min(1.0, throttle))
    # And slow for a corner that is closing, rather than stopping at it.
    if near_ground < GROUND_SLOW_M:
        f = (near_ground - GROUND_STOP_M) / (GROUND_SLOW_M - GROUND_STOP_M)
        throttle = min(throttle, max(0.3, f))
    return throttle, steer, "moving"


# ---- building a world from the real map ---------------------------------

class Grid(object):
    """The squares map, indexed.

    Rows run from wz_max DOWNWARD and x is fastest, so the index is
    stride*row + col with no flip - the same order as the bake, squares.u8 and
    the .blk. Getting that backwards produced a map that agreed with itself
    77% of the time and looked almost right, which is recorded in maze.to_cell
    and is worth not repeating.
    """

    __slots__ = ("mask", "n", "cell", "wx0", "wz1")

    def __init__(self, mask, n, cell_m, wx_min, wz_max):
        self.mask = mask
        self.n = int(n)
        self.cell = float(cell_m)
        self.wx0 = float(wx_min)
        self.wz1 = float(wz_max)

    def blocked_many(self, xs, zs):
        """Blocked, for arrays of world coordinates, in one indexing call.

        BIT 0 AND ONLY BIT 0. The byte carries kind, outland, solid, trunk and
        crushable above it, so `!= 0` would read an open fence as a wall.
        """
        c = ((xs - self.wx0) / self.cell).astype(np.int32)
        r = ((self.wz1 - zs) / self.cell).astype(np.int32)
        off = (r < 0) | (c < 0) | (r >= self.n) | (c >= self.n)
        np.clip(r, 0, self.n - 1, out=r)
        np.clip(c, 0, self.n - 1, out=c)
        return ((self.mask[r, c] & 0x01) != 0) | off      # off the map is wall

    def blocked(self, x, z):
        c = int((x - self.wx0) / self.cell)
        r = int((self.wz1 - z) / self.cell)
        if r < 0 or c < 0 or r >= self.n or c >= self.n:
            return True
        return bool(self.mask[r, c] & 0x01)


def world_from_blk(blk, hulls=None):
    """A world whose ground is the .blk - the same bytes the tanks will read.

    Bit 0 is the block flag and nothing else is. Reading `byte != 0` here
    would call every crushable cell a wall, which is the exact failure the
    format was built to end.
    """
    g = Grid(blk["mask"], blk["n"], blk["cell_m"], blk["wx_min"], blk["wz_max"])
    w = World(g.blocked, hulls)
    w.grid = g
    return w


def hull_on_road(road, index=0):
    """One hull at the head of a road, pointed down it."""
    pts = road.get("pts") or ()
    if len(pts) < 2:
        return None
    x, z = pts[0]
    hx, hz = pts[1]
    h = Hull(x, z, math.atan2(hx - x, hz - z),
             team=2 if road.get("team") == 2 else 1,
             name="r%02d" % index)
    h.route = [(float(p[0]), float(p[1])) for p in pts[1:]]
    return h


def hulls_per_side(roads, per_side=15):
    """A fleet: per_side hulls on each team, spread over that side's roads.

    FIFTEEN A SIDE is the owner's number and it is the game's - a standard
    battle. It matters more than it looks: the failures worth finding are the
    ones that only happen when hulls meet, and a fleet of one per road spawns
    everybody at a road head at t=0, which is a traffic jam the map never
    actually produces.

    Spread round-robin over the side's roads rather than filling the first
    ones, so two hulls sharing a road start a road apart instead of nose to
    tail.
    """
    out = []
    for team in (1, 2):
        mine = [r for r in roads
                if (2 if r.get("team") == 2 else 1) == team
                and len(r.get("pts") or ()) >= 2]
        if not mine:
            continue
        for k in range(per_side):
            h = hull_on_road(mine[k % len(mine)], index=len(out))
            if h is None:
                continue
            # Second and later hulls on a road start further down it, so they
            # are queued along the lane rather than stacked on its head.
            lap = k // len(mine)
            if lap:
                step = min(lap * 12, max(0, len(h.route) - 2))
                if step:
                    h.x, h.z = h.route[step - 1]
                    h.route = h.route[step:]
                    if h.route:
                        h.heading = math.atan2(h.route[0][0] - h.x,
                                               h.route[0][1] - h.z)
            out.append(h)
    return out


def hulls_on_roads(roads, per_road=1, name_from=0):
    """One hull at the head of each road. Kept for the whole-network view."""
    out = []
    for i, r in enumerate(roads):
        for k in range(per_road):
            h = hull_on_road(r, index=i + name_from)
            if h is not None:
                out.append(h)
    return out


# ---- radar --------------------------------------------------------------
#
# "we will start with trying to teach radar what it is seeing. I think we use
#  120 degrees off nose and back and scan both. make rays visible and show
#  where the intersect a square. one tank on map 30 rays front and back only.
#  zero speed." - the owner, 2026-09-16
#
# TWO ARCS, NOT A CIRCLE. A tank drives forwards and reverses; it does not
# strafe. The 120 degrees either side of the nose is where it is going and the
# 120 behind is where it can retreat to, and the 60 degrees off each flank are
# deliberately blind - a hull beside you is neither a path nor an escape, and
# spending rays there buys a picture nobody acts on.
#
# THIRTY RAYS, so 15 an arc: one every 8.6 degrees. At 20 m that is a 3.0 m
# gap between neighbouring rays at full reach, which is narrower than a hull -
# so nothing tank-sized can sit in the gap between two rays and go unseen.
RADAR_RAYS = 30
RADAR_ARC_DEG = 120.0


def radar_angles(n=RADAR_RAYS, arc_deg=RADAR_ARC_DEG):
    """Ray bearings relative to the nose, front arc then rear arc.

    Returned as (angle, arc) so a caller can colour or filter by arc without
    re-deriving which half of the list it is looking at.
    """
    half = n // 2
    span = math.radians(arc_deg)
    out = []
    for k in range(half):
        # Centre of each of `half` equal slices, so the arc is covered evenly
        # and no ray lands exactly on the nose - which would make the centre
        # ray a special case in every reading of this.
        a = -span * 0.5 + span * (k + 0.5) / half
        out.append((a, "front"))
    for k in range(n - half):
        a = math.pi + (-span * 0.5 + span * (k + 0.5) / (n - half))
        out.append((wrap_pi(a), "rear"))
    return out


class RadarHit(object):
    """One ray's answer: how far, what it found, and WHICH SQUARE it found it in.

    The square is the point of this. "show where the intersect a square" - a
    distance alone cannot be checked against the map by eye, but a highlighted
    cell can, and a radar that is wrong will light the wrong cell in a way
    that is obvious on screen and invisible in a number.
    """

    __slots__ = ("angle", "arc", "dist", "what", "x", "z", "row", "col")

    def __init__(self, angle, arc, dist, what, x, z, row, col):
        self.angle = angle
        self.arc = arc
        self.dist = dist
        self.what = what          # "ground", a Hull, or None for a clean miss
        self.x = x
        self.z = z
        self.row = row
        self.col = col

    @property
    def clear(self):
        return self.what is None


def radar_scan(w, h, n=RADAR_RAYS, arc_deg=RADAR_ARC_DEG, reach=RAY_LEN_M,
               step_m=0.25):
    """Sweep both arcs from this hull and report every ray.

    Indexed against the grid the same way sense() is - all samples for all
    rays in one lookup - so a full 30 ray scan is a handful of array
    operations rather than thousands of Python calls.
    """
    angs = radar_angles(n, arc_deg)
    T = np.arange(step_m, reach + 1e-9, step_m)
    ox = np.empty(len(angs)); oz = np.empty(len(angs))
    dx = np.empty(len(angs)); dz = np.empty(len(angs))
    ca, sa = math.cos(h.heading), math.sin(h.heading)
    for i, (a, arc) in enumerate(angs):
        # Off the nose for the front arc, off the tail for the rear one - but
        # both start at the hull's CENTRE, so a hit distance is measured from
        # one place and two rays in different arcs are comparable.
        ox[i] = h.x
        oz[i] = h.z
        wa = h.heading + a
        dx[i] = math.sin(wa); dz[i] = math.cos(wa)

    px = ox[:, None] + dx[:, None] * T[None, :]
    pz = oz[:, None] + dz[:, None] * T[None, :]

    if w.grid is not None:
        blocked = w.grid.blocked_many(px, pz)
    else:
        blocked = np.zeros(px.shape, bool)
        for i in range(px.shape[0]):
            for j in range(px.shape[1]):
                blocked[i, j] = w.blocked_at(px[i, j], pz[i, j])

    out = []
    any_hit = blocked.any(axis=1)
    first = blocked.argmax(axis=1)
    for i, (a, arc) in enumerate(angs):
        if any_hit[i]:
            t = float(T[first[i]])
            what = "ground"
        else:
            t = float(reach)
            what = None
        hx = h.x + dx[i] * t
        hz = h.z + dz[i] * t
        row = col = -1
        if w.grid is not None:
            col = int((hx - w.grid.wx0) / w.grid.cell)
            row = int((w.grid.wz1 - hz) / w.grid.cell)
        out.append(RadarHit(a, arc, t, what, hx, hz, row, col))
    return out
