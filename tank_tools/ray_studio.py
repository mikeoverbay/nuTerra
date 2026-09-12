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
RING_STEP = np.deg2rad(12.0)
RING_MAX = 13
MIN_PROGRESS_M = 8.0
NEW_GROUND_M = 18.0
RAY_BUDGET = 4000

# How far round an obstacle to follow before admitting this side does not go
# round it. Longer than any single building or rock band on a map; short enough
# that a ray cannot circumnavigate the whole world looking for a view.
CORNER_MAX_M = 320.0

# How far a ray at the flag must travel from a candidate leave point before the
# corner counts as rounded. Short enough that a real gap qualifies, long enough
# that shuffling one cell sideways does not.
LEAVE_M = 45.0

# How far to bother looking when testing that. Beyond this the answer stops
# changing and the march is just expensive.
LEAVE_LOOK_M = 160.0
PATH_MAX = 12


def hunt(g, start, goal, open_angle=0.0):
    """The owner's algorithm, as a GENERATOR so it can be watched.

    Yields after every ray, which is what makes this live rather than a
    picture of an answer: the window draws whatever has happened so far and
    the search carries on where it left off.
    """
    sx, sz = snap_free(g, *start)
    gx, gz = snap_free(g, *goal)
    limit = (g["wx1"] - g["wx0"]) * 1.5

    nodes = []          # (x0,z0, x1,z1, parent, reached)
    openq = []
    been = set()

    def bucket(x, z):
        return (int((x - g["wx0"]) / NEW_GROUND_M),
                int((z - g["wz0"]) / NEW_GROUND_M))

    def cast(x, z, dx, dz, parent, cost):
        travelled, hx, hz, reached = march(g, x, z, dx, dz, (gx, gz), limit)
        nodes.append([x, z, hx, hz, parent, reached])
        openq.append((len(nodes) - 1, cost + travelled))
        return len(nodes) - 1

    d = np.hypot(gx - sx, gz - sz)
    # The OPENING ANGLE is how the sweep aims this hunt. Zero is straight at
    # the flag; the sweep walks it left to right so each hunt sets off into
    # different ground rather than all of them starting down the same line.
    ca, sa = np.cos(open_angle), np.sin(open_angle)
    ux, uz = (gx - sx) / d, (gz - sz) / d
    cast(sx, sz, ux * ca - uz * sa, ux * sa + uz * ca, -1, 0.0)
    been.add(bucket(sx, sz))
    paths = []
    rays = 1
    yield nodes, paths, rays

    while openq and rays < RAY_BUDGET and not paths:
        # Best-first: the branch nearest the flag for what it has spent.
        bi = min(range(len(openq)),
                 key=lambda k: openq[k][1] +
                 np.hypot(gx - nodes[openq[k][0]][2], gz - nodes[openq[k][0]][3]))
        ni, cost = openq.pop(bi)
        n = nodes[ni]

        if n[5]:
            chain, k = [], ni
            while k >= 0:
                chain.append((nodes[k][2], nodes[k][3]))
                chain.append((nodes[k][0], nodes[k][1]))
                k = nodes[k][4]
            paths.append(list(reversed(chain)))
            yield nodes, paths, rays
            continue

        hx, hz = n[2], n[3]
        bdx, bdz = n[2] - n[0], n[3] - n[1]
        bl = np.hypot(bdx, bdz)
        if bl < 1e-6:
            bdx, bdz, bl = gx - hx, gz - hz, max(np.hypot(gx - hx, gz - hz), 1e-6)
        bdx, bdz = bdx / bl, bdz / bl

        # GO AROUND IT. This is the part that was missing and it is the whole
        # difference between a resolver and a screensaver.
        #
        # The old version picked an angle that "went somewhere" and then flew
        # off down it until it hit the next thing. Nothing about that goes
        # AROUND an obstacle - it just scatters, which is exactly what it
        # looked like: "ever see that screen saver that draws ramdom lines..
        # that this. We have goals. we are stopping at the first hit. we dot
        # try and go around at all."
        #
        # Rounding a corner means following the obstacle's edge until the flag
        # is VISIBLE, and then going straight at it. So: step along the tangent
        # and, every few steps, ask whether a clear line to the goal has opened
        # up. The first place it has is where the corner ends. That point is the
        # branch, and its ray is aimed at the FLAG - not off into the map.
        #
        # A side that never opens a line within CORNER_MAX_M has not gone round
        # anything; it is wandering, and it is dropped.
        spawned = 0
        for sgn in (1.0, -1.0):                 # LEFT first, as asked
            found = None
            for ring in range(1, RING_MAX + 1):
                a = RING_STEP * ring * sgn
                ca, sa = np.cos(a), np.sin(a)
                dx2, dz2 = bdx * ca - bdz * sa, bdx * sa + bdz * ca
                ox, oz = hx + dx2 * g["cell_m"] * 1.5, hz + dz2 * g["cell_m"] * 1.5
                if not standable(g, ox, oz):
                    continue

                # Walk out along this tangent until heading AT THE FLAG makes
                # progress again. That is the leave point.
                #
                # The first rule tried here was "until you can SEE the flag",
                # which is right for one rock in an empty field and useless on a
                # map: the flag is 800 m away through a monastery and you can
                # essentially never see it. Every side failed and the resolve
                # cast one ray and stopped.
                #
                # What ends a corner is not seeing the goal, it is getting PAST
                # the thing - and the test for that is whether a ray at the flag
                # now travels a decent distance where a moment ago it travelled
                # nothing.
                step = g["cell_m"] * 4.0
                t = 0.0
                px, pz = ox, oz
                while t < CORNER_MAX_M:
                    if not standable(g, px, pz):
                        break
                    gd = max(np.hypot(gx - px, gz - pz), 1e-6)
                    got, _, _, _ = march(g, px, pz, (gx - px) / gd, (gz - pz) / gd,
                                         (gx, gz), LEAVE_LOOK_M)
                    if got >= LEAVE_M:
                        found = (px, pz)
                        break
                    px, pz = px + dx2 * step, pz + dz2 * step
                    t += step
                if found:
                    break

            if not found:
                continue
            ex, ez = found
            b = bucket(ex, ez)
            if b in been:
                continue
            been.add(b)
            # Aimed at the FLAG from where the corner ended.
            d2 = max(np.hypot(gx - ex, gz - ez), 1e-6)
            cast(ex, ez, (gx - ex) / d2, (gz - ez) / d2, ni, cost)
            rays += 1
            spawned += 1
            yield nodes, paths, rays

        if spawned == 0:
            yield nodes, paths, rays

    yield nodes, paths, rays


