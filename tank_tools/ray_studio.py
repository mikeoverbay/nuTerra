"""Ray Studio - watch the tank route resolver hunt, live.

The owner, after closing two nuTerra windows full of tanks doing things:

    "I don/t want to see the tanks searching. I want a window top view of the
     algo's progression of finding routes."
    "this is a path resolver like path studio. Just different space."
    "I just wanna draw it live the same way the path studio code does sorta."

So this is a tool, not a mode of the game. It owns its window, it draws the
whole map, and it draws every ray as it is cast. No tanks. Nothing that moves
except the search.

WHY IT IS PYTHON AND NOT IN THE APP. An algorithm you are still designing wants
a loop you can go round in a second. The VB version takes forty seconds to
build and run before a single pixel appears, which is why the resolver spent an
afternoon being wrong in ways nobody could see. Path Studio exists for the same
reason and this is deliberately its sibling: same idea, different space - it
resolves ground routes where that one resolves camera flights.

THE ALGORITHM IS THE OWNER'S, and it is rays rather than a grid search:

    cast a ray at the flag
    where it hits, open an EXPANDING RING at the collision point
    sweep the ring for the angles that clear - LEFT FIRST
    each angle that gets somewhere is a branch; cast again from there
    a branch that reaches the flag is a path
    a branch that runs out of ring is a dead end, and stays on screen

The thing it replaces expanded 466,229 grid cells to find one route. This casts
about 800 rays. That difference is the whole reason it can be watched.

WHAT THE COLOURS MEAN
    dim green / grey    open ground, and what is shut and why
    DEEP RED            a ray that led nowhere. Never cleared, so the dead ends
                        pile up into a picture of everywhere it has been
    YELLOW              the ray being cast right now
    BRIGHT GREEN        a ray on a path that reached the flag
    CYAN / ORANGE       where it started, and the flag it is hunting

    python tank_tools/ray_studio.py [map] [--hull 4.5] [--speed 8]
"""

import os
import sys
import struct
import numpy as np

FLIGHT = os.path.join(os.environ.get("TEMP", "."), "nuTerra", "flight")

# TankNav's own numbers, so this tool and the app agree about the ground.
CELL_TEXELS = 8
MAX_OBSTACLE_M = 1.0
MAX_SLOPE = 0.7
KIND_MASK, OUTLAND_BIT, TRUNK_BIT = 7, 16, 128
KIND_FENCE, KIND_TREE, KIND_PROP, KIND_WATER = 2, 3, 5, 6


def read_meta(path):
    meta = {}
    with open(path, "r", encoding="utf-8", errors="replace") as f:
        for line in f:
            line = line.strip()
            if not line or line.startswith("#") or "=" not in line:
                continue
            k, v = line.split("=", 1)
            meta[k.strip()] = v.strip()
    return meta


