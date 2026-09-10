"""The box of boxes: every path tried, scored by how it completes.

    python path_cards.py [map] [--png out.png]

One box per target point. Each box holds report cards; each card is a chain of
at most MAX_DEPTH new points trying to reach that target. Every fork becomes
its own card - both sides of every ring, and every gap through a nest - and
the recursion carries its OWN point list down each branch, so no branch can
write into a sibling's path.

The test at each point:

    ray at the target.
      no collision      -> take the line, stop at STANDOFF. done.
      collision         -> grow a ring around the object it hit.
          ring swallows the target  -> take where the ray crosses the ring
                                       and call this target finished. It is
                                       fine for the target to be on the far
                                       side; crossing is close enough.
          a tangent comes into view -> fork: left, right, and every gap if
                                       more than one object is in the ring.

The choice, in order:

    completed?          no  -> thrown out
    every turn flyable? no  -> never built in the first place
    fewest points            -> wins
    still tied               -> SMOOTH_BIAS blends smoothness against length

Nothing is scored on how much it fought. The ring marks (5 alone, 4 for a
nest) are recorded because they explain the shape of a path, and are not
consulted when choosing one.
"""
import math
import os
import sys

import numpy as np
from scipy import ndimage

import radar_commit as nav

# ---------------------------------------------------------------- the rules

STANDOFF = 6.0        # m held off an object, and off the target
RING_STEP = 0.5       # m the ring grows by each time it fails to help.
                      # Fine on purpose: the SMALLEST ring that clears gives
                      # the tangent closest to straight ahead, and therefore
                      # the gentlest turn. Growing in 4 m jumps overshot and
                      # every branch came out as a hard corner.
RING_MAX = 160.0      # m - past this the object is scenery, not an obstacle
MAX_DEPTH = 6         # new points a single card may spend
MIN_LEG = 12.0        # m - shorter than this and there is no room to bank
RING_SAMPLES = 4      # ring sizes to take tangents at, spread across the
                      # whole usable range. Taking only the FIRST ring that
                      # cleared gave one branch per collision and therefore
                      # one card per box - the scoring had nothing to choose
                      # between. A wider ring rejoins the target at a
                      # shallower angle, which is the gentle turn the radius
                      # rule keeps asking for, so the range is where the
                      # alternatives live.
MAX_BRANCH = 10       # cap per collision, or depth 6 is unbounded
MIN_CLEAR = 3.0       # m of room an anchor must have all round it
SMOOTH_BIAS = 0.75    # 1.0 = smoothest wins ties, 0.0 = shortest wins

# MIN TURN RADIUS - the rule for "not natural any more".
#
# Not an angle. An angle alone says nothing: 60 degrees between two 200 m
# legs is a lazy bank, and 60 degrees between two 8 m hops is a pirouette at
# the same heading change. What decides it is whether a fillet of radius
# MIN_TURN_R fits inside the two legs that meet - tangent length
# r*tan(theta/2) against half of the shorter leg. One rule, and it tightens
# by itself exactly where the points bunch up.
#
# 25 m is a starting dial, not a measurement. radar_commit flies STEP = 2 m
# at up to TURN_STEP_DEG = 32 deg, which is a 3.6 m radius - a pivot, and the
# reason its paths can look like they were flown by something hovering.
MIN_TURN_R = 25.0

# ---------------------------------------------------------------- the trace
#
# Set TRACE to a callable and the search narrates itself as it runs: every
# ray, every ring expansion, every tangent tried, every anchor placed and
# every card finished. It is how the search is WATCHED - the numbers say six
# of twelve boxes solved, and only the picture says why the other six were
# not. Off, it costs one None test per event.
TRACE = None


class Stopped(Exception):
    """Raised out of the TRACE hook to abandon a search in progress.

    Its own type so a watcher pressing stop is not reported as the search
    falling over."""


def ev(kind, **kw):
    if TRACE is not None:
        TRACE(kind, kw)


