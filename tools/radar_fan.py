"""
radar_fan.py - low-level camera flight around Abbey (19_monastery) at 5 m AGL.

The nominal course in 19_monastery_plan.csv was planned at 26-49 m altitude and
only avoided things taller than 55 m. Flown at 5 m it drives straight through the
monastery walls and the tree lines. This script keeps that course as an INTENT
only, and flies it with a radar fan: cast rays over a spread of bearings, measure
how far each one gets before it hits something the camera cannot climb over, and
steer on the histogram of those ranges.

Two rules earn their keep:

  the clearance rule - an obstacle blocks only if it is taller than the camera can
      clear (AGL - MARGIN = 3.5 m). Fences, low walls and rubble are FLOWN OVER.
      Buildings and tree lines are not.

  the trap rule - a bearing is accepted only if the position TWO points along it
      is also clear, not just the next one. A ray that gets into a gap but cannot
      get out of it is a courtyard, an alcove or a dead end, and testing only the
      near point flies the camera into pockets it then has to reverse out of.

Run:  python radar_fan.py
Writes radar_fan.png beside this script.
"""

import csv
import math
import os

import numpy as np
from scipy import ndimage
from PIL import Image, ImageDraw

# ---------------------------------------------------------------------------
# Flight envelope
# ---------------------------------------------------------------------------

AGL = 5.0            # metres above bare terrain the camera rides
MARGIN = 1.5         # headroom kept over an obstacle it chooses to overfly
BLOCK_H = AGL - MARGIN   # 3.5 m - taller than this must be flown AROUND
BODY_R = 3.0         # horizontal half-width kept clear of a blocking cell

STEP = 2.0           # metres per navigation step
MAX_TURN = math.radians(18.0)   # heading change allowed per step (6.4 m turn radius)
N_BEARINGS = 61      # candidate bearings in the fan
FAN_HALF = math.radians(90.0)   # +/- this around the current heading
RAY_MAX = 70.0       # radar range, metres
RAY_STEP = 0.6       # ray march increment, under one texel

SAFE_MIN = 4.5       # a bearing whose range is under this cannot be stepped at all
NEAR_PT = 8.0        # first of the two lookahead points
FAR_PT = 30.0        # second - the trap test

CAPTURE = 10.0       # metres: nominal point counts as reached
PASS_R = 32.0        # metres: nominal point counts as passed if we are beyond it
AIM_LEAD = 3         # aim this many nominal samples past the current target
STUCK_STEPS = 90     # give up on an unreachable nominal point after this many

W_RANGE = 1.0        # score: openness
W_ALIGN = 2.0        # score: pointing at the course
W_TURN = 0.8         # score: not snapping the heading around
W_VISIT = 0.7        # score: not going back over ground already covered
VISIT_CELL = 10.0    # metres per breadcrumb cell
RETREAT_M = 24.0     # metres backed out of a dead end before trying again

MAX_STEPS = 6000

FOLDER = os.path.join(os.environ.get("TEMP", "."), "nuTerra", "flight")
MAP = "19_monastery"
HERE = os.path.dirname(os.path.abspath(__file__))
PNG = os.path.join(HERE, "radar_fan.png")


def wrap(a):
    return (a + math.pi) % (2.0 * math.pi) - math.pi