def build_grid(map_name, hull_r_m):
    """The drivable mask and the clearance field, by TankNav's rules.

    THE RULES ARE COPIED AND THAT IS A LIABILITY, so they are all here in one
    place with the app's constant names next to them. If TankNav changes what
    it calls passable and this does not, the studio will draw a confident
    picture of a map the game does not have - which is worse than no picture.
    """
    meta = read_meta(os.path.join(FLIGHT, f"{map_name}_meta.txt"))
    W = int(meta["width"])
    wx0, wx1 = float(meta["wx_min"]), float(meta["wx_max"])
    wz0, wz1 = float(meta["wz_min"]), float(meta["wz_max"])
    scale = float(meta["height_scale"])

    top = np.fromfile(os.path.join(FLIGHT, f"{map_name}_top.rgba"),
                      dtype=np.uint8).reshape(W, W, 4)
    fl16 = np.fromfile(os.path.join(FLIGHT, f"{map_name}_floor.r16"),
                       dtype="<u2").reshape(W, W).astype(np.int32)

    key = top[:, :, 0]
    t16 = (top[:, :, 1].astype(np.int32) << 8) | top[:, :, 2]
    kind = key & KIND_MASK

    # The obstacle test, TankNav.vb: skipped for tree, fence and prop - a tank
    # goes through a fence, over a curb, and a canopy is answered by the trunk
    # bit instead.
    testable = (kind != KIND_TREE) & (kind != KIND_FENCE) & (kind != KIND_PROP)
    obstacle = testable & ((t16 - fl16) > int(MAX_OBSTACLE_M * scale))

    shut = obstacle | (key & TRUNK_BIT).astype(bool) \
        | (key & OUTLAND_BIT).astype(bool) | (kind == KIND_WATER)

    # Down to the nav grid: a cell is shut if ANY texel in it is shut. That is
    # conservative and it is what eats doorways - the nuTerra session measured
    # a 16 m corridor reading as nothing at this resolution.
    N = W // CELL_TEXELS
    blocked = shut.reshape(N, CELL_TEXELS, N, CELL_TEXELS).any(axis=(1, 3))

    # Slope, per cell, off the floor alone.
    cell_m = (wx1 - wx0) / N
    fb = fl16.reshape(N, CELL_TEXELS, N, CELL_TEXELS)
    rise = (fb.max(axis=(1, 3)) - fb.min(axis=(1, 3))) / scale
    blocked |= rise > (MAX_SLOPE * cell_m)

    from scipy import ndimage
    # Clearance, HALF A CELL BACK: the transform measures to the blocking
    # cell's centre and that cell is solid across its whole area.
    clear = (ndimage.distance_transform_edt(~blocked) - 0.5) * cell_m
    np.clip(clear, 0.0, None, out=clear)

    return dict(N=N, cell_m=cell_m, blocked=blocked, clear=clear, used=None,
                wx0=wx0, wx1=wx1, wz0=wz0, wz1=wz1, hull=hull_r_m)


def world_to_cell(g, x, z):
    cx = int((x - g["wx0"]) / (g["wx1"] - g["wx0"]) * g["N"])
    cz = int((g["wz1"] - z) / (g["wz1"] - g["wz0"]) * g["N"])
    return cx, cz


def cell_to_world(g, cx, cz):
    x = g["wx0"] + (cx + 0.5) * (g["wx1"] - g["wx0"]) / g["N"]
    z = g["wz1"] - (cz + 0.5) * (g["wz1"] - g["wz0"]) / g["N"]
    return x, z


def standable(g, x, z):
    cx, cz = world_to_cell(g, x, z)
    if cx < 0 or cz < 0 or cx >= g["N"] or cz >= g["N"]:
        return False
    if g["used"] is not None and g["used"][cz, cx]:
        return False
    return g["clear"][cz, cx] >= g["hull"]


def snap_free(g, x, z):
    if standable(g, x, z):
        return x, z
    cx, cz = world_to_cell(g, x, z)
    for ring in range(1, 65):
        for dz in range(-ring, ring + 1):
            for dx in range(-ring, ring + 1):
                if abs(dx) != ring and abs(dz) != ring:
                    continue
                nx, nz = cx + dx, cz + dz
                if 0 <= nx < g["N"] and 0 <= nz < g["N"] \
                        and g["clear"][nz, nx] >= g["hull"]:
                    return cell_to_world(g, nx, nz)
    return x, z


def sees(g, x, z, tx, tz):
    """Is there a clear straight line from here to there for this hull?

    This is the question that ends a corner. Marched at half a cell, the same
    step a ray uses, so a line this says is clear is a line a ray can fly.
    """
    dx, dz = tx - x, tz - z
    d = np.hypot(dx, dz)
    if d < 1e-6:
        return True
    dx, dz = dx / d, dz / d
    step = g["cell_m"] * 0.5
    t = 0.0
    while t < d:
        if not standable(g, x + dx * t, z + dz * t):
            return False
        t += step
    return True