class Card(object):
    """One candidate chain of points, and how it ended."""

    __slots__ = ("points", "marks", "reached", "why")

    def __init__(self, points, marks, reached, why=""):
        self.points = points
        self.marks = marks
        self.reached = reached
        self.why = why

    def turning(self):
        """Sum of squared deflections. Squared so that, between two paths
        that are both flyable, turning spread over four gentle banks beats
        the same total taken in one."""
        tot = 0.0
        for i in range(1, len(self.points) - 1):
            tot += deflection(self.points[i - 1], self.points[i],
                              self.points[i + 1]) ** 2
        return tot

    def length(self):
        return sum(math.hypot(self.points[i + 1][0] - self.points[i][0],
                              self.points[i + 1][1] - self.points[i][1])
                   for i in range(len(self.points) - 1))


# ------------------------------------------------------------------ geometry

def deflection(a, b, c):
    """How far the course bends at b, in radians. 0 is straight on."""
    h1 = math.atan2(b[1] - a[1], b[0] - a[0])
    h2 = math.atan2(c[1] - b[1], c[0] - b[0])
    d = h2 - h1
    while d > math.pi:
        d -= 2 * math.pi
    while d < -math.pi:
        d += 2 * math.pi
    return abs(d)


def turn_ok(a, b, c, r_min=None):
    """Can a camera actually fly a->b->c?

    The corner is rounded by an arc of radius r_min; that arc needs
    r_min*tan(theta/2) of straight on each side of b to touch down. If that
    does not fit in half of the shorter leg, the turn cannot be flown at this
    radius no matter how the points are smoothed afterwards - the smoothing
    would just cut the corner off the obstacle it was placed to avoid.
    """
    r_min = MIN_TURN_R if r_min is None else r_min
    th = deflection(a, b, c)
    if th < 1e-6:
        return True
    if th > math.pi - 1e-3:
        return False                      # straight back the way it came
    t = r_min * math.tan(th / 2.0)
    l1 = math.hypot(b[0] - a[0], b[1] - a[1])
    l2 = math.hypot(c[0] - b[0], c[1] - b[1])
    return t <= 0.5 * min(l1, l2) + 1e-9


def tangents(px, pz, cx, cz, r):
    """The two points on circle(c, r) where a line from p touches it."""
    dx, dz = cx - px, cz - pz
    d2 = dx * dx + dz * dz
    if d2 <= r * r:
        return []                          # inside it; nothing to aim at
    d = math.sqrt(d2)
    a0 = math.atan2(dz, dx)
    # ASIN, not acos. The right angle in the triangle p-centre-touch is at
    # the TOUCH POINT, because the radius meets the tangent square - so r is
    # the side OPPOSITE the angle at p and the offset is asin(r/d). With acos
    # the touch points come out 8.25 from a centre they are supposed to be 6
    # from, and every tangent aims just past the thing it is going round.
    off = math.asin(max(-1.0, min(1.0, r / d)))
    L = math.sqrt(max(0.0, d2 - r * r))
    out = []
    for s in (+1.0, -1.0):
        a = a0 + s * off
        out.append((px + math.cos(a) * L, pz + math.sin(a) * L, a, L))
    return out


def has_room(world, x, z, r=None):
    """Is there room to sit here?

    An anchor stops being a place the camera can be when it is jammed against
    geometry: the next box then starts with the collision closer than the
    standoff, no ring can be drawn round it, and the whole target fails as
    "against the wall". That is not a planning problem, it is where the
    PREVIOUS point was put. Eight spokes is enough to catch it.
    """
    r = MIN_CLEAR if r is None else r
    for k in range(8):
        a = 2.0 * math.pi * k / 8.0
        if world.radar.march(x, z, math.cos(a), math.sin(a), r) < r - 1e-6:
            return False
    return True


