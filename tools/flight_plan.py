"""
Camera flight planner for nuTerra.

Reads the three layers MapFlightBake exports to %TEMP%\\nuTerra\\flight\\ and
plans a closed loop the camera can fly without hitting anything, then writes the
route out and draws it in pink over the collision map so it can be judged by eye
before any of this moves into the app.

Everything here is deliberately offline. The point is to be able to change the
algorithm and look at the answer in seconds, rather than rebuild nuTerra to find
out that a turn radius was wrong.

    python flight_plan.py [map_name]

Outputs, alongside the bake:
    <map>_plan.png   the collision map with the route drawn on it
    <map>_plan.csv   the route itself - world position, altitude, heading
"""

import csv
import os
import struct
import sys
import heapq

import numpy as np
from scipy import ndimage
from scipy.interpolate import splprep, splev
from PIL import Image, ImageDraw

# ---------------------------------------------------------------------------
# Flight envelope. These are the numbers worth arguing about; everything below
# is machinery.
# ---------------------------------------------------------------------------

AGL_MIN = 25.0      # metres above bare terrain, never less than this
CLEARANCE = 15.0    # metres above whatever the corridor's tallest obstacle is
CLIMB_LIMIT = 55.0  # an obstacle taller than this is flown AROUND, not over
BODY_RADIUS = 6.0   # horizontal half-width kept clear of a hard obstacle.
                    # 6, not 8. Measured at 1 m altitude: at 8 m the dilation
                    # fragments Abbey - the largest connected free region falls
                    # to 73.7% of free space, so whole pockets become
                    # unreachable and no route exists between two waypoints. At
                    # 6 it is 97.2% connected. The gaps in a village are simply
                    # not 16 m wide.
CORRIDOR = 18.0     # metres either side of the path the altitude must clear

N_WAYPOINTS = 14    # control points on the circuit
STANDOFF = 70.0     # metres to hold outside the content core
MASS_FRAC = 0.72    # fraction of a spoke's content mass the orbit stays outside
R_MIN_FRAC = 0.22   # circuit radius floor, as a fraction of the map half-extent
R_MAX_FRAC = 0.80   # and its ceiling
SAMPLE_STEP = 4.0   # metres between samples along the flown curve

ROUTE_GRID = 512    # cells on a side for the routing pass. 512, not 256: the
                    # grid max-pools, so at 256 a 5.5 m cell holding one fence
                    # post blocks the whole cell. That is tolerable when the
                    # route only has to clear 55 m towers and fatal when it has
                    # to thread between hedges.

# The rule the NAVIGATOR will fly by. The course has to be routable at the
# altitude it will actually be flown at.
#
# This was the bug that cost the most: the course was planned avoiding only
# things over CLIMB_LIMIT, on the reasoning that the navigator would sort out
# the rest. It cannot - it thrashed for 3496 reversals and never closed. A
# standoff sweep settled it: at 1 m altitude the loop failed to close at 8, 6,
# 4, 3 AND 2 m of standoff, so the standoff was never the binding constraint.
# The course was simply running through buildings.
FLIGHT_AGL = 1.0
FLIGHT_MARGIN = 0.5
FLIGHT_BLOCK_H = FLIGHT_AGL - FLIGHT_MARGIN


# ---------------------------------------------------------------------------
# The bake
# ---------------------------------------------------------------------------