def march(g, x, z, dx, dz, goal, limit):
    """How far a ray gets, and where it stops.

    Half a cell a step, so nothing narrower than a cell can be stepped over.
    """
    step = g["cell_m"] * 0.5
    t = 0.0
    gx, gz = goal
    while t < limit:
        if (x - gx) ** 2 + (z - gz) ** 2 <= REACH_M ** 2:
            return t, x, z, True
        nx, nz = x + dx * step, z + dz * step
        if not standable(g, nx, nz):
            return t, x, z, False
        x, z, t = nx, nz, t + step
    return t, x, z, False


REACH_M = 12.0

# THE RING IS A CIRCLE IN METRES, expanding half a metre at a time. The owner's
# words: "we draw a ring at that hit point and hit the tangent on both sides. if
# we could not after expanding the ring in .5m steps to max ring size in
# settings... That path is dead."
RING_STEP_M = 0.5
RING_MIN_M = 0.5
RING_MAX_DEFAULT_M = 3.0        # the setting, 0.5 to 5.0 by 0.5

# How finely the ring is walked looking for where it clears.
RING_ANGLE_STEP = np.deg2rad(6.0)

# How far round the ring counts as a tangent rather than a retreat.
RING_ARC_MAX = np.deg2rad(110.0)

# How many ring-and-re-aim hops one chain may take before it is called lost. A
# small ring means many small steps round a big obstacle, which is the design.
MAX_HOPS = 260

# The sweep: start pointing LEFT, finish pointing EAST.
SWEEP_FROM_DEG = -90.0
SWEEP_TO_DEG = 90.0
SWEEP_STEP_DEG = 3.0

REJECT_M = 55.0


def march(g, x, z, dx, dz, goal, limit):
    """Fly a ray until it hits something or reaches the flag."""
    step = g["cell_m"] * 0.5
    t = 0.0
    gx, gz = goal
    while t < limit:
        if (x - gx) ** 2 + (z - gz) ** 2 <= REACH_M ** 2:
            return t, x, z, True
        nx, nz = x + dx * step, z + dz * step
        if not standable(g, nx, nz):
            return t, x, z, False
        x, z, t = nx, nz, t + step
    return t, x, z, False


def ring_tangents(g, hx, hz, indx, indz, max_ring_m):
    """Draw a ring at the hit point and find where it clears, both sides.

    Exactly as described: a circle at the collision, grown in half-metre steps
    until a point on it is standable. Walked outward from the direction we were
    travelling, both ways at once, so the first clear angle on each side is the
    tangent past the thing we hit.

    Returns the two tangent points, either of which may be None, and the radius
    the ring had reached. Nothing found by max_ring_m means this path is DEAD -
    that is the owner's rule and it is what stops a chain crawling for ever.
    """
    base_ang = np.arctan2(indx, indz)
    r = RING_MIN_M
    while r <= max_ring_m + 1e-6:
        left = right = None
        a = RING_ANGLE_STEP
        # NEVER ALL THE WAY ROUND. Walking the ring to 180 degrees always finds
        # clear ground - straight back the way we came, which is where we have
        # just been. That is a retreat, not a tangent, and it was found at the
        # very first 0.5 m radius every single time, so the ring never expanded
        # and the max size made no difference at all from 1 m to 35 m. Identical
        # ray counts across seven times the setting is what gave it away.
        #
        # A tangent is the way PAST a thing, so it lives out to about square
        # with the direction of travel. Past that you are going home.
        while a <= RING_ARC_MAX:
            for sgn in (1.0, -1.0):
                if (sgn > 0 and left is not None) or (sgn < 0 and right is not None):
                    continue
                th = base_ang + a * sgn
                px, pz = hx + np.sin(th) * r, hz + np.cos(th) * r
                if standable(g, px, pz):
                    if sgn > 0:
                        left = (px, pz)
                    else:
                        right = (px, pz)
            if left is not None and right is not None:
                break
            a += RING_ANGLE_STEP
        if left is not None or right is not None:
            return left, right, r
        r += RING_STEP_M
    return None, None, r