# ---------------------------------------------------------------------------
# The bake (world <-> texel mapping straight off the header, as documented)
# ---------------------------------------------------------------------------

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

        # What the camera cannot climb over at 5 m AGL. The terrain itself never
        # appears here - on bare ground top == floor - which is right: the camera
        # rides 5 m over whatever the ground does.
        self.blocked = self.obstacle > BLOCK_H
        self.blocked[:2, :] = self.blocked[-2:, :] = True
        self.blocked[:, :2] = self.blocked[:, -2:] = True

        # The mask the navigator plans against: the same thing grown by the body
        # radius, so the flown path keeps its distance instead of shaving corners.
        pad = max(1, int(round(BODY_R / self.mx)))
        self.plan_blocked = ndimage.binary_dilation(self.blocked, iterations=pad)

        # Distance in metres from every cell to the nearest genuinely blocked one.
        self.clear_dist = ndimage.distance_transform_edt(~self.blocked) * self.mx

    def _r32(self, path):
        with open(path, "rb") as f:
            raw = f.read()
        return np.frombuffer(raw, dtype="<f4").astype(np.float64).reshape(self.h, self.w).copy()

    def texel_of(self, x, z):
        return ((x - self.wx_min) / self.mx - 0.5,
                (self.wz_max - z) / self.mz - 0.5)

    def idx(self, x, z):
        """Vectorised nearest-texel index, clamped."""
        c = np.clip(np.rint((np.asarray(x) - self.wx_min) / self.mx - 0.5), 0, self.w - 1).astype(np.int32)
        r = np.clip(np.rint((self.wz_max - np.asarray(z)) / self.mz - 0.5), 0, self.h - 1).astype(np.int32)
        return r, c

    def sample(self, field, x, z):
        r, c = self.idx(x, z)
        return field[r, c]


# ---------------------------------------------------------------------------
# The radar
# ---------------------------------------------------------------------------

RAY_D = np.arange(RAY_STEP, RAY_MAX + RAY_STEP, RAY_STEP)


def radar(bake, x, z, bearings):
    """Cast a ray on every bearing and return the range at which each one first
    meets a blocking cell. Ranges are RAY_MAX where the ray runs clean to the end
    of its reach.

    Marched at 0.6 m, comfortably under one 1.37 m texel, so no cell can be
    stepped over even on a diagonal - which is the whole failure mode of a coarse
    ray and would let the navigator drive through the corner of a building."""
    sx = np.sin(bearings)[:, None]
    cz = np.cos(bearings)[:, None]
    px = x + sx * RAY_D[None, :]
    pz = z + cz * RAY_D[None, :]
    r, c = bake.idx(px, pz)
    hit = bake.plan_blocked[r, c]                 # (nb, ns)
    any_hit = hit.any(axis=1)
    first = np.argmax(hit, axis=1)
    rng = np.where(any_hit, RAY_D[first] - RAY_STEP, RAY_MAX)
    return np.maximum(rng, 0.0)


def point_clear(bake, x, z):
    r, c = bake.idx(x, z)
    return not bool(bake.plan_blocked[r, c])


def points_clear(bake, x, z):
    r, c = bake.idx(x, z)
    return ~bake.plan_blocked[r, c]


# ---------------------------------------------------------------------------
# The flight
# ---------------------------------------------------------------------------

def retreat(path, heads, visits, vkey, events, stat):
    """Back out of a dead end along ground already flown.

    Reversing down our own track is the only manoeuvre guaranteed not to clip:
    every point on it was clear when we flew it. On the way out the abandoned
    cells get their visit count spiked, so the openness score will not simply
    turn round and fly straight back in."""
    stat["retreats"] += 1
    if path:
        events.append((path[-1].copy(), "boxed"))
    back = 0.0
    while len(path) > 2 and back < RETREAT_M:
        p = path.pop()
        heads.pop()
        k = vkey(p[0], p[1])
        visits[k] = visits.get(k, 0) + 4
        back += float(np.hypot(*(p - path[-1])))
    if len(path) < 2:
        return False
    d = path[-1] - path[-2]
    heads[-1] = math.atan2(d[0], d[1])
    return True