class Bake:
    """The three baked layers plus the world-to-texel mapping that makes them
    mean anything."""

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

        # Cells nothing rasterised into carry the clear-depth sentinel. Left as
        # they are they read as a 500 m pit, which the altitude pass would
        # happily dive into.
        self.no_data = self.floor <= self.empty + 1.0
        if self.no_data.any():
            fill = float(np.median(self.floor[~self.no_data]))
            self.floor[self.no_data] = fill
            self.top[self.no_data] = fill

        self.obstacle = np.maximum(self.top - self.floor, 0.0)

        self.mx = (self.wx_max - self.wx_min) / self.w   # metres per column
        self.mz = (self.wz_max - self.wz_min) / self.h   # metres per row

    def _r32(self, path):
        with open(path, "rb") as f:
            raw = f.read()
        a = np.frombuffer(raw, dtype="<f4").astype(np.float64)
        return a.reshape(self.h, self.w).copy()

    # world <-> texel, straight off the header the bake wrote

    def world_of(self, col, row):
        return (self.wx_min + (col + 0.5) * self.mx,
                self.wz_max - (row + 0.5) * self.mz)

    def texel_of(self, x, z):
        c = (x - self.wx_min) / self.mx - 0.5
        r = (self.wz_max - z) / self.mz - 0.5
        return c, r

    def sample(self, field, x, z):
        """Nearest-texel read, clamped at the edges."""
        c, r = self.texel_of(x, z)
        ci = int(np.clip(round(c), 0, self.w - 1))
        ri = int(np.clip(round(r), 0, self.h - 1))
        return field[ri, ci]

    def describe(self):
        o = self.obstacle
        print(f"  {self.map_name}: {self.w}x{self.h} over "
              f"{self.wx_max - self.wx_min:.0f} x {self.wz_max - self.wz_min:.0f} m "
              f"({self.mx:.2f} m per texel)")
        print(f"  terrain {self.floor.min():.1f} .. {self.floor.max():.1f} m")
        for t in (1, 5, 15, 30, CLIMB_LIMIT, 100):
            print(f"  obstacle > {t:5.0f} m : {100.0 * (o > t).mean():6.2f}% of the map")


# ---------------------------------------------------------------------------
# Horizontal routing
# ---------------------------------------------------------------------------

def build_cost(bake):
    """A coarse grid to route on, plus the hard-blocked set.

    Two separate ideas, and keeping them apart is what stops the camera either
    clipping a tower or refusing to cross a hedge:

      blocked - taller than the camera will climb over. Genuinely impassable,
                dilated by the body radius so the path keeps its distance.
      cost    - everything else. Cheap over open ground, dearer near tall
                things, so a route that has a choice takes the open one.
    """
    g = ROUTE_GRID
    fy = bake.h // g
    fx = bake.w // g

    # Max-pool rather than average. Averaging a 5x5 m cell hides a lamppost, and
    # the whole point of the layer is that the lamppost is there.
    o = bake.obstacle[:g * fy, :g * fx].reshape(g, fy, g, fx).max(axis=(1, 3))

    cell_m = (bake.wx_max - bake.wx_min) / g
    blocked = o > FLIGHT_BLOCK_H

    # Keep clear of hard obstacles by the body radius.
    pad = max(1, int(round(BODY_RADIUS / cell_m)))
    blocked = ndimage.binary_dilation(blocked, iterations=pad)

    # Stay off the very edge of the map - there is nothing to look at out there
    # and the bake has no data beyond it.
    blocked[:2, :] = blocked[-2:, :] = True
    blocked[:, :2] = blocked[:, -2:] = True

    # Distance to the nearest hard obstacle, in cells. Routing downhill on the
    # reciprocal of this keeps the path off walls without forbidding a squeeze.
    dist = ndimage.distance_transform_edt(~blocked)

    cost = 1.0
    cost = cost + 0.06 * np.minimum(o, CLIMB_LIMIT)      # prefer open ground
    cost = cost + 14.0 / (dist + 1.5)                    # prefer elbow room
    # Cheap where there is room to fly, so the course threads the open gaps a
    # low navigator can actually use rather than skimming every wall.
    cost = cost + 26.0 / (dist + 1.0)
    cost[blocked] = np.inf

    return o, blocked, cost, cell_m


def dijkstra(cost, start, goal):
    """Shortest path over an 8-connected cost grid. Small enough at 256^2 that a
    plain heap beats anything cleverer."""
    g = cost.shape[0]
    INF = float("inf")
    dist = np.full(cost.shape, INF)
    prev = np.full(cost.shape + (2,), -1, dtype=np.int32)
    dist[start] = 0.0
    pq = [(0.0, start[0], start[1])]

    nbr = [(-1, 0, 1.0), (1, 0, 1.0), (0, -1, 1.0), (0, 1, 1.0),
           (-1, -1, 1.4142), (-1, 1, 1.4142), (1, -1, 1.4142), (1, 1, 1.4142)]

    while pq:
        d, r, c = heapq.heappop(pq)
        if d > dist[r, c]:
            continue
        if (r, c) == goal:
            break
        for dr, dc, step in nbr:
            nr, nc = r + dr, c + dc
            if not (0 <= nr < g and 0 <= nc < g):
                continue
            w = cost[nr, nc]
            if not np.isfinite(w):
                continue
            nd = d + step * w
            if nd < dist[nr, nc]:
                dist[nr, nc] = nd
                prev[nr, nc] = (r, c)
                heapq.heappush(pq, (nd, nr, nc))

    if not np.isfinite(dist[goal]):
        return None

    out = []
    cur = goal
    while cur != start:
        out.append(cur)
        p = prev[cur[0], cur[1]]
        cur = (int(p[0]), int(p[1]))
    out.append(start)
    out.reverse()
    return out