def chain(g, start, goal, bearing, max_ring_m, trace):
    """One path attempt: ray, ring, tangent, re-aim at the base, repeat.

    "if we could not... That path is dead. If we can hit it, we anchor at
    tangent and scan at base location. same thing."

    So every hop after the first aims AT THE FLAG. The opening ray is the only
    one that goes where the sweep points it; after that the chain is always
    trying to go home and only turning aside to get round what is in the way.
    """
    gx, gz = goal
    limit = (g["wx1"] - g["wx0"]) * 1.5
    pts = [start]
    x, z = start
    dx, dz = np.sin(bearing), np.cos(bearing)
    seen_here = set()
    hand = 0                    # 0 undecided, +1 keep it on the left, -1 right

    for _ in range(MAX_HOPS):
        got, hx, hz, reached = march(g, x, z, dx, dz, goal, limit)
        trace.append(((x, z), (hx, hz), reached))
        if reached:
            pts.append((gx, gz))
            return pts                                   # THE PRIZE
        # A RAY THAT TRAVELS NOTHING IS NOT A DEAD PATH, IT IS ANOTHER HIT.
        #
        # After anchoring on a tangent, aiming at the base points straight back
        # into the thing we just went round, so the next ray is zero long. The
        # first version called that wedged and gave up - every chain died in two
        # hops, and the ring size made no difference at all from 5 m to 35 m,
        # which is what proved the ring was never the problem.
        #
        # By the rule it is simply the next collision: ring again, take a
        # tangent, crawl on. A small ring means many small steps round a big
        # obstacle, and that is the design rather than a fault in it.

        left, right, _ = ring_tangents(g, hx, hz, dx, dz, max_ring_m)
        if left is None and right is None:
            return None                                  # DEAD

        # COMMIT TO A HAND AND KEEP IT.
        #
        # Choosing the better side at every hop is why this oscillated: it
        # sidesteps left, re-aims at the base, turns back into the same wall,
        # and now the RIGHT side looks better - so it steps back, and round it
        # goes. Twenty hops later it revisits its own ground and the loop guard
        # kills it. That happened identically at 260 hops and at 4,000, which is
        # what showed it was never running out of room to crawl.
        #
        # Going round something means keeping it on one hand the whole way. The
        # first ring of a chain picks the side; every ring after it uses the
        # same one, so the chain commits to going round rather than dithering
        # at the face.
        options = (left, right) if hand == 0 else                   ((left,) if hand > 0 else (right,))
        best, best_got = None, -1.0
        for cand in options:
            if cand is None:
                continue
            cxx, czz = cand
            d = max(np.hypot(gx - cxx, gz - czz), 1e-6)
            g2, _, _, _ = march(g, cxx, czz, (gx - cxx) / d, (gz - czz) / d,
                                goal, limit)
            if g2 > best_got:
                best, best_got = cand, g2
        if best is None:
            return None

        # BUT IT MUST ACTUALLY MOVE. Crawling is fine; crawling on the spot is
        # a loop, and a loop is a dead path however long you let it run.
        # The bucket has to be FINER than a ring step, or crawling looks like
        # looping: a 0.5 m ring moves the anchor less than a 2 m bucket, so the
        # second anchor landed in the first one's square and every chain was
        # killed for going round in a circle it had not gone round.
        key = (round(best[0] / 0.4), round(best[1] / 0.4))
        if key in seen_here:
            return None
        seen_here.add(key)

        if hand == 0:
            hand = 1 if best is left else -1

        x, z = best
        pts.append(best)
        d = max(np.hypot(gx - x, gz - z), 1e-6)
        dx, dz = (gx - x) / d, (gz - z) / d              # scan at base location
    return None