# The opening fan: how wide the sweep looks and in what steps. Left first.
SWEEP_DEG = 75.0
SWEEP_STEP_DEG = 7.5

# Two solutions whose rays run this close together are the same way in wearing
# two hats. The owner's rule: "any rays that solve with close rays are
# rejected."
REJECT_M = 55.0

# How wide a solved corridor is taken off the map, and how much ground around
# each base is spared so the next hunt can still get out of the gate.
USED_W_M = 26.0
GUARD_M = 45.0

# Ground is only "used up" where its clearance is at least this many hull radii
# - anywhere tighter is a mandatory gap shared by every route through it.
PINCH_SPARE = 1.6


def resample(path, n=24):
    """A path as n points evenly along its length, so two of different shapes
    can be compared point for point."""
    if len(path) < 2:
        return [path[0]] * n if path else []
    segs, total = [], 0.0
    for i in range(1, len(path)):
        d = np.hypot(path[i][0] - path[i - 1][0], path[i][1] - path[i - 1][1])
        segs.append(d)
        total += d
    if total < 1e-6:
        return [path[0]] * n
    out, want, acc, i = [], 0.0, 0.0, 1
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
    """Is this the same way in as something already solved?

    Compared as SHAPES, resampled to the same count, so a route with 16 turns
    and one with 9 can still be recognised as the same corridor. The mean
    separation is used rather than the worst: two routes that share a corridor
    and differ only where they leave it are still the same corridor.
    """
    a = resample(path)
    for other in pool:
        b = resample(other)
        dsum = sum(np.hypot(p[0] - q[0], p[1] - q[1]) for p, q in zip(a, b))
        if dsum / len(a) < REJECT_M:
            return True
    return False