def follow(world, px, pz, a, cap, target=None):
    """Every anchor worth placing along one bearing.

    Returns a LIST, because the two things an anchor wants are in conflict
    and picking one silently gets the other wrong. The spot that sees the
    target SOONEST gives the gentlest corner at the far end; the spot with
    ROOM round it is the one the next box can actually plan from. Measured
    both ways on the same route: demanding room fixed a box that was jammed
    5.9 m from a wall and broke a box that had been closing cleanly, because
    the roomy anchor sat further along and bent the corner.

    So both are offered and the scoring decides. That is what the box of
    cards is FOR - a conflict between two rules is exactly the thing it
    exists to settle, and resolving it here by fiat wastes it.
    """
    ux, uz = math.cos(a), math.sin(a)
    r = world.radar.march(px, pz, ux, uz, cap)
    limit = cap if r >= cap - 1e-6 else r - STANDOFF
    if limit < MIN_LEG:
        return []

    step = max(2.0, MIN_LEG * 0.5)
    out, first_seen = [], None

    if target is not None:
        d = MIN_LEG
        while d <= limit:
            qx, qz = px + ux * d, pz + uz * d
            if world.clear(qx, qz, target[0], target[1]):
                if first_seen is None:
                    first_seen = (qx, qz)
                if has_room(world, qx, qz):
                    out.append((qx, qz))
                    break
            d += step
        if first_seen is not None and first_seen not in out:
            out.insert(0, first_seen)

    if not out:
        # The target never came into sight on this bearing. Take the furthest
        # spot along it that still has room - that is the most progress this
        # detour can buy before the next box has to think again.
        d = limit
        while d >= MIN_LEG:
            qx, qz = px + ux * d, pz + uz * d
            if has_room(world, qx, qz):
                out.append((qx, qz))
                break
            d -= 2.0
    return out


def ray_circle_crossing(px, pz, tx, tz, cx, cz, r):
    """Where the segment p->t first crosses circle(c, r). p is outside, t in."""
    dx, dz = tx - px, tz - pz
    fx, fz = px - cx, pz - cz
    a = dx * dx + dz * dz
    b = 2.0 * (fx * dx + fz * dz)
    c = fx * fx + fz * fz - r * r
    disc = b * b - 4 * a * c
    if a < 1e-12 or disc < 0:
        return (tx, tz)
    sq = math.sqrt(disc)
    for s in ((-b - sq) / (2 * a), (-b + sq) / (2 * a)):
        if -1e-9 <= s <= 1.0 + 1e-9:
            return (px + dx * s, pz + dz * s)
    return (tx, tz)


# --------------------------------------------------------------------- world

