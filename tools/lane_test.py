r"""
Lane test harness for the radar navigator (radar_commit.py).

Four nominal courses drawn STRAIGHT through the monastery village, flown
there and back as closed loops, so the navigator is forced to find a way
through the lanes instead of being handed a course that already avoids them
(the shipped route never asks it to - the A* lays the course around every
lane). Prints one line per course and a total; draws each flight to
%TEMP%
uTerralight\lane_<tag>_<n>.png.

    python lane_test.py [standoff_m] [tag]

The seed is fixed, so the same four courses come back every run and two runs
compare. Baseline on 2026-09-09 before the lane work: 2/4 closed, 3236
reversals; after: 3/4 closed, 0 reversals. See docs/camera_flight_plan.md,
step 4b.
"""
import sys, math, os, numpy as np
from scipy import ndimage
sys.path.insert(0, r"C:\nuTerra\tools")
import radar_commit as nav

OUT = nav.FOLDER
standoff = float(sys.argv[1]) if len(sys.argv) > 1 else 2.0
tag = sys.argv[2] if len(sys.argv) > 2 else "base"

bake = nav.Bake(nav.FOLDER, "19_monastery")
nav.BODY_R = standoff
raw, plan, dist_m, pad = nav.build_world(bake, None)
radar = nav.Radar(bake, plan, raw, bake.mx)
free = ~plan
d = ndimage.distance_transform_edt(~raw) * bake.mx
lane = free & (d <= 4.0)            # open at this standoff, in a corridor <= 8 m wide

def cells_between(a, b, step=0.5):
    n = max(2, int(math.hypot(b[0]-a[0], b[1]-a[1]) / step))
    hits = lanes = 0
    for k in range(n + 1):
        t = k / n
        x, z = a[0] + (b[0]-a[0]) * t, a[1] + (b[1]-a[1]) * t
        c, r = bake.texel_of(x, z)
        ci = int(np.clip(round(c), 0, bake.w-1)); ri = int(np.clip(round(r), 0, bake.h-1))
        if raw[ri, ci]: hits += 1
        if lane[ri, ci]: lanes += 1
    return hits, lanes

rng = np.random.default_rng(7)
fr = np.argwhere(free & (d > 6.0))   # start/end in open ground
cands = []
for _ in range(4000):
    i, j = rng.integers(0, len(fr), 2)
    a = bake.world_of(fr[i][1], fr[i][0]); b = bake.world_of(fr[j][1], fr[j][0])
    L = math.hypot(b[0]-a[0], b[1]-a[1])
    if not (120.0 <= L <= 260.0): continue
    hits, lanes = cells_between(a, b)
    if hits > 0 and lanes >= 40:
        cands.append((lanes, hits, a, b))
cands.sort(key=lambda c: -c[0])
picked = cands[:4]

print("standoff %.1f m, %d candidate courses, flying the 4 with most lane cells" % (standoff, len(cands)))
tot = dict(closed=0, reversals=0, boxed=0, guard=0, detours=0, backups=0, length=0.0, clips=0)
for k, (lanes, hits, a, b) in enumerate(picked):
    # there and back, 4 m samples, as a closed loop
    L = math.hypot(b[0]-a[0], b[1]-a[1]); n = max(4, int(L / 4.0))
    xs = [a[0] + (b[0]-a[0]) * t / n for t in range(n)] + [b[0] + (a[0]-b[0]) * t / n for t in range(n)]
    zs = [a[1] + (b[1]-a[1]) * t / n for t in range(n)] + [b[1] + (a[1]-b[1]) * t / n for t in range(n)]
    nx, nz = np.array(xs), np.array(zs)
    res = nav.fly(bake, radar, nx, nz, True, record_fans=False)
    try:
        sc = nav.score(bake, radar, res, nx, nz, dist_m)
        length, clips = sc["length"], sc["clips"]
    except Exception as e:
        sc = None; length = clips = -1; print("  score failed:", e)
    backups = res.get("backups", 0)
    print("  course %d: (%.0f,%.0f)->(%.0f,%.0f) %3.0f m, %3d lane cells on the line | closed=%s len=%5.0f clips=%d reversals=%d detours=%d boxed=%d guard=%d backups=%d steps=%d"
          % (k, a[0], a[1], b[0], b[1], L, lanes, res["closed"], length, clips, res["reversals"],
             res["detours"], res["stuck"], res["guard_fires"], backups, res["steps"]))
    tot["closed"] += int(res["closed"]); tot["reversals"] += res["reversals"]; tot["boxed"] += res["stuck"]
    tot["guard"] += res["guard_fires"]; tot["detours"] += res["detours"]; tot["backups"] += backups
    tot["length"] += max(length, 0); tot["clips"] += max(clips, 0)
    if sc is not None:
        try:
            nav.draw(bake, res, sc, nx, nz, os.path.join(OUT, "lane_%s_%d.png" % (tag, k)))
        except Exception as e:
            print("  draw failed:", e)
print("TOTAL closed %d/4  reversals %d  detours %d  boxed %d  guard %d  backups %d  length %.0f  clips %d"
      % (tot["closed"], tot["reversals"], tot["detours"], tot["boxed"], tot["guard"], tot["backups"], tot["length"], tot["clips"]))
