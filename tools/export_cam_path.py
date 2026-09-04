"""
Export a flown radar route as a .campath for nuTerra.

    python export_cam_path.py [map_name]

Flies the route with radar_commit (the committed-side-detour navigator), turns
it into position + heading + tilt + roll, and writes the binary to
nuTerra/cam_paths/<map>.campath. Also drops a CSV next to it for eyeballing and
a PNG showing where the camera banks.

--------------------------------------------------------------------------
Positions are NOT resmoothed, and that is deliberate
--------------------------------------------------------------------------
The obvious move is to spline the flown path before differentiating it, since a
2 m stepped path gives a steppy tangent. It is also how you fly the camera into
a building: the navigator's path is collision-free, and a spline through it cuts
the corners it deliberately went around.

So the positions written here are EXACTLY the ones the navigator flew and
verified. Only the derived angles are smoothed, which cannot move the camera.
The cost is that the heading can lag the true tangent slightly through a hard
turn, which looks like a camera easing round rather than snapping - i.e. the
thing we wanted anyway. The clip check at the end is on the exported positions,
so if this ever stops being true it fails loudly.
"""

import csv
import math
import os
import sys

import numpy as np
from scipy import ndimage

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import radar_commit as nav
import cam_path
import smooth_path


# --------------------------------------------------------------------------
# Camera behaviour. These are the numbers worth arguing about.
# --------------------------------------------------------------------------

SMOOTH = True        # collision-checked shortcut + Chaikin on the flown path
SMOOTH_REACH = 40    # points a shortcut may span. Bounded, or a loop shortcuts
                     # across its own middle and stops being a loop.
SMOOTH_ITERS = 4     # Chaikin passes. Four is well past visually curved.
SPIKE_DEG = 55.0     # turn sharper than this is a candidate zig-zag spike
CORNER_STANDOFF = 3.0  # metres a rounded CORNER may come to an obstacle, against
                       # the full standoff on the straights
MIN_RADIUS = 12.0    # metres. Reported, not enforced - a corner too tight to
                     # fly is a speed problem, not a geometry one.

CRUISE = 12.0        # metres per second along the path
TILT_LOOKAHEAD = 30.0  # metres. Tilt aims at the path THIS far ahead, rather
                       # than at the local climb rate.
                       #
                       # The climb-rate version was right at 25 m and useless at
                       # 1: measured, it never looked up at all, averaging -0.2
                       # deg on rising ground and -6.7 deg on falling, which at
                       # 1 m altitude is staring at the dirt 8 m in front. Aiming
                       # ahead levels out over a dip, because the point 30 m away
                       # is at roughly your own height even when the ground
                       # between you drops.
TILT_BIAS = 0.0        # degrees on top of that. At 1 m there is nothing to gain
                       # from looking down.

MAX_BANK = 40.0      # degrees. Was 28, and that was the binding limit on the
                     # sharp corners: a 12 m radius turn at 12 m/s asks for
                     # atan(v^2/rg) = 50.7 degrees, so the cap was refusing most
                     # of the bank exactly where it should be most obvious.
                     # Taste, not correctness - drop it if it reads as
                     # aerobatics.
BANK_GAIN = 0.85     # scales the coordinated-turn angle. 1.0 is physically
                     # correct for an aircraft and slightly too much for a
                     # camera, which has no passengers to keep level.

ROLL_FLATTEN = 22.0
ROLL_FLATTEN_FINE = 7.0  # the responsive companion to the above, see below
TIGHT_K = 1.0 / 45.0     # curvature (1/m) at which the fine version fully wins  # metres of smoothing on a copy of the path used ONLY to
                     # work out the bank. Curvature is a second derivative, so
                     # every small wiggle the navigator left behind becomes a
                     # visible twitch in the horizon while barely moving the
                     # camera at all. Flattening only for roll keeps the flown
                     # position honest and the horizon calm.
                     #
                     # 22 m, down from 55. The 55 was tuned against the RAW
                     # reactive path, which was jagged; the path is now
                     # shortcut and Chaikin-smoothed before this runs, so its
                     # curvature is already clean and that much flattening only
                     # stopped the roll keeping up with a sharp turn.

BANK_LEAD = 14.0     # metres. Roll STARTS this far before the curve arrives,
                     # which is what makes it read as flying INTO a turn rather
                     # than reacting to one. This is the "in and out" of the
                     # ask - without it the bank is centred on the corner and
                     # looks like a flinch.