class World(object):
    """The mask, the radar, and the obstacles as discrete OBJECTS.

    The navigator has only ever seen cells. A ring is drawn around a THING,
    and "is more than one thing inside this ring" is the question that
    separates a lone shed from a nest - so the blocked mask is labelled into
    connected components once, up front, and every object carries a centre
    and the radius that covers it.
    """

    def __init__(self, bake):
        raw, plan_m, _dist, _pad = nav.build_world(bake, None)
        self.bake = bake
        self.plan = plan_m
        self.radar = nav.Radar(bake, plan_m, raw, bake.mx)

        lab, n = ndimage.label(plan_m)
        self.lab = lab
        self.n = n
        if n == 0:
            self.cx = np.zeros(0)
            self.cz = np.zeros(0)
            self.rr = np.zeros(0)
            return

        idx = np.arange(1, n + 1)
        cen = ndimage.center_of_mass(plan_m, lab, idx)
        boxes = ndimage.find_objects(lab)
        cx, cz, rr = [], [], []
        for k, (r_, c_) in enumerate(cen):
            wx, wz = bake.world_of(c_, r_)
            sl = boxes[k]
            h = (sl[0].stop - sl[0].start) * bake.mx
            w = (sl[1].stop - sl[1].start) * bake.mx
            cx.append(wx)
            cz.append(wz)
            rr.append(0.5 * math.hypot(h, w))
        self.cx = np.array(cx)
        self.cz = np.array(cz)
        self.rr = np.array(rr)

    def object_at(self, x, z):
        """Which object covers this spot, or the nearest one to it."""
        c, r = self.bake.texel_of(x, z)
        c, r = int(round(c)), int(round(r))
        h, w = self.lab.shape
        best = 0
        for dr in (0, -1, 1, -2, 2):
            for dc in (0, -1, 1, -2, 2):
                rr_, cc_ = r + dr, c + dc
                if 0 <= rr_ < h and 0 <= cc_ < w and self.lab[rr_, cc_]:
                    best = int(self.lab[rr_, cc_])
                    break
            if best:
                break
        if best:
            return best - 1
        if self.n == 0:
            return None
        d = (self.cx - x) ** 2 + (self.cz - z) ** 2
        return int(np.argmin(d))

    def hit(self, x, z, tx, tz):
        """Ray at the target. None if it gets there."""
        d = math.hypot(tx - x, tz - z)
        if d < 1e-6:
            return None
        ux, uz = (tx - x) / d, (tz - z) / d
        r = self.radar.march(x, z, ux, uz, d)
        if r >= d - 1e-6:
            return None
        return (x + ux * r, z + uz * r, r)

    def clear(self, x, z, tx, tz):
        d = math.hypot(tx - x, tz - z)
        if d < 1e-6:
            return True
        return self.radar.clear(x, z, (tx - x) / d, (tz - z) / d, d)

    def inside_ring(self, cx, cz, r):
        """Every object COMPLETELY inside circle(c, r)."""
        if self.n == 0:
            return []
        d = np.hypot(self.cx - cx, self.cz - cz)
        return list(np.nonzero(d + self.rr <= r)[0])


# ----------------------------------------------------------------- the search

def gaps(world, px, pz, ring_c, ring_r, members, step_deg=0.6):
    """A tight sweep for the ways BETWEEN the things in a nest.

    Only across the angular span the ring covers, at a fine step - the whole
    point of a nest is that the way through it is narrower than anything a
    32-degree navigator step would ever land on.
    """
    cx, cz = ring_c
    d = math.hypot(cx - px, cz - pz)
    if d <= ring_r:
        return []
    a0 = math.atan2(cz - pz, cx - px)
    half = math.asin(max(-1.0, min(1.0, ring_r / d)))
    reach = d + ring_r
    n = max(3, int(2 * half / math.radians(step_deg)))
    ok = []
    for i in range(n + 1):
        a = a0 - half + (2 * half) * i / float(n)
        ok.append(world.radar.march(px, pz, math.cos(a), math.sin(a),
                                    reach) >= reach - 1e-6)
    out, run = [], None
    for i, good in enumerate(ok + [False]):
        if good and run is None:
            run = i
        elif not good and run is not None:
            mid = (run + i - 1) / 2.0
            a = a0 - half + (2 * half) * mid / float(n)
            out.append((px + math.cos(a) * reach, pz + math.sin(a) * reach))
            run = None
    return out


def _file(out, card):
    """Put a card in the box, and say so."""
    out.append(card)
    ev("card", points=list(card.points), reached=card.reached, why=card.why)
    return card


def ang_diff(a, b):
    d = a - b
    while d > math.pi:
        d -= 2 * math.pi
    while d < -math.pi:
        d += 2 * math.pi
    return d


def spread(items, k):
    """k items taken evenly across a list, always including both ends."""
    n = len(items)
    if n <= k:
        return items
    return [items[int(round(i * (n - 1) / float(k - 1)))] for i in range(k)]


