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

# Chaikin cut ratios, tried in order. 0.25 is the classic corner cut; the rest
# are fallbacks for corners where it would clip.
CUT_RATIOS = (0.25, 0.15, 0.08, 0.04)


def _seg_clear(clear, a, b):
    return clear(a[0], a[1], b[0], b[1])


def despike(pts, clear, closed=True, tight_deg=55.0, passes=6):
    """Drop zig-zag spikes: points where the path doubles back instead of turning.

    Three consecutive points a, b, c. Two tests, and b is removed only if both
    pass and the shortcut is legal:

      1. The turn at b is tighter than tight_deg. A gentle bend is a corner, not
         a spike, and rounding those is Chaikin's job.

      2. Take the LONGER of a->b and b->c and build the circle on it as a
         DIAMETER. If the remaining point falls inside, the three points have
         doubled back on themselves.

         That circle test is exactly "does the far point see the long leg at
         more than 90 degrees" - the inscribed angle in a semicircle is a right
         angle, so inside means obtuse. It is a cheap, scale-free way of asking
         whether c came back toward a rather than carrying on past b, which is
         what a spike is and what an honest corner is not. Written as a dot
         product rather than a distance to a centre: (q-A).(q-B) < 0.

      3. a->c is collision free. Without this the filter cheerfully cuts the
         corner into the thing the navigator was avoiding - a spike is often a
         spike BECAUSE something was in the way.

    Repeated until nothing more comes out, because removing one point can expose
    the next. Neighbours removed in the same pass are skipped so a run of spikes
    cannot collapse into a segment nobody checked.
    """
    # On a closed path the index range is the FULL n, so i=0 tests the triple
    # (last, first, second) and i=n-1 tests (n-2, n-1, first). The seam gets the
    # same test as everywhere else rather than being skipped as an end - which
    # is what range(1, n-1) would do, and is right only for an open path.
    tight = math.radians(tight_deg)
    cur = list(pts)
    total = 0

    for _ in range(max(1, passes)):
        n = len(cur)
        if n < 5:
            break
        keep = [True] * n
        removed = 0
        rng = range(n) if closed else range(1, n - 1)

        for i in rng:
            if not keep[i]:
                continue
            ia, ic = (i - 1) % n, (i + 1) % n
            if not keep[ia] or not keep[ic]:
                continue
            a, b, c = cur[ia], cur[i], cur[ic]

            ux, uz = b[0] - a[0], b[1] - a[1]
            vx, vz = c[0] - b[0], c[1] - b[1]
            lu = math.hypot(ux, uz)
            lv = math.hypot(vx, vz)
            if lu < 1e-6 or lv < 1e-6:
                continue

            dot = (ux * vx + uz * vz) / (lu * lv)
            if math.acos(min(max(dot, -1.0), 1.0)) < tight:
                continue

            if lu >= lv:
                A, B, q = a, b, c
            else:
                A, B, q = b, c, a
            if ((q[0] - A[0]) * (q[0] - B[0]) + (q[1] - A[1]) * (q[1] - B[1])) >= 0.0:
                continue

            if not _seg_clear(clear, a, c):
                continue

            keep[i] = False
            removed += 1

        if removed == 0:
            break
        cur = [p for p, k in zip(cur, keep) if k]
        total += removed

    return cur, total


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

    # THE SEAM. The walk above runs 0 -> n-1 in a straight line, so the corner
    # AT the start is never shortcut across: point 0 survives as a vertex
    # whatever shape it is in. On a closed loop that leaves exactly one corner
    # the rest of the pipeline cannot improve, at the one place every lap goes
    # through - and it is the corner most likely to be sharp, because it is the
    # join between the end of the route and the beginning.
    #
    # Same test as everywhere else: if the point before it can see the point
    # after it, the middle one is not carrying its weight. Repeated, because
    # dropping the seam exposes its neighbour to the same question.
    if closed:
        for _ in range(4):
            if len(out) <= 3:
                break
            if _seg_clear(clear, out[-1], out[1]):
                out = out[1:]
            elif _seg_clear(clear, out[-2], out[0]):
                out = out[:-1]
            else:
                break
    return out