BANK_SMOOTH = 9.0   # metres of smoothing on the bank. Long, because curvature
                     # off a stepped path is noisy and a twitching horizon is
                     # far more obvious than a slightly late roll.

TILT_SMOOTH = 20.0   # metres of smoothing on the tilt
HEAD_SMOOTH = 10.0   # metres of smoothing on the heading. Shorter, because
                     # heading IS the direction of travel and lagging it too
                     # much makes the camera look sideways out of a turn.

ALT_SMOOTH = 9.0     # metres of smoothing on altitude. Short, because at 1 m
                     # the camera is meant to TRACK the ground, not float over
                     # it - the long window that suited a 25 m flight would
                     # leave it 10 m up in a dip.
TERRACE_RAMP = 30.0  # metres over which a terrace step is ramped, so the
                     # camera climbs into it instead of teleporting
ALT_LEAD = 5.0      # metres of running-maximum before smoothing, so a climb
                     # begins ahead of the rise that needs it
MAX_TILT = 16.0      # degrees. The cap exists because tilt is a DERIVATIVE and
                     # derivatives of terrain-following curves have long tails.


def periodic_smooth(a, metres, step_m, closed=True):
    """Hanning smoothing along the path, wrapping at the ends when closed."""
    k = max(1, int(round(metres / step_m)))
    if k % 2 == 0:
        k += 1
    if k < 3:
        return a.copy()
    ker = np.hanning(k)
    ker /= ker.sum()
    if closed:
        pad = np.concatenate([a[-k:], a, a[:k]])
        return np.convolve(pad, ker, mode="same")[k:-k]
    pad = np.concatenate([np.full(k, a[0]), a, np.full(k, a[-1])])
    return np.convolve(pad, ker, mode="same")[k:-k]


def unwrap_heading(h):
    """Continuous heading, so smoothing does not average across the +/-pi seam.

    Averaging 179 and -179 degrees naively gives 0 - pointing exactly backwards
    - and it would happen once per lap on any route that crosses the seam.
    """
    return np.unwrap(h)


def smooth_heading(h, metres, step_m, closed=True):
    """Smooth an ANGLE series along the path.

    Unwrapping alone is not enough on a closed loop, and getting this wrong was
    worth 92 degrees of heading error. A lap turns through a full circle, so the
    unwrapped series ends ~2*pi from where it started - and periodic_smooth pads
    a closed series by wrapping it, which glues that 360 degree step onto the
    seam and lets the kernel smear it into the points either side. It showed up
    as the camera snapping about 90 degrees and back over half a second, once
    per lap, at exactly two points and nowhere else.

    So take the winding out first: subtract the linear ramp that accounts for
    it, which leaves a genuinely periodic series to smooth, then add it back.

    Away from the seam this changes nothing at all - measured at 0.0000 degrees
    over the other 1263 points of the monastery loop.
    """
    u = unwrap_heading(h)
    if not closed:
        return periodic_smooth(u, metres, step_m, False)

    n = len(u)
    # The lap's total turning, INCLUDING the closing step back to the first
    # point - without it the ramp is short by one sample and leaves a step.
    close_step = ((h[0] - h[-1] + math.pi) % (2 * math.pi)) - math.pi
    total = (u[-1] - u[0]) + close_step
    ramp = np.arange(n) * (total / n)
    return periodic_smooth(u - ramp, metres, step_m, True) + ramp