def sweep_out(world, px, pz, target, cap, span_deg=120.0, step_deg=1.0,
              keep=6):
    """Every clear way out of here, widest runs first.

    Used when there is no ring to go round - either we are inside it, or the
    collision is closer than the standoff. Sweeps a fan centred on the target
    bearing, groups the bearings that run clear, and offers the middle of
    each run. Widest first because the widest gap is the one most likely to
    still be a gap once the camera has width.
    """
    a0 = math.atan2(target[1] - pz, target[0] - px)
    half = math.radians(span_deg) * 0.5
    n = max(8, int(2 * half / math.radians(step_deg)))
    ok = []
    for i in range(n + 1):
        a = a0 - half + (2 * half) * i / float(n)
        ok.append(world.radar.march(px, pz, math.cos(a), math.sin(a),
                                    MIN_LEG * 1.5) >= MIN_LEG * 1.5 - 1e-6)
    runs, start = [], None
    for i, good in enumerate(ok + [False]):
        if good and start is None:
            start = i
        elif not good and start is not None:
            runs.append((i - start, (start + i - 1) / 2.0))
            start = None
    runs.sort(reverse=True)
    out = []
    for _w, mid in runs[:keep]:
        a = a0 - half + (2 * half) * mid / float(n)
        out.extend(follow(world, px, pz, a, cap, target))
    return out


