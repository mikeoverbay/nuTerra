"""
Point-to-point navigator: direct ray, acceptance rings, tangents, then a
bounded grid search.

A SECOND navigator, beside radar_commit.py rather than replacing it. That one
follows a nominal course A* laid down in advance and uses the radar to avoid
what the course drives through. This one goes straight from waypoint to
waypoint and only consults the map when the geometry actually needs thinking
about - which is the point, because the A* stage is what prices lanes out
before the flier ever sees them.

Four layers, cheapest first. Every step takes the first that answers:

  1  DIRECT    the ray to the target is clear -> fly it
  2  RING      accept arriving within R of the target rather than on it, with
               R grown 1, 2, 4, 6, 10 m until something is reachable. This is
               not a search: the acceptance circle IS an angular tolerance,
               half-angle asin(R / d), so it widens on its own as the target
               gets closer and costs one cone scan per ring.
  3  TANGENT   the shortest way round an obstacle grazes its edge, so the only
               intermediate points worth trying are the tangents. The fan hands
               them over for nothing: the blocked bearings form a contiguous
               run, and the first CLEAR bearing on each side of that run is the
               silhouette - exact, for any shape. A ring fitted round the
               blocker would be hopeless here; a circle round a 200 m wall has
               a 100 m radius and its tangents mean nothing.
  4  SEARCH    a bounded A* on the mask, in a window round the two points.
               Wall-following exists because a robot cannot see the map. This
               one can - the whole occupancy grid is in memory - so the way
               round is a search, not a guess. Optimal, and when it returns
               nothing that is a PROOF the target is unreachable, not a
               timeout.

Layer 3 fails on concavity - in a U both tangents lead further in - which is
exactly why 4 exists behind it. Layer 4 alone would be correct and slow; 1-3
answer the great majority of steps in microseconds.

EVERYTHING TRIED IS RECORDED. Every ray, with the layer that cast it and
whether it was clear, blocked, or the one taken; every acceptance; every
search. draw() paints them, and the log says which layer decided each move.
That is the whole reason this file exists in the shape it does - the last
navigator's behaviour could only be understood by instrumenting it.

    python radar_tangent.py [map] [--png out.png] [--log out.txt]
"""

import math
import os
import sys

import numpy as np
from PIL import Image, ImageDraw

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import radar_commit as nav
import flight_plan as fp


# --------------------------------------------------------------------------
# Behaviour
# --------------------------------------------------------------------------

STEP = 2.0              # metres per move
ACCEPT_R = 3.0          # within this of a waypoint and it is reached
RINGS = (1.0, 2.0, 4.0, 6.0, 10.0)   # acceptance radii tried, in order
RING_MAX = 10.0         # past this it is behind something, not merely off to
                        # one side - stop relaxing and let a real search answer
CONE_RAYS = 41          # bearings sampled inside an acceptance cone
SEARCH_PAD = 120.0      # metres of margin round the pair for the bounded A*
SEARCH_GRID_MAX = 1500  # cells a side before the window is pooled at all.
                        # High on purpose. Pooling a blocked mask is MAX-pooled
                        # - it has to be, or a lamppost vanishes - and that
                        # closes any corridor narrower than the pooled cell. At
                        # 400 a 680 m window pools by 3, which is 2.05 m cells
                        # against 1 m lanes: every lane sealed, and the search
                        # then reports "unreachable" for a crossing that is
                        # plainly open. This is the same fault as ROUTE_GRID at
                        # 512 and it deserves the same answer - resolve the
                        # corridor or do not bother. A 1000x350 window is ~350k
                        # cells, which A* clears in about a second, and this
                        # only runs when a step is genuinely blocked.
MAX_STEPS = 20000


class Stopped(Exception):
    """Raised by an on_step hook to abandon a run.

    A live view needs a stop that takes effect NOW, not at the next waypoint.
    The hook is already called every step, so it is the natural place to throw
    from, and run() lets it out untouched.
    """


class Try:
    """One ray that was cast, and what came of it.

    Kept for the picture. A navigator that only records what it DID cannot be
    debugged - the question is always why it did not do the other thing.
    """

    __slots__ = ("x", "z", "a", "r", "layer", "verdict")

    def __init__(self, x, z, a, r, layer, verdict):
        self.x, self.z, self.a, self.r = x, z, a, r
        self.layer = layer          # direct | ring | tangent | search
        self.verdict = verdict      # clear | blocked | TAKEN