def chaikin(pts, clear, closed=True, iterations=4, corner_clear=None):
    """Chaikin corner cutting, with every generated point checked.

    One pass replaces each edge with its quarter and three-quarter points. Four
    passes is well past the point where the result is visually a curve.

    A pair is only cut when BOTH new points are reachable from their neighbours;
    otherwise the original vertex is kept, so a corner the navigator needed
    stays sharp rather than being rounded into an obstacle. That is why this
    cannot be replaced by scipy's splprep, which has no way to refuse.
    """
    # Corners may be checked against a MORE PERMISSIVE mask than the straights.
    # A corner is where a standoff costs the most - it is the only place the
    # path has to bulge sideways - so spending part of the margin there buys
    # smoothness where sharpness is most visible. Defaults to the same mask.
    corner_clear = corner_clear or clear
    cur = list(pts)
    refused = 0
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

            # Chaikin's classic cut is at a quarter and three quarters. When
            # that is refused, retry GENTLER rather than giving up: a smaller
            # cut moves the path less, so it is more likely to stay clear, and a
            # partly rounded corner is worth more than a sharp one.
            #
            # Refusing outright left 25 corners uncut on Abbey and the tightest
            # turn at 2.2 m. Those are exactly the corners the camera has to
            # throw itself round.
            done = False
            for t in CUT_RATIOS:
                q = (a[0] * (1 - t) + b[0] * t, a[1] * (1 - t) + b[1] * t)
                r = (a[0] * t + b[0] * (1 - t), a[1] * t + b[1] * (1 - t))
                if (_seg_clear(corner_clear, a, q) and _seg_clear(corner_clear, q, r)
                        and _seg_clear(corner_clear, r, b)):
                    nxt.append(q)
                    nxt.append(r)
                    done = True
                    break
            if not done:
                nxt.append(a)
                nxt.append(b)
                refused += 1
        if not closed:
            nxt.append(cur[-1])

        # The CORNER CHORD - from one edge's r to the next edge's q - straddles
        # the old vertex and is the segment that actually cuts the corner. The
        # per-edge tests above never touch it, so a corner could be rounded
        # straight through an obstacle and nothing here would notice. Check it,
        # and put the old vertex back where it fails.
        m = len(nxt)
        fixed = []
        for k in range(m):
            fixed.append(nxt[k])
            if k % 2 == 1 and (closed or k + 1 < m):
                nk = (k + 1) % m
                if not _seg_clear(corner_clear, nxt[k], nxt[nk]):
                    # restore the vertex this pair was cut from
                    src = cur[((k // 2) + 1) % len(cur)]
                    fixed.append(src)
                    refused += 1
        cur = fixed
    return cur, refused


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


def smooth(pts, clear, step_m, closed=True, reach=None, iterations=4,
           tight_deg=55.0, corner_clear=None):
    """The whole pipeline: despike, shortcut, cut corners, resample evenly.

    Despike FIRST. It removes the doubling-back that the shortcut would
    otherwise have to reach across, and it is the pass that targets what is
    actually visible - a sharp zig is far more obvious in flight than a route
    that is a few metres longer than it needs to be.
    """
    d, s1 = despike(pts, clear, closed=closed, tight_deg=tight_deg)
    a = shortcut(d, clear, closed=closed, max_reach=reach)

    # Despike AGAIN, after the shortcut. This is where it actually bites.
    #
    # On the raw path every leg is one STEP long, so the circle-on-a-diameter
    # test only fires on a near-complete reversal and finds nothing - measured,
    # 0 points on Abbey. After string-pulling the vertices are tens of metres
    # apart and a genuine zig finally looks like a spike at this scale, which is
    # the scale it is visible at in flight.
    a, s2 = despike(a, clear, closed=closed, tight_deg=tight_deg)

    b, refused = chaikin(a, clear, closed=closed, iterations=iterations,
                         corner_clear=corner_clear)
    return resample(b, step_m, closed=closed), len(a), (s1, s2), refused