def fly(bake, nom_x, nom_z, nom_tan, trap_rule=True, record_fans=False):
    """Fly the nominal course on the radar.

    The decision each step is a CASCADE. The strict filter is tried first and the
    navigator only loosens its standards when nothing survives it, one notch at a
    time and only for that step. That ordering is what stops one tight spot
    throwing the rule away everywhere else."""
    n = len(nom_x)
    nom = np.stack([nom_x, nom_z], axis=1)

    start = nom[0].copy()
    if not point_clear(bake, start[0], start[1]):
        free = np.argwhere(~bake.plan_blocked)
        cr, cc = bake.idx(start[0], start[1])
        d = (free[:, 0] - cr) ** 2 + (free[:, 1] - cc) ** 2
        r, c = free[int(np.argmin(d))]
        start = np.array([bake.wx_min + (c + 0.5) * bake.mx,
                          bake.wz_max - (r + 0.5) * bake.mz])

    pos = start.copy()
    heading = math.atan2(nom_x[1] - nom_x[0], nom_z[1] - nom_z[0])

    # Acceptance levels: (half-width of the fan, far-point distance, 0 = no far
    # test). The trap rule IS the non-zero far point.
    if trap_rule:
        levels = [(FAN_HALF, FAR_PT), (FAN_HALF, 18.0),
                  (math.pi, FAR_PT), (math.pi, 18.0), (math.pi, 0.0)]
    else:
        levels = [(FAN_HALF, 0.0), (math.pi, 0.0)]

    pi = 0
    pi_age = 0
    path = [pos.copy()]
    heads = [heading]
    fans = []
    events = []
    turn_sign = 0
    turn_hold = 0
    pivots = 0
    visits = {}

    stat = dict(pocket_steps=0, trap_entries=0, retreats=0, pivots=0,
                wide_fan=0, relaxed=0, in_pocket=False, skips=0)

    def vkey(x, z):
        return (int(math.floor(x / VISIT_CELL)), int(math.floor(z / VISIT_CELL)))

    visits[vkey(pos[0], pos[1])] = 1

    steps = 0
    done = False
    while steps < MAX_STEPS:
        steps += 1

        # --- progress along the nominal course ------------------------------
        # Monotonic, and it needs both tests: CAPTURE catches a point flown
        # straight through, the projection test catches one the detour swung
        # wide of. Without the second the camera drifts past a waypoint it never
        # got within 10 m of and then turns back for it.
        moved = False
        for k in range(pi, min(pi + 40, n)):
            if np.hypot(*(pos - nom[k])) < CAPTURE:
                pi = k + 1
                moved = True
        while pi < n:
            v = pos - nom[pi]
            if float(v @ nom_tan[pi]) > 0.0 and np.hypot(*v) < PASS_R:
                pi += 1
                moved = True
            else:
                break
        pi_age = 0 if moved else pi_age + 1
        if pi_age > STUCK_STEPS and pi < n:
            pi += 1
            pi_age = 0
            stat["skips"] += 1
            events.append((pos.copy(), "skip"))

        if pi >= n:
            if np.hypot(*(pos - start)) < 6.0 and steps > 100:
                done = True
                break
            aim = start
        else:
            # Aim at the course, but never at a nominal sample buried inside a
            # building - chasing one of those is what walks the camera into a
            # courtyard it then has to reverse out of.
            k = min(pi + AIM_LEAD, n - 1)
            while k < n and not point_clear(bake, nom[k][0], nom[k][1]):
                k += 1
            aim = nom[k] if k < n else start

        to_aim = aim - pos
        aim_b = math.atan2(to_aim[0], to_aim[1])

        # --- radar -----------------------------------------------------------
        rel_fan = np.linspace(-FAN_HALF, FAN_HALF, N_BEARINGS)
        bearings = heading + rel_fan
        rng = radar(bake, pos[0], pos[1], bearings)

        if record_fans:
            fans.append((pos.copy(), bearings.copy(), rng.copy()))

        # in a pocket: nothing within +/-45 deg of the nose reaches even 18 m
        pocket = bool(rng[np.abs(rel_fan) < math.radians(45)].max() < 18.0)
        if pocket:
            stat["pocket_steps"] += 1
            if not stat["in_pocket"]:
                stat["trap_entries"] += 1
                events.append((pos.copy(), "pocket"))
                events.append((pos.copy(), "pocket"))
        stat["in_pocket"] = pocket

        # --- acceptance cascade ----------------------------------------------
        chosen = None
        for li, (half, far) in enumerate(levels):
            if half > FAN_HALF + 1e-6:
                bs = heading + np.linspace(-math.pi, math.pi, 121)
                rs = radar(bake, pos[0], pos[1], bs)
            else:
                bs, rs = bearings, rng

            ok = rs >= SAFE_MIN
            ok &= points_clear(bake, pos[0] + np.sin(bs) * NEAR_PT,
                               pos[1] + np.cos(bs) * NEAR_PT)
            if far > 0.0:
                # THE TRAP RULE. Two points ahead, not one: a bearing whose NEAR
                # point is clear but whose FAR point is not has got into a gap
                # that does not go anywhere - an alcove, a courtyard, the slot
                # between two buildings. There is no room there whatever the
                # near point said.
                ok &= points_clear(bake, pos[0] + np.sin(bs) * far,
                                   pos[1] + np.cos(bs) * far)
            if ok.any():
                if li > 0:
                    stat["relaxed"] += 1
                if half > FAN_HALF + 1e-6:
                    stat["wide_fan"] += 1
                chosen = (bs, rs, ok)
                break

        if chosen is None:
            if not retreat(path, heads, visits, vkey, events, stat):
                break
            pos = path[-1].copy()
            heading = heads[-1]
            pivots = 0
            continue

        bs, rs, ok = chosen

        # --- score -------------------------------------------------------------
        rel = np.array([wrap(b - heading) for b in bs])
        d_aim = np.array([wrap(b - aim_b) for b in bs])
        range_term = np.minimum(rs, RAY_MAX) / RAY_MAX
        align_term = 0.5 * (np.cos(d_aim) + 1.0)
        turn_term = np.abs(rel) / math.pi

        px = pos[0] + np.sin(bs) * FAR_PT
        pz = pos[1] + np.cos(bs) * FAR_PT
        vis = np.array([min(visits.get(vkey(a, b), 0), 5) / 5.0 for a, b in zip(px, pz)])

        # Closing the loop is the one time flying back over old ground is the
        # whole objective, so the breadcrumb penalty comes off - it was pushing
        # the camera away from its own start point.
        wv = 0.0 if pi >= n else W_VISIT

        score = (W_RANGE * range_term + W_ALIGN * align_term
                 - W_TURN * turn_term - wv * vis)

        # Hysteresis. Two mirror-image bearings either side of a symmetric gap
        # score identically, and without this the camera alternates between them
        # and crabs straight at the gap edge. Committing to a turn direction for
        # a few steps breaks the tie the same way each time.
        if turn_hold > 0 and turn_sign != 0:
            score = score + 0.25 * np.clip(rel * turn_sign, -1.0, 1.0)

        score = np.where(ok, score, -1e9)
        best = int(np.argmax(score))
        want = bs[best]

        # --- rate limit and step -----------------------------------------------
        dh = wrap(want - heading)
        lim = max(-MAX_TURN, min(MAX_TURN, dh))
        if abs(lim) > math.radians(4.0):
            sgn = 1 if lim > 0 else -1
            turn_sign, turn_hold = sgn, 8
        else:
            turn_hold = max(0, turn_hold - 1)

        new_head = wrap(heading + lim)
        nxt = pos + np.array([math.sin(new_head), math.cos(new_head)]) * STEP

        if not point_clear(bake, nxt[0], nxt[1]):
            # The bearing wanted is fine, the rate limit just cannot get the nose
            # there this step. Turn on the spot rather than fly the clipped
            # heading into the wall - a camera is allowed to pivot.
            heading = new_head
            heads[-1] = heading
            pivots += 1
            stat["pivots"] += 1
            if pivots > 40:
                if not retreat(path, heads, visits, vkey, events, stat):
                    break
                pos = path[-1].copy()
                heading = heads[-1]
                pivots = 0
            continue

        pivots = 0
        heading = new_head
        pos = nxt
        path.append(pos.copy())
        heads.append(heading)
        k = vkey(pos[0], pos[1])
        visits[k] = visits.get(k, 0) + 1

    return dict(path=np.array(path), heads=np.array(heads), fans=fans,
                events=events, steps=steps, done=done, start=start, pi=pi,
                stat=stat)