class TangentNav:
    def __init__(self, bake, radar, log=None):
        self.bake = bake
        self.radar = radar
        self.cell = bake.mx
        self.tries = []
        self.path = []
        # The layer that decided each path SEGMENT: _take is called once
        # immediately before every path.append, so moves[i] is the move from
        # path[i] to path[i+1] and len(moves) == len(path) - 1. That is what
        # lets the drawn path say WHY it goes where it goes rather than only
        # where, which is the difference between watching it and reading it.
        self.moves = []
        self.reached = []           # (x, z, waypoint index, how)
        self.events = []
        self._log = log or (lambda m: None)

    # ---------------------------------------------------------------- probes

    def _ray(self, x, z, a, want, layer):
        """March a bearing and record it. True when clear for `want` metres."""
        r = self.radar.march(x, z, math.cos(a), math.sin(a), max(want, 1.0))
        ok = r >= want - 1e-6
        self.tries.append(Try(x, z, a, min(r, want), layer,
                              "clear" if ok else "blocked"))
        return ok

    def _take(self, x, z, a, dist, layer):
        self.tries.append(Try(x, z, a, dist, layer, "TAKEN"))
        self.moves.append(layer)

    # ------------------------------------------------------------- the layers

    def _direct(self, x, z, tx, tz):
        d = math.hypot(tx - x, tz - z)
        a = math.atan2(tz - z, tx - x)
        if d < 1e-6:
            return a
        return a if self._ray(x, z, a, d, "direct") else None

    def _ring(self, x, z, tx, tz):
        """Any bearing that passes within R of the target, R growing.

        The cone is asin(R/d) wide, so this needs no circle test - a bearing
        inside the cone that stays clear for d*cos(half) has passed within R by
        construction.
        """
        d = math.hypot(tx - x, tz - z)
        a_t = math.atan2(tz - z, tx - x)
        for R in RINGS:
            if R > RING_MAX or R >= d:
                break
            half = math.asin(min(1.0, R / d))
            reach = d * math.cos(half)
            best = None
            for k in range(CONE_RAYS):
                a = a_t - half + 2.0 * half * k / (CONE_RAYS - 1)
                if self._ray(x, z, a, reach, "ring"):
                    if best is None or abs(a - a_t) < abs(best - a_t):
                        best = a
            if best is not None:
                self._log("      ring R=%.0f m accepted, bearing off by %.1f deg"
                          % (R, math.degrees(abs(best - a_t))))
                return best
        return None

    def _tangents(self, x, z, tx, tz, heading):
        """The silhouette bearings either side of what is in the way.

        Straight off the fan: the blocked bearings form a run, and the first
        clear bearing outside it, each side, is the tangent.
        """
        d = math.hypot(tx - x, tz - z)
        a_t = math.atan2(tz - z, tx - x)
        fan = self.radar.fan(x, z, heading)
        if not fan:
            return None

        # index of the fan ray nearest the target bearing
        i_t = min(range(len(fan)),
                  key=lambda i: abs(nav.ang_norm(fan[i][0] - a_t)))
        near = min(d, nav.RADAR_RANGE)

        cands = []
        for direction in (+1, -1):
            i = i_t
            while 0 <= i < len(fan) and fan[i][1] < near - 1e-6:
                i += direction
            if not (0 <= i < len(fan)):
                continue
            a = fan[i][0]
            run = fan[i][1]
            # corner just past the silhouette
            cx = x + math.cos(a) * min(run, near)
            cz = z + math.sin(a) * min(run, near)
            self.tries.append(Try(x, z, a, min(run, near), "tangent", "clear"))
            # can the target be seen from there?
            dd = math.hypot(tx - cx, tz - cz)
            aa = math.atan2(tz - cz, tx - cx)
            seen = self.radar.clear(cx, cz, math.cos(aa), math.sin(aa), dd)
            self.tries.append(Try(cx, cz, aa, dd if seen else
                                  self.radar.march(cx, cz, math.cos(aa),
                                                   math.sin(aa), dd),
                                  "tangent", "clear" if seen else "blocked"))
            if seen:
                cands.append((min(run, near) + dd, a))
        if not cands:
            return None
        cands.sort()
        self._log("      tangent accepted, total %.0f m" % cands[0][0])
        return cands[0][1]

    def _search(self, x, z, tx, tz):
        """Bounded A* on the mask. Returns a bearing to the next corner."""
        b = self.bake
        pad = SEARCH_PAD
        x0, x1 = min(x, tx) - pad, max(x, tx) + pad
        z0, z1 = min(z, tz) - pad, max(z, tz) + pad
        ca, ra = b.texel_of(x0, z0)
        cb, rb = b.texel_of(x1, z1)
        # Into fresh names. Assigning c0 and then using it in max(c0, c1) reads
        # the value just written, which collapses the window - the search then
        # declared a 440 m crossing unreachable without moving a metre.
        c0 = int(np.clip(min(ca, cb), 0, b.w - 1))
        c1 = int(np.clip(max(ca, cb), 0, b.w - 1))
        r0 = int(np.clip(min(ra, rb), 0, b.h - 1))
        r1 = int(np.clip(max(ra, rb), 0, b.h - 1))
        if c1 - c0 < 2 or r1 - r0 < 2:
            return None

        sub = self.radar.plan[r0:r1 + 1, c0:c1 + 1]
        # Pool down if the window is large, MAX so a blocked cell survives.
        f = max(1, int(math.ceil(max(sub.shape) / float(SEARCH_GRID_MAX))))
        if f > 1:
            hh = (sub.shape[0] // f) * f
            ww = (sub.shape[1] // f) * f
            sub = sub[:hh, :ww].reshape(hh // f, f, ww // f, f).max(axis=(1, 3))

        cost = np.where(sub, np.inf, 1.0)

        def to_sub(px, pz):
            c, r = b.texel_of(px, pz)
            return (int(np.clip((r - r0) / f, 0, sub.shape[0] - 1)),
                    int(np.clip((c - c0) / f, 0, sub.shape[1] - 1)))

        s_rc = to_sub(x, z)
        g_rc = to_sub(tx, tz)
        if not np.isfinite(cost[s_rc]):
            cost[s_rc] = 1.0        # we are standing here, so it is passable
        if not np.isfinite(cost[g_rc]):
            self._log("      search: the target itself is inside an obstacle")
            return None
        try:
            cells = fp.astar(cost, s_rc, g_rc)
        except Exception as e:
            self._log("      search failed: %s" % e)
            return None
        if not cells:
            self._log("      search: NO ROUTE - target unreachable from here")
            return None

        # Aim at the furthest cell still in line of sight: the taut version of
        # the path, which is the tangent idea applied to what the search found.
        # world_of already puts the sample at the cell CENTRE, so the window
        # offset must not add another half cell on top - that was half a cell
        # of drift per step, in the direction of travel.
        aim = None
        aim_d = 0.0
        for (rr, cc) in cells[1:]:
            wx, wz = b.world_of(c0 + cc * f, r0 + rr * f)
            dd = math.hypot(wx - x, wz - z)
            aa = math.atan2(wz - z, wx - x)
            if dd > 1e-6 and self.radar.clear(x, z, math.cos(aa), math.sin(aa), dd):
                aim, aim_d = aa, dd
            elif aim is not None:
                break
        if aim is None:
            # The route exists but not one metre of it is in line of sight,
            # which means the first cell out is behind the standoff dilation.
            # Aim at the first cell regardless of sight and let the move test
            # decide - it is one cell, and refusing here is what stalled.
            rr, cc = cells[1] if len(cells) > 1 else cells[0]
            wx, wz = b.world_of(c0 + cc * f, r0 + rr * f)
            dd = math.hypot(wx - x, wz - z)
            if dd < 1e-6:
                self._log("      search: taut aim degenerate, first cell is here")
                return None
            self._log("      search: nothing in sight, stepping to the first cell")
            aim = math.atan2(wz - z, wx - x)
        self._log("      search: %d cells, taut aim %.0f m" % (len(cells), aim_d))
        self.tries.append(Try(x, z, aim, STEP * 3, "search", "clear"))
        return aim

    # ------------------------------------------------------------------ drive

    def export_path_csv(self, path):
        """The flown path, one point a line. What the run actually produced."""
        with open(path, "w", encoding="utf-8") as f:
            f.write("i,s_m,x,z" + chr(10))
            s_m = 0.0
            for i, (x, z) in enumerate(self.path):
                if i:
                    px, pz = self.path[i - 1]
                    s_m += math.hypot(x - px, z - pz)
                f.write("%d,%.3f,%.3f,%.3f%s" % (i, s_m, x, z, chr(10)))
        return len(self.path)

    def export_tries_csv(self, path):
        """EVERY ray cast, with the layer that cast it and how it ended.

        The path alone says what happened; this says what was considered and
        rejected, which is the half that explains it.
        """
        with open(path, "w", encoding="utf-8") as f:
            f.write("x,z,bearing_deg,range_m,layer,verdict" + chr(10))
            for t in self.tries:
                f.write("%.3f,%.3f,%.2f,%.3f,%s,%s%s"
                        % (t.x, t.z, math.degrees(t.a), t.r, t.layer,
                           t.verdict, chr(10)))
        return len(self.tries)

    def run(self, waypoints, on_step=None, step_every=1):
        """Fly the waypoints. on_step(self) is called as it goes.

        The callback is what makes a live view possible: the caller repaints
        from self.path and self.tries between steps instead of waiting for a
        picture at the end. step_every trades smoothness for speed - repainting
        a Tk canvas every 2 m of a 2 km route is most of the run.
        """
        x, z = waypoints[0]
        self.path.append((x, z))
        heading = 0.0
        steps = 0

        for wi in range(1, len(waypoints) + 1):
            tx, tz = waypoints[wi % len(waypoints)]
            self._log("waypoint %d/%d -> (%.0f, %.0f)"
                      % (wi, len(waypoints), tx, tz))
            stalled = 0
            while True:
                steps += 1
                if steps > MAX_STEPS:
                    self._log("  MAX_STEPS")
                    return False
                d = math.hypot(tx - x, tz - z)
                if d <= ACCEPT_R:
                    self._log("  reached (%.1f m) after %d steps" % (d, steps))
                    self.reached.append((x, z, wi, "on the point"))
                    if on_step is not None:
                        on_step(self)
                    break

                a = self._direct(x, z, tx, tz)
                how = "direct"
                if a is None:
                    a = self._ring(x, z, tx, tz)
                    how = "ring"
                if a is None:
                    a = self._tangents(x, z, tx, tz, heading)
                    how = "tangent"
                if a is None:
                    a = self._search(x, z, tx, tz)
                    how = "search"
                if a is None:
                    self._log("  STUCK at (%.0f, %.0f), %.0f m short" % (x, z, d))
                    self.reached.append((x, z, wi, "STUCK"))
                    return False

                # Take as much of the step as fits, rather than all or
                # nothing. The bearing came from a layer that proved SOMETHING
                # along it is clear - the taut aim can be one cell away while
                # STEP is 2 m, and insisting on the full 2 m walked into the
                # wall the search was routing around, then refused and stalled.
                ux, uz = math.cos(a), math.sin(a)
                move = min(STEP, d)
                while move > 0.25 and not self.radar.clear(x, z, ux, uz, move):
                    move *= 0.5
                if move <= 0.25:
                    stalled += 1
                    if stalled > 20:
                        self._log("  stalled - no forward motion fits")
                        self.reached.append((x, z, wi, "STUCK"))
                        return False
                    continue
                # Land in a FREE texel, not on the boundary of one.
                #
                # march stops at the first blocking cell, so a move of exactly
                # its range ends on the edge and texel_of rounds into the
                # blocked cell. Every ray from there returns 0 - the position
                # itself is blocked - so direct, ring and tangent all fail
                # forever and only the search crawls. Back off until the cell
                # the move lands in is genuinely free.
                nx_, nz_ = x + ux * move, z + uz * move
                for _ in range(6):
                    c_, r_ = self.bake.texel_of(nx_, nz_)
                    ci = int(np.clip(round(c_), 0, self.bake.w - 1))
                    ri = int(np.clip(round(r_), 0, self.bake.h - 1))
                    if not self.radar.plan[ri, ci]:
                        break
                    move *= 0.5
                    nx_, nz_ = x + ux * move, z + uz * move
                else:
                    stalled += 1
                    if stalled > 20:
                        self._log("  stalled - every landing cell is blocked")
                        self.reached.append((x, z, wi, "STUCK"))
                        return False
                    continue
                self._take(x, z, a, move, how)
                x, z, heading = nx_, nz_, a
                self.path.append((x, z))
                stalled = 0
                if on_step is not None and steps % step_every == 0:
                    on_step(self)
        return True


# --------------------------------------------------------------------------
# Picture
# --------------------------------------------------------------------------

COLOUR = {
    ("direct", "clear"):    (90, 200, 255, 70),
    ("direct", "blocked"):  (255, 90, 70, 95),
    ("ring", "clear"):      (120, 230, 160, 45),
    ("ring", "blocked"):    (200, 80, 60, 32),
    ("tangent", "clear"):   (255, 210, 90, 170),
    ("tangent", "blocked"): (180, 70, 90, 120),
    ("search", "clear"):    (200, 130, 255, 200),
}

# The same rays, for a LIVE view rather than the 2048 px picture above.
#
# Two different jobs. In draw() hundreds of rays land on the same pixels and
# the low alphas add up into a wash that says "it looked here a lot"; on
# screen only the last couple of sweeps exist at any moment, over a dark mask,
# at less than half the resolution - and at alpha 45 a single one-pixel ray is
# invisible. That is why the scan could not be seen going by.
#
# TAKEN is in this one and not in draw(). Offline the committed move is
# already drawn, as the path; live, the whole point is watching the DECISION
# arrive, so the move it settled on is the brightest thing on the screen.
COLOUR_LIVE = {
    ("direct", "clear"):    (90, 200, 255, 150),
    ("direct", "blocked"):  (255, 90, 70, 185),
    ("ring", "clear"):      (120, 230, 160, 120),
    ("ring", "blocked"):    (200, 80, 60, 95),
    ("tangent", "clear"):   (255, 210, 90, 235),
    ("tangent", "blocked"): (180, 70, 90, 200),
    ("search", "clear"):    (200, 130, 255, 240),
}

# The committed move is deliberately NOT a ray in either table.
#
# It was, briefly, at full alpha - and end to end those rays ARE the path, so
# they drew a second copy of it one pixel wide and unshiftable underneath the
# real line. Two lines for one thing, and the bright one was the wrong one.
# The decision is shown by colouring the PATH instead: same information, on
# the object it belongs to.
PATH_COLOUR = {
    "direct":  (255, 255, 255),     # straight at the target, nothing in the way
    "ring":    (120, 255, 170),     # aimed at the acceptance ring, not the point
    "tangent": (255, 205, 60),      # round the side of a blocker
    "search":  (215, 130, 255),     # gave up on rays and searched the grid
}


def draw(bake, radar, nvg, waypoints, out_png, side=2048):
    """Crop to where the work happened.

    Drawn over the whole 1400 m map the rays are a smudge - they are 90 m long
    on a 1400 m square. The interesting area is the box the path and its probes
    actually cover, so frame that and let a ray be a ray.
    """
    xs = [p[0] for p in nvg.path] + [w[0] for w in waypoints]
    zs = [p[1] for p in nvg.path] + [w[1] for w in waypoints]
    for t in nvg.tries:
        xs += [t.x, t.x + math.cos(t.a) * t.r]
        zs += [t.z, t.z + math.sin(t.a) * t.r]
    pad = 30.0
    x0, x1 = min(xs) - pad, max(xs) + pad
    z0, z1 = min(zs) - pad, max(zs) + pad
    span = max(x1 - x0, z1 - z0)
    cx, cz = 0.5 * (x0 + x1), 0.5 * (z0 + z1)
    x0, x1 = cx - span / 2, cx + span / 2
    z0, z1 = cz - span / 2, cz + span / 2

    c0, r1 = bake.texel_of(x0, z0)
    c1, r0 = bake.texel_of(x1, z1)
    c0 = int(max(0, min(c0, c1)))
    c1 = int(min(bake.w - 1, max(c0, c1)))
    r0 = int(max(0, min(r0, r1)))
    r1 = int(min(bake.h - 1, max(r0, r1)))

    raw = radar.raw[r0:r1 + 1, c0:c1 + 1]
    plan_m = radar.plan[r0:r1 + 1, c0:c1 + 1]
    img = np.zeros(raw.shape + (3,), np.uint8)
    img[...] = (16, 18, 22)
    img[plan_m] = (40, 38, 36)
    img[raw] = (118, 92, 48)
    im = Image.fromarray(img, "RGB").resize((side, side), Image.NEAREST)
    d = ImageDraw.Draw(im, "RGBA")
    sx = side / float(c1 - c0 + 1)
    sz = side / float(r1 - r0 + 1)

    def T(x, z):
        c, r = bake.texel_of(x, z)
        return ((c - c0) * sx, (r - r0) * sz)

    for t in nvg.tries:
        if t.verdict == "TAKEN":
            continue
        col = COLOUR.get((t.layer, t.verdict))
        if not col:
            continue
        d.line([T(t.x, t.z), T(t.x + math.cos(t.a) * t.r,
                               t.z + math.sin(t.a) * t.r)], fill=col, width=1)

    if len(nvg.path) > 1:
        d.line([T(px, pz) for px, pz in nvg.path],
               fill=(255, 46, 168, 255), width=max(2, side // 700))

    for i, (wx, wz) in enumerate(waypoints):
        px, py = T(wx, wz)
        rr = max(4, side // 260)
        d.ellipse([px - rr, py - rr, px + rr, py + rr],
                  outline=(90, 220, 255, 255), width=2)
        ar = ACCEPT_R * sx
        d.ellipse([px - ar, py - ar, px + ar, py + ar], outline=(90, 220, 255, 90))
        d.text((px + rr + 3, py - rr), str(i), fill=(150, 235, 255, 255))

    for (rx, rz, wi, how) in nvg.reached:
        px, py = T(rx, rz)
        col = (110, 255, 140, 255) if how != "STUCK" else (255, 70, 70, 255)
        rr = max(5, side // 200)
        d.line([px - rr, py - rr, px + rr, py + rr], fill=col, width=2)
        d.line([px - rr, py + rr, px + rr, py - rr], fill=col, width=2)

    d.text((10, 8), "radar_tangent - every ray tried", fill=(240, 240, 240))
    d.text((10, 24), "blue direct   green ring   amber tangent   "
                     "purple search   red blocked", fill=(205, 205, 205))
    d.text((10, 40), "pink = flown   circles = waypoints + accept radius   "
                     "X = reached", fill=(205, 205, 205))
    im.save(out_png)


def main():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    map_name = args[0] if args else "19_monastery"
    out_png = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                           "radar_tangent.png")
    log_path = None
    for i, a in enumerate(sys.argv):
        if a == "--png" and i + 1 < len(sys.argv):
            out_png = sys.argv[i + 1]
        if a == "--log" and i + 1 < len(sys.argv):
            log_path = sys.argv[i + 1]

    lines = []

    def log(m):
        lines.append(m)
        print(m)

    bake = nav.Bake(nav.FOLDER, map_name)
    raw, plan_m, dist_m, pad = nav.build_world(bake, None)
    radar = nav.Radar(bake, plan_m, raw, bake.mx)
    log("%s: %.2f%% blocked, %.2f%% after the %d-cell standoff (BODY_R %.1f m)"
        % (map_name, 100 * raw.mean(), 100 * plan_m.mean(), pad, nav.BODY_R))

    if "--hard" in sys.argv:
        # Straight across the monastery, both ways. The direct line runs
        # through the buildings, so nothing here is answerable by a ray.
        waypoints = [(-190.0, 40.0), (250.0, 40.0),
                     (250.0, -60.0), (-190.0, -60.0)]
        log("hard course: %d waypoints straight through the village"
            % len(waypoints))
    else:
        nx, nz = nav.load_plan(os.path.join(nav.FOLDER, map_name + "_plan.csv"))
        n = max(4, len(nx) // 12)
        waypoints = [(float(nx[i]), float(nz[i])) for i in range(0, len(nx), n)]
        log("%d waypoints from the plan" % len(waypoints))

    nvg = TangentNav(bake, radar, log=log)
    ok = nvg.run(waypoints)
    flown = sum(math.hypot(nvg.path[i + 1][0] - nvg.path[i][0],
                           nvg.path[i + 1][1] - nvg.path[i][1])
                for i in range(len(nvg.path) - 1))
    by = {}
    for t in nvg.tries:
        if t.verdict == "TAKEN":
            by[t.layer] = by.get(t.layer, 0) + 1
    log("")
    log("closed=%s  flown=%.0f m  moves by layer: %s  rays tried=%d"
        % (ok, flown, by, len(nvg.tries)))

    draw(bake, radar, nvg, waypoints, out_png)
    log("wrote " + out_png)

    stem = os.path.splitext(out_png)[0]
    n = nvg.export_path_csv(stem + "_path.csv")
    m = nvg.export_tries_csv(stem + "_tries.csv")
    log("wrote %s_path.csv (%d points) and %s_tries.csv (%d rays)"
        % (stem, n, stem, m))
    if log_path:
        open(log_path, "w", encoding="utf-8").write(chr(10).join(lines) + chr(10))


if __name__ == "__main__":
    main()