def expand(world, pos, target, points, marks, depth, out, budget):
    """One branch. Everything it spawns carries its own points list."""
    if budget[0] <= 0:
        return
    budget[0] -= 1

    def offer(p, mark=None, why=""):
        """Add a point to THIS branch and hand back a fresh list.

        A refusal is RECORDED, not swallowed. Silently returning None meant a
        box where every branch was an unflyable corner produced zero cards -
        not one failed card, zero - and the report could only say "NOTHING
        FLYABLE" without saying what it had rejected or why. A dead end is a
        result.
        """
        if len(points) >= 2 and not turn_ok(points[-2], points[-1], p):
            _file(out, Card(points + [p], marks, False, "turn too sharp"))
            return None
        if len(points) >= 1 and math.hypot(p[0] - points[-1][0],
                                           p[1] - points[-1][1]) < 1e-6:
            _file(out, Card(list(points), list(marks), False, "went nowhere"))
            return None
        return (points + [p], marks + ([mark] if mark is not None else []))

    tx, tz = target
    h = world.hit(pos[0], pos[1], tx, tz)
    ev("ray", x=pos[0], z=pos[1], tx=tx, tz=tz, depth=depth,
       hit=(None if h is None else (h[0], h[1])))

    if h is None:
        d = math.hypot(tx - pos[0], tz - pos[1])
        if d <= STANDOFF:
            _file(out, Card(list(points), list(marks), True, "already there"))
            return
        f = (d - STANDOFF) / d
        p = (pos[0] + (tx - pos[0]) * f, pos[1] + (tz - pos[1]) * f)
        got = offer(p)
        if got:
            _file(out, Card(got[0], got[1], True, "clear line"))
        return

    if depth >= MAX_DEPTH:
        _file(out, Card(list(points), list(marks), False, "out of points"))
        return

    # THE RING GOES ROUND THE COLLISION, NOT ROUND THE BLOB.
    #
    # It was centred on the connected component's centroid with a radius
    # covering its whole bounding box - and on a dilated mask a component is
    # often a whole village block, so the very first ring had a 100 m radius
    # and swallowed a target 44 m away before a single tangent was tried.
    # Every box then "arrived" on its first card: 13 boxes, 13 cards, one of
    # them declaring the target reached after moving one metre. A search that
    # never branches is not a search.
    #
    # Centred on the hit, growing from STANDOFF, it means what it says:
    # larger and larger rings around the thing in the way, until a tangent
    # clears it - or until the ring is wide enough to contain the target,
    # which is then a real statement about how much geometry is between us.
    oi = world.object_at(h[0], h[1])
    cx, cz = h[0], h[1]

    # HOW BIG THE RING MAY GET.
    #
    # It is centred on the collision, so it can never usefully grow past our
    # own distance to that collision: the moment r reaches it we are INSIDE
    # the ring, and a point inside a circle has no tangent to it. Not "we can
    # never get there" - it means the ring has stopped being the right tool
    # from HERE, because there is no way round a thing we are already within.
    # Backing off and trying again from further out is what the branch above
    # is for.
    reach = math.hypot(cx - pos[0], cz - pos[1])
    r_max = min(RING_MAX, reach - 1e-3)

    # GROW IT ALL THE WAY, AND KEEP EVERY SIZE THAT WORKS.
    #
    # The target check still comes first and still stops everything - but a
    # ring that clears is no longer the end of the growing. Every radius
    # whose tangent is reachable is remembered, and the range is sampled
    # afterwards, because the useful spread is between the tightest berth
    # that works and the widest one that still exists.
    clearing = []
    r = STANDOFF
    took = None
    while r <= r_max:
        if math.hypot(tx - cx, tz - cz) <= r:
            took = r
            break
        ts = tangents(pos[0], pos[1], cx, cz, r)
        good = [t for t in ts if world.clear(pos[0], pos[1], t[0], t[1])]
        ev("ring", cx=cx, cz=cz, r=r, x=pos[0], z=pos[1], depth=depth,
           tangents=[(t[0], t[1]) for t in ts],
           clear=[(t[0], t[1]) for t in good])
        if good:
            clearing.append((r, good))
        r += RING_STEP

    if took is not None:
        ev("took", cx=cx, cz=cz, r=took, x=pos[0], z=pos[1], depth=depth)
        p = ray_circle_crossing(pos[0], pos[1], tx, tz, cx, cz, took)
        got = offer(p)
        if got:
            _file(out, Card(got[0], got[1], True, "ring took the target"))
        return

    if not clearing:
        # THE RING HAD NO ROOM TO EXIST.
        #
        # We are closer to the collision than STANDOFF, so every ring we
        # could draw already contains us and has no tangent. It does NOT
        # mean the target is unreachable - it means we are against the wall,
        # and from against a wall you do not go round a circle, you look for
        # a way out. A wide fine sweep is the same tight scan a nest gets,
        # just centred on where we want to go instead of on a ring.
        wide = sweep_out(world, pos[0], pos[1], target, cap=nav.RADAR_RANGE)
        if not wide:
            _file(out, Card(list(points), list(marks), False,
                            "against the wall at %.1f m, no way out" % r_max))
            return
        for p in wide:
            got = offer(p, 3)
            if got:
                expand(world, p, target, got[0], got[1], depth + 1, out, budget)
        return

    # SAMPLE THE RANGE - tightest, widest, and evenly between.
    picks = spread(clearing, RING_SAMPLES)

    cap = max(2.0 * MIN_LEG, math.hypot(tx - pos[0], tz - pos[1]))
    cap = min(cap, nav.RADAR_RANGE)

    seen = []
    branches = []
    mark = 5
    for (rr_, good) in picks:
        members = world.inside_ring(cx, cz, rr_)
        alone = len(members) <= 1
        if not alone:
            mark = 4
        bearings = [t[2] for t in good]
        if not alone:
            for g in gaps(world, pos[0], pos[1], (cx, cz), rr_, members):
                if world.clear(pos[0], pos[1], g[0], g[1]):
                    bearings.append(math.atan2(g[1] - pos[1], g[0] - pos[0]))
        for a in bearings:
            # Two rings a metre apart give two bearings a fraction of a degree
            # apart and two cards that are the same card. Sampling the range
            # is only worth it if the samples differ.
            if any(abs(ang_diff(a, b)) < math.radians(1.5) for b in seen):
                continue
            seen.append(a)
            got_pts = follow(world, pos[0], pos[1], a, cap, target)
            for gp in got_pts:
                ev("anchor", x=pos[0], z=pos[1], px=gp[0], pz=gp[1],
                   depth=depth, r=rr_)
            branches.extend(got_pts)
            if len(branches) >= MAX_BRANCH:
                break
        if len(branches) >= MAX_BRANCH:
            break

    if not branches:
        _file(out, Card(list(points), list(marks), False,
                        "no tangent or gap in sight"))
        return

    for b in branches:
        got = offer(b, mark)
        if got:
            expand(world, b, target, got[0], got[1], depth + 1, out, budget)