def largest_free_region(blocked):
    """The biggest connected patch of flyable space.

    Waypoints have to land in THIS, not merely on a cell that is not blocked.
    Free and reachable are different things: an enclosed courtyard is free, and
    dropping a waypoint in one makes the router report no route between two
    points that both look perfectly fine. At a 6 m standoff Abbey has 523
    disconnected free regions and only one of them is the map.
    """
    free = ~blocked
    lab, n = ndimage.label(free)
    if n <= 1:
        return free
    sizes = ndimage.sum(free, lab, range(1, n + 1))
    return lab == (1 + int(np.argmax(sizes)))


def nearest_free(ok, rc):
    """Nudge a waypoint onto the closest cell in the reachable region."""
    if ok[rc]:
        return rc
    free = np.argwhere(ok)
    d = (free[:, 0] - rc[0]) ** 2 + (free[:, 1] - rc[1]) ** 2
    r, c = free[int(np.argmin(d))]
    return (int(r), int(c))


def pick_waypoints(coarse, blocked, g, cell_m, reachable=None):
    """Control points on an orbit whose RADIUS follows the map's content.

    A fixed ring gave an eight-pointed star that never went near the monastery -
    the shape came from the ring, and the routing only scalloped it. So instead:
    find where the built-up mass actually is, and in each direction hold station
    just outside the last of it.

    Radial by construction, so the loop cannot cross itself, and because the
    radius tracks the content the result reads as a circuit OF something rather
    than a circle drawn on top of it."""
    # Interest is CONTENT - buildings and trees, the things worth orbiting.
    #
    # Anything past the climb limit is deliberately excluded rather than capped.
    # Capping was the first attempt and it collapsed the whole loop into the
    # north-east corner: the outland cliff is enormous, so even flattened to
    # 55 m its sheer area outweighed the entire monastery and dragged the
    # centroid onto it. A wall is not a subject.
    content = np.where((coarse > 1.0) & (coarse <= CLIMB_LIMIT), coarse, 0.0)

    # Outland decoration rings the map edge and is not part of the playable
    # space. Drop a border before it can vote.
    m = max(1, int(round(0.12 * g)))
    content[:m, :] = 0.0
    content[-m:, :] = 0.0
    content[:, :m] = 0.0
    content[:, -m:] = 0.0

    # Blur until individual buildings merge into the districts they belong to.
    interest = ndimage.gaussian_filter(content, sigma=max(1.0, 55.0 / cell_m))
    thresh = max(0.35, 0.22 * float(interest.max()))

    # Orbit about the centre of that mass, not the centre of the map - the two
    # are rarely the same and the difference is what stops the loop sitting
    # lopsided over the interesting half.
    ys, xs = np.nonzero(interest > thresh)
    if len(ys) == 0:
        cr = cc = (g - 1) / 2.0
    else:
        wts = interest[ys, xs]
        cr = float((ys * wts).sum() / wts.sum())
        cc = float((xs * wts).sum() / wts.sum())

    r_min = R_MIN_FRAC * (g - 1) / 2.0
    r_max = R_MAX_FRAC * (g - 1) / 2.0
    standoff = STANDOFF / cell_m

    # March out along each spoke and take the radius holding MASS_FRAC of the
    # content mass on it, rather than the outermost hit.
    #
    # The outermost hit was the second attempt and it orbited outside
    # everything: one isolated tree line 400 m out pushed the whole spoke that
    # far, and the loop ended up so conservative that it would have flown
    # identically with no collision data at all. A percentile follows the bulk
    # of the content and lets sparse outliers be flown around instead - which is
    # what the obstacle layer is for.
    angles = np.linspace(0.0, 2.0 * np.pi, N_WAYPOINTS, endpoint=False)
    radii = np.empty(N_WAYPOINTS)
    for i, a in enumerate(angles):
        dr, dc = -np.cos(a), np.sin(a)
        ts, mass = [], []
        t = 0.0
        while t < r_max:
            r = int(round(cr + dr * t))
            c = int(round(cc + dc * t))
            if not (0 <= r < g and 0 <= c < g):
                break
            ts.append(t)
            mass.append(max(0.0, interest[r, c] - thresh))
            t += 1.0

        total = float(np.sum(mass))
        if total <= 0.0:
            core = r_min
        else:
            cum = np.cumsum(mass) / total
            core = float(ts[int(np.searchsorted(cum, MASS_FRAC))])
        radii[i] = np.clip(core + standoff, r_min, r_max)

    # Blur the radii around the circle so the orbit breathes instead of
    # stepping. Periodic - wrap, filter, unwrap.
    k = 3
    pad = np.concatenate([radii[-k:], radii, radii[:k]])
    ker = np.hanning(2 * k + 1)
    ker /= ker.sum()
    radii = np.convolve(pad, ker, mode="same")[k:-k]

    ok = reachable if reachable is not None else ~blocked
    pts = []
    for a, rad in zip(angles, radii):
        r = int(round(np.clip(cr - rad * np.cos(a), 0, g - 1)))
        c = int(round(np.clip(cc + rad * np.sin(a), 0, g - 1)))
        pts.append(nearest_free(ok, (r, c)))
    return pts