def resample(path, n=24):
    if len(path) < 2:
        return [path[0]] * n if path else []
    segs, total = [], 0.0
    for i in range(1, len(path)):
        d = np.hypot(path[i][0] - path[i - 1][0], path[i][1] - path[i - 1][1])
        segs.append(d)
        total += d
    if total < 1e-6:
        return [path[0]] * n
    out, acc, i = [], 0.0, 1
    for k in range(n):
        want = total * k / (n - 1)
        while i < len(segs) and acc + segs[i - 1] < want:
            acc += segs[i - 1]
            i += 1
        if i >= len(path):
            out.append(path[-1])
            continue
        t = 0.0 if segs[i - 1] < 1e-6 else (want - acc) / segs[i - 1]
        out.append((path[i - 1][0] + (path[i][0] - path[i - 1][0]) * t,
                    path[i - 1][1] + (path[i][1] - path[i - 1][1]) * t))
    return out


def too_close(path, pool):
    a = resample(path)
    for other in pool:
        b = resample(other)
        if sum(np.hypot(p[0] - q[0], p[1] - q[1])
               for p, q in zip(a, b)) / len(a) < REJECT_M:
            return True
    return False


def resolve(g, start, goal, max_ring_m=RING_MAX_DEFAULT_M):
    """Sweep from LEFT round to EAST, one chain per bearing.

    "We will start scanning left. every fail or win, we change ray and try for
    another path chain. We do this until we are point east."

    A win and a loss cost the same thing - the next bearing - so the sweep is
    the whole outer loop and there is no backtracking anywhere in it.
    """
    sx, sz = snap_free(g, *start)
    gxy = snap_free(g, *goal)
    pool, rays = [], []
    for a_deg in np.arange(SWEEP_FROM_DEG, SWEEP_TO_DEG + 1e-6, SWEEP_STEP_DEG):
        trace = []
        got = chain(g, (sx, sz), gxy, np.deg2rad(a_deg), max_ring_m, trace)
        for seg in trace:
            rays.append((seg[0], seg[1], got is not None))
        if got is not None and not too_close(got, pool):
            pool.append(got)
        yield rays, pool, len(rays), a_deg
    yield rays, pool, len(rays), SWEEP_TO_DEG