# ---------------------------------------------------------------------------
# Measurement
# ---------------------------------------------------------------------------

def measure(bake, res, nom_x, nom_z):
    p = res["path"]

    # densify to 0.4 m: a clip cannot be allowed to hide between two 2 m samples
    dx = np.diff(p[:, 0])
    dz = np.diff(p[:, 1])
    seg = np.hypot(dx, dz)
    s = np.concatenate([[0.0], np.cumsum(seg)])
    total = float(s[-1])
    ss = np.arange(0.0, total, 0.4)
    fx = np.interp(ss, s, p[:, 0])
    fz = np.interp(ss, s, p[:, 1])

    r, c = bake.idx(fx, fz)
    clips = int(bake.blocked[r, c].sum())
    clearance = float(bake.clear_dist[r, c].min())

    # the physical test as well: is the top surface at the camera's own cell
    # actually above the camera?
    alt = bake.floor[r, c] + AGL
    hard_clips = int((bake.top[r, c] > alt).sum())

    nom = np.stack([nom_x, nom_z], axis=1)
    d = np.sqrt(((fx[:, None] - nom[None, :, 0]) ** 2 +
                 (fz[:, None] - nom[None, :, 1]) ** 2)).min(axis=1)
    return dict(length=total, clips=clips, hard_clips=hard_clips,
                clearance=clearance, dev=d, max_dev=float(d.max()),
                fx=fx, fz=fz)