def route_loop(cost, blocked, waypoints):
    """Route waypoint to waypoint and close the circuit."""
    path = []
    n = len(waypoints)
    for i in range(n):
        a = waypoints[i]
        b = waypoints[(i + 1) % n]
        leg = dijkstra(cost, a, b)
        if leg is None:
            raise RuntimeError(f"no route from {a} to {b}")
        path.extend(leg[:-1])       # drop the duplicated joint
    return path


# ---------------------------------------------------------------------------
# Smoothing and altitude
# ---------------------------------------------------------------------------

def smooth_closed(xs, zs, step_m, smooth):
    """Fit a periodic spline through the routed polyline and resample it at a
    fixed ARC LENGTH.

    Fixed arc length rather than fixed parameter: sampling in t makes the camera
    slow through tightly-spaced control points and sprint through open ones,
    which reads as the flight hesitating."""
    tck, _ = splprep([np.asarray(xs), np.asarray(zs)], s=smooth, per=True)

    # Walk the curve densely once to measure it, then pick parameters that land
    # on equal distances.
    u = np.linspace(0.0, 1.0, 4000)
    px, pz = splev(u, tck)
    seg = np.hypot(np.diff(px), np.diff(pz))
    s = np.concatenate([[0.0], np.cumsum(seg)])
    total = s[-1]

    n = max(16, int(round(total / step_m)))
    want = np.linspace(0.0, total, n, endpoint=False)
    uu = np.interp(want, s, u)

    x, z = splev(uu, tck)
    dx, dz = splev(uu, tck, der=1)
    return np.asarray(x), np.asarray(z), np.asarray(dx), np.asarray(dz), total


def plan_altitude(bake, x, z, dx, dz):
    """Altitude for each sample: clear the corridor, and never get closer to the
    ground than AGL_MIN.

    The corridor matters. Taking the height only at the path's own texel lets a
    wing clip a roof the centreline missed, so this takes the worst top height
    across a band either side of the direction of travel."""
    n = len(x)
    ln = np.hypot(dx, dz)
    ln[ln == 0.0] = 1.0
    nx, nz = -dz / ln, dx / ln           # unit normal, left of travel

    offs = np.linspace(-CORRIDOR, CORRIDOR, 9)
    tops = np.empty((len(offs), n))
    floors = np.empty((len(offs), n))
    for i, o in enumerate(offs):
        for j in range(n):
            sx = x[j] + nx[j] * o
            sz = z[j] + nz[j] * o
            tops[i, j] = bake.sample(bake.top, sx, sz)
            floors[i, j] = bake.sample(bake.floor, sx, sz)

    need = np.maximum(tops.max(axis=0) + CLEARANCE,
                      floors.max(axis=0) + AGL_MIN)

    # A running maximum forward and backward, so the climb STARTS before the
    # obstacle instead of at it, then a periodic blur to take the corners off.
    # Without the lead-in the camera pitches up into a wall it is already on.
    lead = max(1, int(round(60.0 / SAMPLE_STEP)))
    rolled = np.stack([np.roll(need, k) for k in range(-lead, lead + 1)])
    need = rolled.max(axis=0)

    k = max(3, int(round(45.0 / SAMPLE_STEP)) | 1)
    ker = np.hanning(k)
    ker /= ker.sum()
    pad = np.concatenate([need[-k:], need, need[:k]])
    y = np.convolve(pad, ker, mode="same")[k:-k]

    return y