def main():
    import pygame

    map_name = sys.argv[1] if len(sys.argv) > 1 and not sys.argv[1].startswith("-") \
        else "19_monastery"
    hull = 4.5
    speed = 1
    # A good resolve is now 43 rays, which at one a frame is over in under a
    # second - too fast to watch, which defeats the point of a live view. The
    # delay is per ray, so the hunt runs at a pace a person can follow.
    delay = 220
    for i, a in enumerate(sys.argv):
        if a == "--hull" and i + 1 < len(sys.argv):
            hull = float(sys.argv[i + 1])
        if a == "--speed" and i + 1 < len(sys.argv):
            speed = int(sys.argv[i + 1])
        if a == "--delay" and i + 1 < len(sys.argv):
            delay = int(sys.argv[i + 1])

    print(f"ray studio: {map_name}, hull {hull:.1f} m")
    g = build_grid(map_name, hull)
    N = g["N"]
    print(f"  grid {N}x{N} at {g['cell_m']:.2f} m, "
          f"{(~g['blocked']).sum():,} open cell(s)")

    # The two ctf bases. They are not in the meta - the app reads them from
    # arena_defs - so monastery's are given here and anything else needs them
    # passed in. Wrong is better than silent: it is printed.
    bases = {"19_monastery": ((-20.1, -387.8), (0.4, 397.4))}
    if map_name not in bases:
        print(f"  no base positions known for {map_name}")
        return
    start, goal = bases[map_name]
    print(f"  team 1 {start}  ->  team 2 {goal}")

    pygame.init()
    S = 1000
    screen = pygame.display.set_mode((S, S), pygame.RESIZABLE)
    pygame.display.set_caption(f"Ray Studio - {map_name} - [Tank AI work]")
    font = pygame.font.SysFont("consolas", 16)

    # The map, once. Everything else is drawn over it each frame.
    base = np.zeros((N, N, 3), dtype=np.uint8)
    base[...] = (22, 44, 26)
    base[g["blocked"]] = (62, 30, 20)
    tight = (~g["blocked"]) & (g["clear"] < hull)
    base[tight] = (44, 44, 30)          # open, but not for this hull
    surf = pygame.surfarray.make_surface(np.transpose(base, (1, 0, 2)))

    gen = resolve(g, start, goal)
    nodes, paths, rays = [], [], 0
    running, done, paused = True, False, False

    def to_px(x, z, w):
        cx = (x - g["wx0"]) / (g["wx1"] - g["wx0"]) * w
        cz = (g["wz1"] - z) / (g["wz1"] - g["wz0"]) * w
        return int(cx), int(cz)

    while running:
        for e in pygame.event.get():
            if e.type == pygame.QUIT:
                running = False
            elif e.type == pygame.KEYDOWN:
                if e.key in (pygame.K_ESCAPE, pygame.K_q):
                    running = False
                elif e.key == pygame.K_SPACE:
                    paused = not paused
                elif e.key == pygame.K_r:
                    gen = resolve(g, start, goal)
                    nodes, paths, rays, done = [], [], 0, False
                elif e.key == pygame.K_TAB:
                    start, goal = goal, start
                    gen = resolve(g, start, goal)
                    nodes, paths, rays, done = [], [], 0, False

        if not done and not paused:
            for _ in range(speed):
                try:
                    nodes, paths, rays = next(gen)
                except StopIteration:
                    done = True
                    break

        w = min(screen.get_width(), screen.get_height())
        view = pygame.transform.smoothscale(surf, (w, w))
        screen.fill((10, 10, 12))
        screen.blit(view, (0, 0))

        # Which rays ended up on a path, so the dead ends can be told from the
        # ones that led somewhere.
        on_path = set()
        for pth in paths:
            for k in range(len(pth) - 1):
                on_path.add((round(pth[k][0], 1), round(pth[k][1], 1)))

        for nd in nodes:
            a = to_px(nd[0], nd[1], w)
            b = to_px(nd[2], nd[3], w)
            key = (round(nd[0], 1), round(nd[1], 1))
            col = (150, 30, 36)                       # dead end
            if key in on_path:
                col = (70, 240, 110)                  # on a path
            pygame.draw.line(screen, col, a, b, 1)

        if nodes:
            last = nodes[-1]
            pygame.draw.line(screen, (255, 235, 90),
                             to_px(last[0], last[1], w),
                             to_px(last[2], last[3], w), 2)
            hx, hz = to_px(last[2], last[3], w)
            pygame.draw.line(screen, (255, 255, 255), (hx - 8, hz), (hx + 8, hz), 1)
            pygame.draw.line(screen, (255, 255, 255), (hx, hz - 8), (hx, hz + 8), 1)

        for pt, col, lab in ((start, (0, 200, 255), "START  team 1 base"),
                             (goal, (255, 140, 0), "FLAG  team 2 base")):
            px_, pz_ = to_px(pt[0], pt[1], w)
            pygame.draw.circle(screen, col, (px_, pz_),
                               max(4, int(50.0 / (g["wx1"] - g["wx0"]) * w)), 2)
            tag = font.render(f"{lab}  ({pt[0]:.0f}, {pt[1]:.0f})", True, col)
            screen.blit(tag, (px_ + 14, pz_ - 8))

        msg = (f"rays {rays}   paths {len(paths)}   hull {hull:.1f} m"
               f"   {'DONE' if done else ('PAUSED' if paused else 'hunting')}"
               f"    [space] pause  [r] restart  [tab] swap ends  [q] quit")
        screen.blit(font.render(msg, True, (255, 255, 255)), (8, 8))
        pygame.display.flip()
        pygame.time.wait(16 if (done or paused) else delay)

    pygame.quit()


if __name__ == "__main__":
    main()