def choose(cards, smooth_bias=SMOOTH_BIAS):
    """Completed, flyable, fewest points, then the slider."""
    ok = [c for c in cards if c.reached]
    if not ok:
        return None
    fewest = min(len(c.points) for c in ok)
    tied = [c for c in ok if len(c.points) == fewest]
    if len(tied) == 1:
        return tied[0]

    turns = [c.turning() for c in tied]
    lens = [c.length() for c in tied]

    def norm(v, xs):
        lo, hi = min(xs), max(xs)
        return 0.0 if hi - lo < 1e-9 else (v - lo) / (hi - lo)

    return min(tied, key=lambda c: (smooth_bias * norm(c.turning(), turns)
                                    + (1 - smooth_bias) * norm(c.length(), lens)))


def run(world, waypoints, on_box=None, budget_per_box=20000):
    """Walk the waypoints, one box at a time. EVERY target gets a box.

    Returns (runs, report, closed). `runs` is a list of connected stretches,
    not one path: a leg nobody could fly is a GAP, and joining across it
    would draw a straight line through the buildings that beat it.

    A stuck box used to `return` on the spot, so the first hard leg ended the
    whole run - boxes 6 through 12 were never attempted, there was no card
    set for those targets, and the picture stopped at the first wall instead
    of showing every wall. One bad leg is a fact about that leg.
    """
    pos = waypoints[0]
    runs = [[pos]]
    broken = []
    report = []
    for wi in range(1, len(waypoints)):
        target = waypoints[wi]
        out = []
        expand(world, pos, target, [pos], [], 0, out, [budget_per_box])
        best = choose(out)
        done = len([c for c in out if c.reached])
        why = {}
        for c in out:
            if not c.reached:
                why[c.why] = why.get(c.why, 0) + 1
        report.append((wi, len(out), done, best, why))
        if on_box is not None:
            on_box(wi, target, out, best)
        # A card that arrives with NO new points is not a failure - it means
        # the last box already left us inside this target's standoff, which
        # happens whenever two waypoints are closer together than STANDOFF.
        # Treating it as a dead end stopped the whole run at box 2.
        if best is None:
            # Pick up AT the target we could not reach and carry on, so the
            # boxes after this one are still tried. The gap stays a gap.
            broken.append(wi)
            pos = target
            runs.append([pos])
            continue
        runs[-1] += best.points[1:]
        pos = best.points[-1]
    return runs, report, not broken


# ------------------------------------------------------------------ the tool

def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    map_name = args[0] if args else "19_monastery"
    bake = nav.Bake(nav.FOLDER, map_name)
    world = World(bake)
    print("%s: %d objects on the mask" % (map_name, world.n))

    nx, nz = nav.load_plan(os.path.join(nav.FOLDER, map_name + "_plan.csv"))
    n = max(4, len(nx) // 12)
    wps = [(float(nx[i]), float(nz[i])) for i in range(0, len(nx), n)]
    print("%d waypoints" % len(wps))

    import time
    t0 = time.time()
    runs, report, closed = run(world, wps)
    el = time.time() - t0
    path = [p for r in runs for p in r]

    for (wi, tried, done, best, why) in report:
        if best is None:
            print("  box %2d: %5d cards, %4d completed -> NOTHING FLYABLE"
                  % (wi, tried, done))
            for reason, k in sorted(why.items(), key=lambda kv: -kv[1]):
                print("           %4d x %s" % (k, reason))
        else:
            print("  box %2d: %5d cards, %4d completed -> %d points, "
                  "turning %.2f, %.0f m  (%s)"
                  % (wi, tried, done, len(best.points) - 1, best.turning(),
                     best.length(), best.why))
    print()
    print("closed=%s  %d points in %d connected run%s  %.1f s"
          % (closed, len(path), len(runs), "" if len(runs) == 1 else "s", el))
    return 0 if closed else 1


if __name__ == "__main__":
    sys.exit(main())