def verify(bake, x, y, z):
    """Fly it and see. Reports the worst clearance and how often it is short."""
    worst = 1e9
    worst_at = None
    short = 0
    for j in range(len(x)):
        t = bake.sample(bake.top, x[j], z[j])
        c = y[j] - t
        if c < worst:
            worst, worst_at = c, (x[j], z[j])
        if c < CLEARANCE * 0.5:
            short += 1
    return worst, worst_at, short


# ---------------------------------------------------------------------------
# Picture
# ---------------------------------------------------------------------------

PINK = (255, 46, 168)
PINK_HI = (255, 140, 205)


def draw_plan(bake, x, y, z, waypoints_world, path_png):
    """The collision map with the route on it.

    The base is shaded by obstacle HEIGHT rather than the binary mask, because a
    route that skims a 40 m tower and a route that skims a hedge look identical
    in binary and are not the same route at all."""
    o = bake.obstacle
    shade = np.clip(o / CLIMB_LIMIT, 0.0, 1.0)
    img = np.zeros((bake.h, bake.w, 3), dtype=np.uint8)

    # ground: a dark relief so the terrain is readable under the obstacles
    g = bake.floor
    gn = (g - g.min()) / max(1e-6, (g.max() - g.min()))
    img[..., 0] = (18 + 26 * gn).astype(np.uint8)
    img[..., 1] = (22 + 32 * gn).astype(np.uint8)
    img[..., 2] = (30 + 40 * gn).astype(np.uint8)

    hit = o > 1.0
    img[hit, 0] = (70 + 185 * shade[hit]).astype(np.uint8)
    img[hit, 1] = (74 + 181 * shade[hit]).astype(np.uint8)
    img[hit, 2] = (78 + 177 * shade[hit]).astype(np.uint8)

    # anything the camera will not climb gets a warning tint, so the reason for
    # a detour is visible in the same picture as the detour
    hard = o > CLIMB_LIMIT
    img[hard] = (255, 200, 60)

    im = Image.fromarray(img, "RGB")
    d = ImageDraw.Draw(im)

    pts = []
    for j in range(len(x)):
        c, r = bake.texel_of(x[j], z[j])
        pts.append((c, r))
    pts.append(pts[0])

    d.line(pts, fill=(90, 0, 60), width=7, joint="curve")
    d.line(pts, fill=PINK, width=3, joint="curve")

    for wx, wz in waypoints_world:
        c, r = bake.texel_of(wx, wz)
        d.ellipse([c - 6, r - 6, c + 6, r + 6], fill=PINK_HI, outline=(255, 255, 255))

    c, r = bake.texel_of(x[0], z[0])
    d.ellipse([c - 9, r - 9, c + 9, r + 9], fill=(80, 255, 130), outline=(255, 255, 255))

    # heading tick at the start, so "which way round" is not a guess
    hx, hz = x[1] - x[0], z[1] - z[0]
    hl = np.hypot(hx, hz) or 1.0
    c2, r2 = bake.texel_of(x[0] + hx / hl * 55.0, z[0] + hz / hl * 55.0)
    d.line([(c, r), (c2, r2)], fill=(80, 255, 130), width=3)

    im.save(path_png)
    return im