def mark_used(g, path, start, goal):
    """Take a solved corridor off the map.

    The owner: "and that found a solution.. save it our pool of solved paths.
    That one is done shoot ray right and move on."

    DONE means done - the ground it used is not available to the next hunt.
    Without this every opening angle in the sweep curves back onto the same
    corridor the moment the corner logic re-aims at the flag, and 2,130 rays
    return one route twenty-one times.

    The ENDS ARE SPARED. Both bases sit in tight ground, so marking there would
    wall the next hunt in before it started.
    """
    if g["used"] is None:
        g["used"] = np.zeros_like(g["blocked"])
    N, cm = g["N"], g["cell_m"]
    # Wide enough that the next hunt cannot simply run alongside; capped so a
    # pass over open ground does not consume the field.
    r_cells = max(1, int(min(USED_W_M, 40.0) / cm))
    guard2 = (GUARD_M / cm) ** 2
    scx, scz = world_to_cell(g, *start)
    gcx, gcz = world_to_cell(g, *goal)
    step = cm * 0.5
    for i in range(1, len(path)):
        ax, az = path[i - 1]
        bx, bz = path[i]
        d = np.hypot(bx - ax, bz - az)
        if d < 1e-6:
            continue
        ux, uz = (bx - ax) / d, (bz - az) / d
        t = 0.0
        while t <= d:
            cx, cz = world_to_cell(g, ax + ux * t, az + uz * t)
            t += step
            if not (0 <= cx < N and 0 <= cz < N):
                continue
            if (cx - scx) ** 2 + (cz - scz) ** 2 < guard2:
                continue
            if (cx - gcx) ** 2 + (cz - gcz) ** 2 < guard2:
                continue
            # A PINCH IS NEVER TAKEN OFF THE MAP. You can only use up ground
            # that had room for an alternative; where a corridor is barely
            # wider than the hull there was never a second way through it, and
            # marking it does not retire a route, it seals the map.
            #
            # Measured: both ways into team 2's base leave team 1's through the
            # SAME 8.5 m pinch at (50, -390). Marking the first corridor at 26 m
            # closed it, and the remaining twenty sweep angles found nothing at
            # all - not because the map has one route, but because the gate had
            # been welded shut behind the first one.
            if g["clear"][cz, cx] < g["hull"] * PINCH_SPARE:
                continue
            x0, x1 = max(0, cx - r_cells), min(N, cx + r_cells + 1)
            z0, z1 = max(0, cz - r_cells), min(N, cz + r_cells + 1)
            g["used"][z0:z1, x0:x1] = True


def resolve(g, start, goal):
    """Sweep the opening ray left to right and collect every DISTINCT way in.

    The owner's rule, in his words: "and that found a solution.. save it our
    pool of solved paths. That one is done shoot ray right and move on. any
    rays that solve with close rays are rejected."

    So a solve is not the end of the resolve, it is one entry. The sweep keeps
    going rightward, and a later hunt that lands on the same corridor is thrown
    away rather than stored twice - which is what makes the pool a list of the
    map's actual ways in rather than a list of how many times we looked.
    """
    pool, all_nodes = [], []
    g["used"] = None                            # a fresh resolve sees a whole map
    angles = np.arange(SWEEP_DEG, -SWEEP_DEG - 0.1, -SWEEP_STEP_DEG)  # LEFT first
    for a_deg in angles:
        got = None
        for nodes, paths, rays in hunt(g, start, goal, np.deg2rad(a_deg)):
            all_nodes = all_nodes[:len(all_nodes)] + nodes
            if paths:
                got = paths[0]
            yield all_nodes, pool + ([got] if got else []), len(all_nodes)
            if got:
                break
        if got is None:
            continue
        if too_close(got, pool):
            continue                      # same way in, wearing a different hat
        pool.append(got)
        mark_used(g, got, start, goal)          # that one is done
        yield all_nodes, pool, len(all_nodes)
    yield all_nodes, pool, len(all_nodes)


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

        for pt, col in ((start, (0, 200, 255)), (goal, (255, 140, 0))):
            pygame.draw.circle(screen, col, to_px(pt[0], pt[1], w),
                               max(4, int(50.0 / (g["wx1"] - g["wx0"]) * w)), 2)

        msg = (f"rays {rays}   paths {len(paths)}   hull {hull:.1f} m"
               f"   {'DONE' if done else ('PAUSED' if paused else 'hunting')}"
               f"    [space] pause  [r] restart  [q] quit")
        screen.blit(font.render(msg, True, (255, 255, 255)), (8, 8))
        pygame.display.flip()
        pygame.time.wait(16 if (done or paused) else delay)

    pygame.quit()


if __name__ == "__main__":
    main()