def build(map_name):
    print("fly")
    bake = nav.Bake(nav.FOLDER, map_name)
    cell_m = bake.mx
    nx, nz = nav.load_plan(os.path.join(nav.FOLDER, map_name + "_plan.csv"))

    # The level and the masks come from the navigator, not from a second copy
    # here. They WERE duplicated, and that is exactly how an exported path ends
    # up flown against a different obstacle set from the one that planned it -
    # the two drift apart the first time either is tuned, and nothing complains.
    terrace_of = None
    if nav.TERRACED:
        # Same planner the navigator uses, including its lift search. Calling it
        # rather than repeating it is the whole point - a second copy here is
        # how the exported path ends up flown against different obstacles.
        terrace_of, tlevels, worlds, radar, extra, nx, nz = nav.plan_flight(
            bake, nx, nz, two_point=True)
        nav.FLIGHT_Y = None
        res = nav.fly(bake, radar, nx, nz, two_point=True, record_fans=False,
                      terrace_of=terrace_of)
        raw = worlds[0][2]
        level = None
        print(f"  {len(tlevels)} terraces at {nav.BODY_R:.0f} m standoff"
              + (f" (+{extra:.0f} m lift)" if extra else "")
              + ", Y = " + ", ".join(f"{v:.0f}" for v in tlevels))
    else:
        worlds = None
        level = None
        if nav.LEVEL_FLIGHT:
            level = nav.LEVEL_Y if nav.LEVEL_Y is not None else nav.pick_level(bake, nx, nz)

        res = None
        for _ in range(nav.LEVEL_TRIES):
            nav.FLIGHT_Y = level
            raw, plan, dist_m, pad = nav.build_world(bake, level)
            radar = nav.Radar(bake, plan, raw, cell_m)
            res = nav.fly(bake, radar, nx, nz, two_point=True, record_fans=False)
            if res["closed"] or level is None:
                break
            level += nav.LEVEL_STEP

        if level is not None:
            print(f"  level Y = {level:.1f} m, "
                  f"{100.0 * raw.mean():.2f}% of the map reaches it")

    closed = bool(res["closed"])
    flown = [(float(a), float(b)) for (a, b) in res["path"]]
    print(f"  {len(flown)} points, closed={closed}, "
          f"{res['detours']} detours, {res['reversals']} reversals")

    if SMOOTH:
        # Checked against the SAME dilated mask the navigator flew by, so a
        # shortcut or a cut corner keeps the standoff rather than trading it for
        # smoothness.
        def seg_clear(x0, z0, x1, z1):
            d = math.hypot(x1 - x0, z1 - z0)
            if d < 1e-6:
                return True
            return radar.clear(x0, z0, (x1 - x0) / d, (z1 - z0) / d, d)

        def plen(p):
            return sum(math.hypot(p[(i + 1) % len(p)][0] - p[i][0],
                                  p[(i + 1) % len(p)][1] - p[i][1])
                       for i in range(len(p) if closed else len(p) - 1))

        r0, t0 = smooth_path.curvature_ok(flown, MIN_RADIUS, closed)
        # A second, thinner mask used ONLY for rounding corners. Straights keep
        # the full standoff; a corner may spend down to CORNER_STANDOFF, because
        # a corner is the one place the path must bulge sideways and it is where
        # sharpness shows most.
        keep_r = nav.BODY_R
        nav.BODY_R = CORNER_STANDOFF
        c_raw, c_plan, _cd, _cp = nav.build_world(bake, None)
        nav.BODY_R = keep_r
        c_radar = nav.Radar(bake, c_plan, c_raw, cell_m)

        def corner_clear(x0, z0, x1, z1):
            d = math.hypot(x1 - x0, z1 - z0)
            if d < 1e-6:
                return True
            return c_radar.clear(x0, z0, (x1 - x0) / d, (z1 - z0) / d, d)

        sm, kept, spikes, refused = smooth_path.smooth(
            flown, seg_clear, nav.STEP, closed=closed,
            reach=SMOOTH_REACH, iterations=SMOOTH_ITERS, tight_deg=SPIKE_DEG,
            corner_clear=corner_clear)
        r1, t1 = smooth_path.curvature_ok(sm, MIN_RADIUS, closed)
        print(f"  despiked {spikes[0]} before the shortcut, {spikes[1]} after; "
              f"Chaikin refused {refused} corner cuts as unsafe")
        print(f"  smoothed: {len(flown)} -> {kept} shortcut -> {len(sm)} points, "
              f"{plen(flown):.0f} -> {plen(sm):.0f} m")
        print(f"  tightest turn {r0:.1f} -> {r1:.1f} m, "
              f"corners under {MIN_RADIUS:.0f} m: {t0} -> {t1}")
        flown = sm

    path = np.asarray(flown, dtype=float)
    x = path[:, 0]
    z = path[:, 1]
    n = len(x)

    # ---------------------------------------------------------------- arc len
    dx = np.diff(x, append=x[0] if closed else x[-1])
    dz = np.diff(z, append=z[0] if closed else z[-1])
    seg = np.hypot(dx, dz)
    step_m = float(np.mean(seg[seg > 0])) if np.any(seg > 0) else 1.0
    s = np.concatenate([[0.0], np.cumsum(seg)[:-1]])
    total = float(np.sum(seg))
    print(f"  {total:.0f} m at {step_m:.2f} m per point")

    # --------------------------------------------------------------- altitude
    ground = np.array([bake.sample(bake.floor, x[i], z[i]) for i in range(n)])

    if worlds is not None:
        # Terraced. The flown level is piecewise constant, so a raw copy would
        # teleport the camera at every step. Running maximum over a lead window
        # first, then smooth: the climb starts before the step and the ramp
        # never sits below either of the two levels it joins, which a plain
        # smoothing would do right at the boundary - exactly where the higher
        # terrace's ground is.
        flown = np.array([float(v) for v in res["levels"]], dtype=float)
        # Twice the ramp, not once. With the two equal, the smoothing kernel
        # still reaches past the held maximum at the centre of a step and pulls
        # the camera under its own terrace - measured at 2.59 m above ground
        # where 4 was asked for. Holding the max across the whole kernel costs
        # only a slightly earlier climb.
        lead = max(1, int(round(2.0 * TERRACE_RAMP / step_m)))
        need = np.stack([np.roll(flown, k) for k in range(-lead, lead + 1)]).max(axis=0)
        y = periodic_smooth(need, TERRACE_RAMP, step_m, closed)
    elif level is not None:
        # Locked. Nothing to smooth and nothing to lead - the whole point of a
        # level flight is that the camera does not move vertically at all, so
        # the tilt below comes out as the bias alone and the horizon holds still.
        y = np.full(n, float(level))
    else:

        # Running maximum over a window FIRST, then smooth. Not
        # smooth-then-clamp: clamping afterwards puts the terrain's sharp edges
        # straight back into the curve the smoothing just removed, and tilt is a
        # derivative of this - the first version ran to 45 degrees of pitch off
        # 2 m steps.
        lead = max(1, int(round(ALT_LEAD / step_m)))
        need = ground + nav.AGL
        need = np.stack([np.roll(need, k) for k in range(-lead, lead + 1)]).max(axis=0)
        y = periodic_smooth(need, ALT_SMOOTH, step_m, closed)

    # ---------------------------------------------------------------- heading
    heading_raw = np.arctan2(dx, dz)
    heading = smooth_heading(heading_raw, HEAD_SMOOTH, step_m, closed)

    # ------------------------------------------------------------------- tilt
    # Aim at the path ahead, not at the local climb rate. Over a dip the point
    # 30 m along is near your own height, so the camera holds level instead of
    # following the ground down and staring at it.
    la = max(1, int(round(TILT_LOOKAHEAD / step_m)))
    if closed:
        ax, ay, az = np.roll(x, -la), np.roll(y, -la), np.roll(z, -la)
    else:
        idx = np.minimum(np.arange(n) + la, n - 1)
        ax, ay, az = x[idx], y[idx], z[idx]
    horiz = np.maximum(np.hypot(ax - x, az - z), 1e-6)
    tilt = np.arctan2(ay - y, horiz)
    tilt = periodic_smooth(tilt, TILT_SMOOTH, step_m, closed) + math.radians(TILT_BIAS)
    # A flythrough that pitches past this is not framing anything any more.
    tilt = np.clip(tilt, -math.radians(MAX_TILT), math.radians(MAX_TILT))

    # ------------------------------------------------------------------- roll
    # Curvature comes from a FLATTENED copy of the path, not the flown one. The
    # camera still flies every metre of the real route; only the bank is worked
    # out from the smoothed version.
    def curvature_at(flatten):
        gx = periodic_smooth(x, flatten, step_m, closed)
        gz = periodic_smooth(z, flatten, step_m, closed)
        gdx = np.diff(gx, append=gx[0] if closed else gx[-1])
        gdz = np.diff(gz, append=gz[0] if closed else gz[-1])
        gseg = np.maximum(np.hypot(gdx, gdz), 1e-6)
        # From the UNWRAPPED heading, or every seam crossing reads as an
        # infinitely tight corner.
        gh = smooth_heading(np.arctan2(gdx, gdz), HEAD_SMOOTH, step_m, closed)
        d = np.diff(gh, append=gh[0] if closed else gh[-1])
        if closed:
            d[-1] = ((gh[0] - gh[-1] + math.pi) % (2 * math.pi)) - math.pi
        return d / gseg

    # Two curvatures, blended by how tight the turn is.
    #
    # One flattening length cannot serve both jobs: enough smoothing to keep the
    # horizon still on a straight also clips the peak off a sharp corner, and
    # the roll then arrives 33% short of the coordinated angle exactly where the
    # bank should be most obvious. So take the calm version on straights and
    # hand over to the responsive one as the corner tightens - finer resolution
    # only where it is needed.
    calm = curvature_at(ROLL_FLATTEN)
    fine = curvature_at(ROLL_FLATTEN_FINE)
    w = np.clip(np.abs(calm) / TIGHT_K, 0.0, 1.0)
    curvature = calm * (1.0 - w) + fine * w

    # Coordinated turn: the bank that would keep a drink level in the cup.
    # atan(v^2 * k / g). Physically motivated rather than a made-up curve, so
    # the gain and the cap are the only arbitrary numbers.
    #
    # NEGATED, and this was a real bug: heading is atan2(dx, dz), so INCREASING
    # heading turns toward +X - and looking down +Z, LookAt puts screen-right at
    # -X, which makes that a LEFT turn. Positive roll was measured to bank
    # RIGHT. So the raw sign banked out of every corner, the way a car leans,
    # instead of into it the way anything flying does.
    bank = -np.arctan(CRUISE * CRUISE * curvature / 9.81) * BANK_GAIN
    bank = np.clip(bank, -math.radians(MAX_BANK), math.radians(MAX_BANK))

    # Lead it, THEN smooth. Rolling in before the corner and out the far side is
    # the whole point; smoothing after the shift keeps the entry and exit soft
    # instead of stepping to the led value.
    lead = int(round(BANK_LEAD / step_m))
    if lead > 0:
        bank = np.roll(bank, -lead) if closed else np.concatenate(
            [bank[lead:], np.full(lead, bank[-1])])
    roll = periodic_smooth(bank, BANK_SMOOTH, step_m, closed)

    speed = np.full(n, CRUISE)

    pts = [(float(x[i]), float(y[i]), float(z[i]),
            float(heading[i]), float(tilt[i]), float(roll[i]),
            float(s[i]), float(speed[i])) for i in range(n)]

    return bake, raw, res, pts, total, closed, step_m, level, worlds