# ---------------------------------------------------------------------------
# Picture
# ---------------------------------------------------------------------------

PINK = (255, 46, 168)


def draw(bake, res, m, nom_x, nom_z, out):
    o = bake.obstacle
    img = np.zeros((bake.h, bake.w, 3), dtype=np.uint8)

    g = bake.floor
    gn = (g - g.min()) / max(1e-6, (g.max() - g.min()))
    img[..., 0] = (16 + 24 * gn).astype(np.uint8)
    img[..., 1] = (20 + 30 * gn).astype(np.uint8)
    img[..., 2] = (28 + 38 * gn).astype(np.uint8)

    # grey: something is there, but it is under 3.5 m and gets FLOWN OVER
    low = (o > 0.5) & (o <= BLOCK_H)
    sh = np.clip(o[low] / BLOCK_H, 0, 1)
    img[low, 0] = (58 + 45 * sh).astype(np.uint8)
    img[low, 1] = (62 + 45 * sh).astype(np.uint8)
    img[low, 2] = (68 + 45 * sh).astype(np.uint8)

    # amber: taller than the camera can clear - must be flown AROUND
    hard = bake.blocked
    hs = np.clip(o[hard] / 25.0, 0.15, 1.0)
    img[hard, 0] = (120 + 135 * hs).astype(np.uint8)
    img[hard, 1] = (86 + 114 * hs).astype(np.uint8)
    img[hard, 2] = (26 + 34 * hs).astype(np.uint8)

    im = Image.fromarray(img, "RGB").convert("RGBA")

    # --- radar transversals on their own layer so they stay translucent ------
    fanlay = Image.new("RGBA", im.size, (0, 0, 0, 0))
    fd = ImageDraw.Draw(fanlay)
    for pos, bs, rs in res["fans"]:
        c0, r0 = bake.texel_of(pos[0], pos[1])
        for b, rr in zip(bs[::3], rs[::3]):
            ex = pos[0] + math.sin(b) * rr
            ez = pos[1] + math.cos(b) * rr
            c1, r1 = bake.texel_of(ex, ez)
            hitting = rr < RAY_MAX - 1e-6
            col = (255, 120, 90, 105) if hitting else (90, 225, 235, 60)
            fd.line([(c0, r0), (c1, r1)], fill=col, width=1)
        fd.ellipse([c0 - 2, r0 - 2, c0 + 2, r0 + 2], fill=(150, 240, 250, 150))
    im = Image.alpha_composite(im, fanlay)
    d = ImageDraw.Draw(im)

    # --- nominal course, dim, so deviations read as deviations ---------------
    npts = [bake.texel_of(nom_x[j], nom_z[j]) for j in range(len(nom_x))]
    npts.append(npts[0])
    d.line(npts, fill=(140, 154, 200, 255), width=2, joint="curve")

    # --- flown path -----------------------------------------------------------
    p = res["path"]
    fpts = [bake.texel_of(p[j, 0], p[j, 1]) for j in range(len(p))]
    d.line(fpts, fill=(70, 0, 46, 255), width=6, joint="curve")
    d.line(fpts, fill=PINK + (255,), width=3, joint="curve")

    # --- where it had to leave the course a long way --------------------------
    dev = m["dev"]
    fx, fz = m["fx"], m["fz"]
    runs = []
    cur = []
    for j in range(len(dev)):
        if dev[j] > 18.0:
            cur.append(j)
        elif cur:
            runs.append(cur)
            cur = []
    if cur:
        runs.append(cur)
    for run in runs:
        k = run[int(np.argmax(dev[run]))]
        c, r = bake.texel_of(fx[k], fz[k])
        d.ellipse([c - 14, r - 14, c + 14, r + 14], outline=(255, 190, 70, 255), width=2)
        d.text((c + 17, r + 8), f"detour {dev[k]:.0f} m", fill=(255, 200, 90, 255))

    for pos, kind in res["events"]:
        c, r = bake.texel_of(pos[0], pos[1])
        if kind == "pocket":
            d.line([(c, r - 8), (c + 8, r), (c, r + 8), (c - 8, r), (c, r - 8)],
                   fill=(120, 240, 255, 255), width=2)
            d.text((c - 46, r - 24), "pocket refused", fill=(140, 240, 255, 255))
        elif kind == "boxed":
            d.line([(c - 8, r - 8), (c + 8, r + 8)], fill=(255, 70, 70, 255), width=2)
            d.line([(c - 8, r + 8), (c + 8, r - 8)], fill=(255, 70, 70, 255), width=2)
            d.text((c + 11, r + 4), "boxed in", fill=(255, 110, 110, 255))
        else:
            d.ellipse([c - 6, r - 6, c + 6, r + 6], outline=(150, 200, 255, 255), width=2)

    # --- start marker and heading tick ----------------------------------------
    c, r = bake.texel_of(p[0, 0], p[0, 1])
    d.ellipse([c - 9, r - 9, c + 9, r + 9], fill=(80, 255, 130, 255), outline=(255, 255, 255, 255))
    h0 = res["heads"][0]
    c2, r2 = bake.texel_of(p[0, 0] + math.sin(h0) * 60.0, p[0, 1] + math.cos(h0) * 60.0)
    d.line([(c, r), (c2, r2)], fill=(80, 255, 130, 255), width=3)
    d.text((c - 22, r + 12), "START", fill=(120, 255, 160, 255))
    d.text((c2 + 4, r2 - 14), "initial heading", fill=(120, 255, 160, 255))

    # --- legend ----------------------------------------------------------------
    st = res["stat"]
    d.rectangle([10, 10, 410, 186], fill=(8, 10, 16, 210), outline=(90, 100, 120, 255))
    rows = [
        ((255, 46, 168), f"flown at {AGL:.0f} m AGL   {m['length']:.0f} m   clips {m['clips']}"),
        ((140, 154, 200), "nominal course (planned high, ignores 5 m)"),
        ((90, 225, 235), "radar ray - clear to 70 m"),
        ((255, 120, 90), "radar ray - hit, drawn to its measured range"),
        ((210, 150, 45), f"amber: obstacle > {BLOCK_H:.1f} m - fly AROUND"),
        ((100, 105, 112), f"grey: under {BLOCK_H:.1f} m - fly OVER"),
        ((255, 190, 70), f"detour ring, max {m['max_dev']:.0f} m off course"),
        ((120, 240, 255), f"pocket the two-point rule refused: {st['trap_entries']}"),
        ((255, 70, 70), f"boxed in / reversed out: {st['retreats']}"),
    ]
    for i, (col, txt) in enumerate(rows):
        y = 20 + i * 18
        d.rectangle([20, y + 3, 34, y + 11], fill=col + (255,))
        d.text((42, y), txt, fill=(215, 220, 232, 255))

    im.convert("RGB").save(out)


