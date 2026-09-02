"""
Collision-checked path smoothing.

A reactive navigator leaves a path that is correct and ugly: it steps at a fixed
STEP, turns in whole SWEEP_STEP increments, and every avoidance leaves a kink.
Running a spline through that is the obvious fix and the wrong one - a spline
cuts exactly the corners the navigator deliberately went around, so it smooths
the path straight into the building it was avoiding.

The standard motion-planning answer is two passes, and the order matters:

  1. SHORTCUT (also called string-pulling, or path pruning). Walk the path and
     for each point reach as far forward as possible, replacing the whole
     stretch with a straight line whenever that line is collision free. This is
     what removes the zig-zag; a reactive path is mostly detours that stopped
     being necessary a few metres later.

  2. CHAIKIN corner cutting. Replace every point with two points a quarter and
     three quarters along each edge. Repeated, this converges to a quadratic
     B-spline - so it is a spline, arrived at by subdivision, which is what lets
     each new point be tested before it is accepted.

Every candidate in both passes is collision checked against the same mask the
navigator flew by, and rejected candidates simply leave the original point in
place. That is the whole difference between this and fitting a curve.

The caller supplies `clear(x0, z0, x1, z1)` - true when that straight segment is
flyable. Nothing here knows about bakes or radars.
"""

import math


def _seg_clear(clear, a, b):
    return clear(a[0], a[1], b[0], b[1])


def shortcut(pts, clear, closed=True, max_reach=None):
    """String-pulling. Greedy: from each kept point, take the furthest reachable
    point ahead that is still collision free.

    Greedy rather than the random-pair shortcutting some planners use, because
    this path is a closed loop being exported once - determinism is worth more
    here than the marginally shorter result random restarts would find, and a
    route that changes every time it is generated is impossible to review.

    max_reach bounds how far ahead to look. Without it a loop can shortcut
    across its own middle and stop being a loop at all.
    """
    n = len(pts)
    if n < 3:
        return list(pts)

    reach = n - 1 if max_reach is None else max(2, int(max_reach))
    out = [pts[0]]
    i = 0
    guard = 0
    while i < n - 1 and guard < 10 * n:
        guard += 1
        best = i + 1
        hi = min(n - 1, i + reach)
        for j in range(hi, i + 1, -1):
            if _seg_clear(clear, pts[i], pts[j]):
                best = j
                break
        out.append(pts[best])
        i = best

    # A closed loop must still close: the last kept point has to see the first.
    if closed and len(out) > 2 and not _seg_clear(clear, out[-1], out[0]):
        out.append(pts[-1])
    return out


def chaikin(pts, clear, closed=True, iterations=4):
    """Chaikin corner cutting, with every generated point checked.

    One pass replaces each edge with its quarter and three-quarter points. Four
    passes is well past the point where the result is visually a curve.

    A pair is only cut when BOTH new points are reachable from their neighbours;
    otherwise the original vertex is kept, so a corner the navigator needed
    stays sharp rather than being rounded into an obstacle. That is why this
    cannot be replaced by scipy's splprep, which has no way to refuse.
    """
    cur = list(pts)
    for _ in range(max(0, iterations)):
        n = len(cur)
        if n < 3:
            break
        nxt = []
        rng = range(n) if closed else range(n - 1)
        if not closed:
            nxt.append(cur[0])
        for i in rng:
            a = cur[i]
            b = cur[(i + 1) % n]
            q = (a[0] * 0.75 + b[0] * 0.25, a[1] * 0.75 + b[1] * 0.25)
            r = (a[0] * 0.25 + b[0] * 0.75, a[1] * 0.25 + b[1] * 0.75)
            if _seg_clear(clear, a, q) and _seg_clear(clear, q, r) and _seg_clear(clear, r, b):
                nxt.append(q)
                nxt.append(r)
            else:
                nxt.append(a)
                nxt.append(b)
        if not closed:
            nxt.append(cur[-1])
        cur = nxt
    return cur


def resample(pts, step_m, closed=True):
    """Even spacing along the curve.

    By arc length, not by index. Chaikin leaves points bunched where the path
    was already dense, and an unevenly sampled path makes the exported speed
    and heading wobble for no reason in the geometry.
    """
    n = len(pts)
    if n < 2:
        return list(pts)

    ring = list(pts) + ([pts[0]] if closed else [])
    seg = [math.hypot(ring[i + 1][0] - ring[i][0], ring[i + 1][1] - ring[i][1])
           for i in range(len(ring) - 1)]
    total = sum(seg)
    if total <= 0.0:
        return list(pts)

    count = max(8, int(round(total / max(step_m, 1e-6))))
    out = []
    target = 0.0
    i = 0
    acc = 0.0
    for _ in range(count):
        while i < len(seg) - 1 and acc + seg[i] < target:
            acc += seg[i]
            i += 1
        t = 0.0 if seg[i] <= 0 else (target - acc) / seg[i]
        t = min(max(t, 0.0), 1.0)
        a, b = ring[i], ring[i + 1]
        out.append((a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t))
        target += total / count
    return out


def curvature_ok(pts, min_radius_m, closed=True):
    """Worst turn radius on the path, and how much of it is tighter than the
    limit. Reported rather than enforced - a camera that cannot make a corner is
    a speed problem, not a geometry one."""
    n = len(pts)
    if n < 3:
        return 1e9, 0
    worst = 1e9
    tight = 0
    rng = range(n) if closed else range(1, n - 1)
    for i in rng:
        a = pts[(i - 1) % n]
        b = pts[i]
        c = pts[(i + 1) % n]
        ux, uz = b[0] - a[0], b[1] - a[1]
        vx, vz = c[0] - b[0], c[1] - b[1]
        lu = math.hypot(ux, uz)
        lv = math.hypot(vx, vz)
        if lu < 1e-6 or lv < 1e-6:
            continue
        cross = (ux * vz - uz * vx) / (lu * lv)
        cross = min(max(cross, -1.0), 1.0)
        dth = abs(math.asin(cross))
        if dth < 1e-9:
            continue
        r = (0.5 * (lu + lv)) / dth
        worst = min(worst, r)
        if r < min_radius_m:
            tight += 1
    return worst, tight


def smooth(pts, clear, step_m, closed=True, reach=None, iterations=4):
    """The whole pipeline: shortcut, cut corners, resample evenly."""
    a = shortcut(pts, clear, closed=closed, max_reach=reach)
    b = chaikin(a, clear, closed=closed, iterations=iterations)
    return resample(b, step_m, closed=closed), len(a)