def check_clips(bake, raw, pts, worlds=None, levels=None):
    """The exported positions must still be collision-free.

    Positions are passed through untouched, so this should be zero by
    construction - which is exactly why it is worth asserting. If a future
    change starts resmoothing them, this is what says so.
    """
    by_level = {}
    if worlds:
        for (lvl, pl, rw, dm) in worlds:
            by_level[round(float(lvl), 3)] = rw

    bad = 0
    for k, (px, py, pz, *_rest) in enumerate(pts):
        c, r = bake.texel_of(px, pz)
        ci = int(np.clip(round(c), 0, bake.w - 1))
        ri = int(np.clip(round(r), 0, bake.h - 1))
        m = raw
        if levels is not None and k < len(levels) and levels[k] is not None:
            m = by_level.get(round(float(levels[k]), 3), raw)
        if m[ri, ci]:
            bad += 1
    return bad


def draw(bake, raw, pts, out_png):
    """The route coloured by bank, so the roll can be read off the map."""
    from PIL import Image, ImageDraw

    o = bake.obstacle
    img = np.zeros((bake.h, bake.w, 3), dtype=np.uint8)
    g = bake.floor
    gn = (g - g.min()) / max(1e-6, (g.max() - g.min()))
    img[..., 0] = (16 + 22 * gn).astype(np.uint8)
    img[..., 1] = (19 + 26 * gn).astype(np.uint8)
    img[..., 2] = (26 + 34 * gn).astype(np.uint8)

    low = (o > 0.5) & (~raw)
    img[low] = (70, 74, 80)
    img[raw] = (196, 150, 40)

    im = Image.fromarray(img, "RGB")
    d = ImageDraw.Draw(im)

    mx = max(abs(p[5]) for p in pts) or 1e-6
    for i in range(len(pts) - 1):
        a, b = pts[i], pts[i + 1]
        c0, r0 = bake.texel_of(a[0], a[2])
        c1, r1 = bake.texel_of(b[0], b[2])
        t = a[5] / mx
        if t >= 0:                       # right bank -> warm
            col = (255, int(120 - 80 * t), int(200 - 150 * t))
        else:                            # left bank -> cool
            col = (int(120 + 80 * t), int(200 + 55 * t), 255)
        d.line([(c0, r0), (c1, r1)], fill=col, width=3)

    c, r = bake.texel_of(pts[0][0], pts[0][2])
    d.ellipse([c - 8, r - 8, c + 8, r + 8], fill=(80, 255, 130), outline=(255, 255, 255))
    d.text((12, 12), f"bank: warm = right, cool = left, max "
                     f"{math.degrees(mx):.1f} deg", fill=(235, 235, 240))
    im.save(out_png)