# ---------------------------------------------------------------------------

def main():
    bake = Bake(FOLDER, MAP)
    print(f"bake {bake.w}x{bake.h}, {bake.mx:.3f} m/texel")
    print(f"  blocked at {BLOCK_H:.1f} m: {100.0 * bake.blocked.mean():.2f}% of the map")
    print(f"  grown by body radius {BODY_R:.1f} m: {100.0 * bake.plan_blocked.mean():.2f}%")

    nx, nz = [], []
    with open(os.path.join(FOLDER, MAP + "_plan.csv")) as f:
        for row in csv.DictReader(f):
            nx.append(float(row["x"]))
            nz.append(float(row["z"]))   # y deliberately ignored
    nx = np.array(nx)
    nz = np.array(nz)
    n = len(nx)

    tan = np.stack([np.roll(nx, -1) - nx, np.roll(nz, -1) - nz], axis=1)
    tan /= np.linalg.norm(tan, axis=1)[:, None]

    r, c = bake.idx(nx, nz)
    nb = int(bake.blocked[r, c].sum())
    nbp = int(bake.plan_blocked[r, c].sum())
    print(f"nominal course: {n} samples; {nb} sit inside something taller than "
          f"{BLOCK_H:.1f} m, {nbp} inside it once grown by the body radius")

    def report(tag, res, m):
        st = res["stat"]
        print(f"  {tag:24s} done={str(res['done']):5s} steps={res['steps']:5d} "
              f"len={m['length']:7.0f} m  clips={m['clips']}")
        print(f"  {'':24s} trap entries={st['trap_entries']:4d}  "
              f"pocket steps={st['pocket_steps']:4d}  reversals={st['retreats']:3d}  "
              f"pivots={st['pivots']:4d}  wide-fan={st['wide_fan']:4d}  "
              f"skipped waypoints={st['skips']}")

    print("\nA/B on the two-point lookahead")
    off = fly(bake, nx, nz, tan, trap_rule=False)
    m_off = measure(bake, off, nx, nz)
    report("trap rule OFF", off, m_off)

    on = fly(bake, nx, nz, tan, trap_rule=True, record_fans=True)
    m_on = measure(bake, on, nx, nz)
    report("trap rule ON", on, m_on)

    print(f"\nflown  {m_on['length']:.0f} m over {on['steps']} steps")
    print(f"  max deviation from nominal {m_on['max_dev']:.1f} m")
    print(f"  min horizontal clearance   {m_on['clearance']:.2f} m")
    print(f"  clips (blocked mask)       {m_on['clips']}")
    print(f"  clips (top above camera)   {m_on['hard_clips']}")
    print(f"  closed the loop            {on['done']}")

    on_draw = dict(on)
    on_draw["fans"] = on["fans"][::18]
    draw(bake, on_draw, m_on, nx, nz, PNG)
    print("wrote " + PNG)

    return on, m_on, off, m_off


if __name__ == "__main__":
    main()