def draw_profile(bake, x, y, z, path_png):
    """Altitude against distance, with the ground and the obstacle tops under
    it. This is where a plan that looks fine from above shows itself flying
    through a roof."""
    n = len(x)
    W, H = 1024, 260
    img = Image.new("RGB", (W, H), (16, 18, 26))
    d = ImageDraw.Draw(img)

    ground = np.array([bake.sample(bake.floor, x[j], z[j]) for j in range(n)])
    tops = np.array([bake.sample(bake.top, x[j], z[j]) for j in range(n)])

    lo = min(ground.min(), y.min()) - 10.0
    hi = max(y.max(), tops.max()) + 10.0

    def to_px(j, v):
        return (j * (W - 1) / (n - 1), H - 1 - (v - lo) / (hi - lo) * (H - 20) - 10)

    d.polygon([to_px(j, tops[j]) for j in range(n)] + [(W - 1, H - 1), (0, H - 1)],
              fill=(70, 74, 80))
    d.polygon([to_px(j, ground[j]) for j in range(n)] + [(W - 1, H - 1), (0, H - 1)],
              fill=(38, 44, 54))
    d.line([to_px(j, y[j]) for j in range(n)], fill=PINK, width=3)

    d.text((8, 6), f"altitude  {y.min():.0f} .. {y.max():.0f} m", fill=(200, 200, 210))
    img.save(path_png)
    return img


# ---------------------------------------------------------------------------

def main():
    folder = os.path.join(os.environ.get("TEMP", "."), "nuTerra", "flight")
    map_name = sys.argv[1] if len(sys.argv) > 1 else "19_monastery"

    print("bake")
    bake = Bake(folder, map_name)
    bake.describe()

    print("route")
    coarse, blocked, cost, cell_m = build_cost(bake)
    g = ROUTE_GRID
    print(f"  routing on {g}x{g} ({cell_m:.1f} m per cell), "
          f"{100.0 * blocked.mean():.1f}% blocked at the {CLIMB_LIMIT:.0f} m climb limit")

    reach = largest_free_region(blocked)
    print(f"  reachable space: {100.0 * reach.sum() / max(1, (~blocked).sum()):.1f}% "
          f"of the free cells are in one connected region")
    wps = pick_waypoints(coarse, blocked, g, cell_m, reachable=reach)
    cells = route_loop(cost, blocked, wps)

    # coarse cell -> world, through the same mapping the bake documented
    fy = bake.h // g
    fx = bake.w // g
    xs = [bake.world_of((c + 0.5) * fx, (r + 0.5) * fy)[0] for r, c in cells]
    zs = [bake.world_of((c + 0.5) * fx, (r + 0.5) * fy)[1] for r, c in cells]
    print(f"  {len(cells)} cells through {len(wps)} waypoints")

    print("smooth")
    smooth = float(len(xs)) * 8.0
    x, z, dx, dz, total = smooth_closed(xs, zs, SAMPLE_STEP, smooth)
    print(f"  {len(x)} samples over {total:.0f} m at {SAMPLE_STEP:.0f} m")

    print("altitude")
    y = plan_altitude(bake, x, z, dx, dz)
    worst, worst_at, short = verify(bake, x, y, z)
    print(f"  {y.min():.0f} .. {y.max():.0f} m")
    print(f"  worst clearance {worst:.1f} m at ({worst_at[0]:.0f}, {worst_at[1]:.0f}), "
          f"{short} of {len(x)} samples under half clearance")

    heading = np.arctan2(dx, dz)

    wps_world = []
    for r, c in wps:
        wx, wz = bake.world_of((c + 0.5) * fx, (r + 0.5) * fy)
        wps_world.append((wx, wz))

    png = os.path.join(folder, map_name + "_plan.png")
    prof = os.path.join(folder, map_name + "_profile.png")
    csv_path = os.path.join(folder, map_name + "_plan.csv")

    draw_plan(bake, x, y, z, wps_world, png)
    draw_profile(bake, x, y, z, prof)

    with open(csv_path, "w", newline="") as f:
        w = csv.writer(f)
        w.writerow(["i", "s_m", "x", "y", "z", "heading_rad"])
        for j in range(len(x)):
            w.writerow([j, round(j * SAMPLE_STEP, 2),
                        round(float(x[j]), 3), round(float(y[j]), 3), round(float(z[j]), 3),
                        round(float(heading[j]), 5)])

    print("wrote")
    for p in (png, prof, csv_path):
        print("  " + p)


if __name__ == "__main__":
    main()