def main(out_dir=None, seed=None):
    """Raises RuntimeError on a bad export, NOT SystemExit.

    out_dir overrides where the .campath lands. Path Studio passes a scratch
    folder so that generating a route does not publish it - the file in
    cam_paths is what nuTerra flies, and replacing it should be a decision,
    not a side effect of pressing Generate.

    This module is imported and called as a library by path_studio, and
    SystemExit inherits from BaseException rather than Exception - so a normal
    `except Exception` around the call does not catch it. The worker thread died
    silently, neither the done nor the failed handler ran, and the Generate
    button stayed disabled forever with nothing said. A library should raise
    something a caller can reasonably catch.
    """
    map_name = sys.argv[1] if len(sys.argv) > 1 else nav.MAP

    bake, raw, res, pts, total, closed, step_m, level, worlds = build(map_name)

    out_dir = out_dir or cam_path.campath_dir()
    os.makedirs(out_dir, exist_ok=True)

    # Only the .campath goes in the project folder - that directory is copied
    # into the build output, so a debug PNG left there would ship. The CSV and
    # the bank picture join the rest of the diagnostics in TEMP.
    binp = os.path.join(out_dir, map_name + ".campath")
    csvp = os.path.join(nav.FOLDER, map_name + "_campath.csv")
    pngp = os.path.join(nav.FOLDER, map_name + "_bank.png")

    print("write")
    size = cam_path.write_path(binp, pts, map_name, closed=closed,
                               total_len=total, seed=seed)

    # Verify the SEED too. It is the half nothing downstream reads, so a
    # bug in it would sit in every file until someone tried to reuse one.
    ok, why = cam_path.verify(binp, pts, seed=seed)
    print(f"  {binp}  {size} bytes")
    print(f"  round trip: {'OK' if ok else 'FAILED'} - {why}")
    if not ok:
        raise RuntimeError("the file does not read back as what was written")

    # Not res["levels"] - after smoothing the point count has changed and that
    # array no longer lines up with pts. Terrain-following has one world anyway.
    clips = check_clips(bake, raw, pts, worlds=worlds if not SMOOTH else None)
    print(f"  clips on the exported positions: {clips}")

    agl = sorted(p[1] - bake.sample(bake.floor, p[0], p[2]) for p in pts)
    med = agl[len(agl) // 2]
    p90 = agl[int(len(agl) * 0.9)]
    sat = sum(1 for p in pts if abs(math.degrees(p[4])) >= MAX_TILT - 0.01)
    asked = ("terraced" if worlds is not None
             else (f"locked at Y = {level:.1f}" if level is not None
                   else f"asked for {nav.AGL:.1f} AGL"))
    print(f"  height above ground {agl[0]:.2f} .. {agl[-1]:.2f} m "
          f"(median {med:.1f}, 90th {p90:.1f}, {asked})")
    print(f"  tilt at the {MAX_TILT:.0f} deg cap: {100 * sat / len(pts):.1f}% of the path")
    if clips:
        raise RuntimeError("exported path passes through an obstacle")

    with open(csvp, "w", newline="") as f:
        w = csv.writer(f)
        w.writerow(["i", "x", "y", "z", "heading_deg", "tilt_deg", "roll_deg", "s_m", "speed"])
        for i, p in enumerate(pts):
            w.writerow([i, round(p[0], 3), round(p[1], 3), round(p[2], 3),
                        round(math.degrees(p[3]), 3), round(math.degrees(p[4]), 3),
                        round(math.degrees(p[5]), 3), round(p[6], 2), round(p[7], 2)])

    draw(bake, raw, pts, pngp)

    print()
    print(cam_path.describe(binp))
    print()
    rolls = [math.degrees(p[5]) for p in pts]
    print(f"  bank left {min(rolls):+.1f} deg, right {max(rolls):+.1f} deg, "
          f"{sum(1 for r in rolls if abs(r) > 5) * 100 // len(rolls)}% of the path banked over 5 deg")
    print(f"  flight time {total / CRUISE:.0f} s at {CRUISE:.0f} m/s")
    print(f"  csv {csvp}")
    print(f"  png {pngp}")


if __name__ == "__main__":
    main()
