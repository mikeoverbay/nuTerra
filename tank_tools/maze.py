"""The maze-solver's answer: flood fill from the goal, then walk downhill.

WHY THIS EXISTS. The ray planner has been tuned all day against nothing. Every
change was measured against the previous run, and the noise floor on this map
is enormous - 0.25 degrees of ray spacing once took the same configuration from
2,455 m to 5,655 m. You cannot tell a real improvement from a reroll that way,
and the owner's verdict was fair: "This is going no where."

Maze solving has the missing piece. Flood fill - the micromouse algorithm, Lee's
algorithm, BFS from the goal - labels every reachable cell with its true
distance to the goal, and then the shortest route is just walking downhill. It
is exact, it has no parameters at all, and there is nothing to tune.

So this is NOT a competitor to the ray planner. It is the ruler. It answers:

  * what does the shortest DRIVABLE route actually cost, against the 785 m
    straight line that ignores the terrain
  * therefore, is the ray planner's answer 1.1x the optimum or 3x it
  * and which parts of the map the planner is going the long way round

THE SAME COLLISION MAP, or the comparison is worthless. The planner collides
against `collide_hull` - the obstacle map grown by half a hull, so the query is
"can the tank's CENTRE be here". This downsamples that exact array to one metre,
blocking a metre cell if any texel inside it is blocked. Conservative, and
conservative in the direction that makes the ruler honest: the optimum reported
here can only be longer than the true optimum, never shorter, so a planner that
beats it has found something wrong rather than something clever.

    python tank_tools/maze.py [map] [--to X,Z] [--out route.png]
"""

import os
import sys

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from tank_tools import ray_studio as rs

CELL_M = 1.0


def grid_1m(g, cell_m=CELL_M):
    """collide_hull, downsampled to one-metre cells. True means blocked.

    A cell is blocked if ANY texel in it is - max pooling, not averaging.
    Averaging would open a gap the tank cannot fit through, which is the whole
    failure this is meant to be a check against.
    """
    W = g["W"]
    span = g["wx1"] - g["wx0"]
    n = int(round(span / cell_m))
    # The texel grid does not divide evenly by the metre grid (5.851 texels to
    # the metre on this bake), and a fixed stride is exactly the bug that put
    # two grids 200 m apart earlier today. So index by WORLD COORDINATE.
    rows = np.clip(((np.arange(n) + 0.5) * cell_m / span * W).astype(int), 0, W - 1)
    cols = rows
    # Max-pool by taking the block maximum over the texels each cell covers.
    edges = np.clip((np.arange(n + 1) * cell_m / span * W).astype(int), 0, W)
    hull = g["collide_hull"]
    blocked = np.zeros((n, n), bool)
    for r in range(n):
        r0, r1 = edges[r], max(edges[r] + 1, edges[r + 1])
        band = hull[r0:r1, :]
        if band.shape[0] == 0:
            continue
        rowmax = band.any(axis=0)
        # column pooling, vectorised with reduceat
        blocked[r] = np.maximum.reduceat(rowmax, edges[:n])
    return blocked, n


def to_cell(g, x, z, n, cell_m=CELL_M):
    """World XZ -> (row, col). Rows count DOWN from the north edge, as the
    bake and the square map both do - getting this backwards produced a map
    that agreed with itself 77% of the time and looked almost right."""
    c = int((x - g["wx0"]) / cell_m)
    r = int((g["wz1"] - z) / cell_m)
    return max(0, min(n - 1, r)), max(0, min(n - 1, c))


def to_world(g, r, c, cell_m=CELL_M):
    return (g["wx0"] + (c + 0.5) * cell_m, g["wz1"] - (r + 0.5) * cell_m)


def nearest_free(blocked, rc, limit=40):
    """The closest free cell to this one, and how far it had to go.

    Both bases need it. A metre cell is blocked if ANY texel in it is, and both
    team marks sit within a metre of their own base building's hull margin - the
    same building that stops the first ray of every search - so the exact goal
    cell reads solid while the texel under the mark is free. Failing there would
    be the pooling reporting its own conservatism as "no route".
    """
    n = blocked.shape[0]
    r0, c0 = rc
    if not blocked[r0, c0]:
        return (r0, c0), 0.0
    for rad in range(1, limit):
        r1, r2 = max(0, r0 - rad), min(n, r0 + rad + 1)
        c1, c2 = max(0, c0 - rad), min(n, c0 + rad + 1)
        sub = blocked[r1:r2, c1:c2]
        free = np.argwhere(~sub)
        if len(free):
            d = np.hypot(free[:, 0] + r1 - r0, free[:, 1] + c1 - c0)
            k = int(d.argmin())
            return (int(free[k, 0] + r1), int(free[k, 1] + c1)), float(d[k])
    raise ValueError("no free cell within %d of %s" % (limit, rc))


def flood(blocked, goal_rc, cost=None):
    """Exact distance from every free cell to the goal, octile metric.

    scipy's Dijkstra over an 8-connected grid graph. Diagonals cost sqrt(2) and
    are refused when both orthogonal neighbours are blocked, so a route cannot
    squeeze through the corner where two walls touch - which a plain
    8-connected BFS happily does, and a tank cannot.
    """
    from scipy.sparse import coo_matrix
    from scipy.sparse.csgraph import dijkstra

    n = blocked.shape[0]
    free = ~blocked
    idx = -np.ones((n, n), np.int64)
    idx[free] = np.arange(free.sum())
    N = int(free.sum())

    rows, cols, vals = [], [], []
    steps = ((-1, 0, 1.0), (1, 0, 1.0), (0, -1, 1.0), (0, 1, 1.0),
             (-1, -1, np.sqrt(2)), (-1, 1, np.sqrt(2)),
             (1, -1, np.sqrt(2)), (1, 1, np.sqrt(2)))
    for dr, dc, w in steps:
        a = free[max(0, -dr):n - max(0, dr), max(0, -dc):n - max(0, dc)]
        b = free[max(0, dr):n - max(0, -dr), max(0, dc):n - max(0, -dc)]
        ok = a & b
        if dr and dc:
            # NO CORNER CUTTING. Both orthogonals must be free too.
            o1 = free[max(0, -dr):n - max(0, dr), max(0, dc):n - max(0, -dc)]
            o2 = free[max(0, dr):n - max(0, -dr), max(0, -dc):n - max(0, dc)]
            ok = ok & o1 & o2
        ia = idx[max(0, -dr):n - max(0, dr), max(0, -dc):n - max(0, dc)][ok]
        ib = idx[max(0, dr):n - max(0, -dr), max(0, dc):n - max(0, -dc)][ok]
        rows.append(ia)
        cols.append(ib)
        if cost is None:
            vals.append(np.full(ia.shape, w, np.float32))
        else:
            # PER-CELL COST, so ground can be made EXPENSIVE without being made
            # impassable. That distinction is the whole trick for route
            # variety: blocking a corridor a previous lane used can cut the map
            # in half, while charging four times to drive it sends the next
            # lane round if there IS a round, and lets it share the road if
            # there is not.
            cb = cost[max(0, dr):n - max(0, -dr), max(0, dc):n - max(0, -dc)][ok]
            vals.append((w * cb).astype(np.float32))

    graph = coo_matrix((np.concatenate(vals),
                        (np.concatenate(rows), np.concatenate(cols))),
                       shape=(N, N)).tocsr()
    src = idx[goal_rc]
    if src < 0:
        raise ValueError("the goal cell is blocked")
    d = dijkstra(graph, indices=src, directed=False)
    field = np.full((n, n), np.inf, np.float32)
    field[free] = d
    return field


def walk_down(field, start_rc):
    """Downhill from the start: the shortest route, one cell at a time."""
    n = field.shape[0]
    r, c = start_rc
    if not np.isfinite(field[r, c]):
        raise ValueError("the start cell is blocked or cut off from the goal")
    out = [(r, c)]
    for _ in range(n * 4):
        best, br, bc = field[r, c], r, c
        for dr in (-1, 0, 1):
            for dc in (-1, 0, 1):
                nr, nc = r + dr, c + dc
                if 0 <= nr < n and 0 <= nc < n and field[nr, nc] < best:
                    best, br, bc = field[nr, nc], nr, nc
        if (br, bc) == (r, c):
            break                      # at the goal, or in a flat spot
        r, c = br, bc
        out.append((r, c))
    return out


def solve(g, start, goal, cell_m=CELL_M):
    """The shortest drivable route, and what it cost to find."""
    import time
    t0 = time.time()
    blocked, n = grid_1m(g, cell_m)
    t1 = time.time()
    gr, gsnap = nearest_free(blocked, to_cell(g, goal[0], goal[1], n, cell_m))
    sr, ssnap = nearest_free(blocked, to_cell(g, start[0], start[1], n, cell_m))
    field = flood(blocked, gr)
    t2 = time.time()
    cells = walk_down(field, sr)
    pts = [to_world(g, r, c, cell_m) for r, c in cells]
    length = float(sum(np.hypot(pts[k + 1][0] - pts[k][0],
                                pts[k + 1][1] - pts[k][1])
                       for k in range(len(pts) - 1)))
    return dict(pts=pts, field=field, blocked=blocked, n=n, length=length,
                grid_s=t1 - t0, flood_s=t2 - t1, snap=(ssnap, gsnap),
                reach=float(np.isfinite(field).mean()))


def main():
    map_name = "19_monastery"
    to = None
    out = None
    for i, a in enumerate(sys.argv):
        if a == "--to" and i + 1 < len(sys.argv):
            to = tuple(float(v) for v in sys.argv[i + 1].split(","))
        if a == "--out" and i + 1 < len(sys.argv):
            out = sys.argv[i + 1]
        if i == 1 and not a.startswith("-"):
            map_name = a

    g = rs.build_grid(map_name, 4.5)
    S = rs.snap_free(g, -20.1, -387.8)
    G = rs.snap_free(g, *(to if to else (0.4, 397.4)))
    direct = np.hypot(G[0] - S[0], G[1] - S[1])

    r = solve(g, S, G)
    print("FLOOD FILL, the exact shortest drivable route")
    print("  grid      %d x %d at %.1f m, %.1f%% of it reachable from the goal"
          % (r["n"], r["n"], CELL_M, 100 * r["reach"]))
    print("  direct    %.0f m" % direct)
    print("  OPTIMUM   %.0f m = %.2fx direct   (%d cells)"
          % (r["length"], r["length"] / direct, len(r["pts"])))
    print("  cost      %.1f s to build the grid, %.1f s to flood it"
          % (r["grid_s"], r["flood_s"]))
    if max(r["snap"]) > 0:
        print("  snapped   start %.1f m, goal %.1f m to the nearest free cell "
              "- both marks sit inside their own base building's hull margin"
              % r["snap"])
    if out:
        from tank_tools.draw_path import render
        try:
            squares = rs.Squares(map_name)
        except Exception:
            squares = None
        render(g, squares, r["pts"], [], out)
        print("  drawn     %s" % out)
    return 0


if __name__ == "__main__":
    sys.exit(main())


# ==========================================================================
# LANES: one route per tank, and none of them the same road
# ==========================================================================
#
# The owner, after seeing every hull take the one optimal corridor:
#
#   "can't have them running to the same route. Here is the plan: start left
#    and seek same location on opposite side same X. draw a ring there 20 m and
#    if we land there that path is done, save it. seek from that location to
#    our base and hook it. If the 2nd to home fails we probably started first
#    path where we can't get out so throw it out and move over towards base."
#
# So a lane is a CROSSING at a fixed X - straight over the map to the mirror of
# where it started - hooked to a second leg that runs from there to the base.
# Fixing X per tank is what makes the routes different: they are spread across
# the map by construction rather than by hoping a search finds variety.
#
# TWO FLOODS FOR THE WHOLE FAMILY, and that is the point of doing it this way.
# A flood from the START gives the route to ANY crossing point; a flood from the
# BASE gives the route from ANY crossing point onwards. So every lane is two
# lookups and a walk downhill, not two searches - N lanes cost the same 1.2 s
# as one.
#
# WHAT HIS RULE DOES NOT COVER, and both happen on monastery:
#
#   * LEG ONE can fail too, not just leg two. A lane whose X runs into the
#     river never reaches the far side at all. Same remedy - shift towards the
#     base - so it is folded into the same test.
#   * TWO LANES CAN MERGE after the hook, because both legs end at the same
#     base and the last few hundred metres are forced. Lanes are reported with
#     the fraction of cells they share with the nearest lane already kept, so
#     "different route" can be a measured claim rather than an assumption.

RING_M = 20.0


def lane_routes(g, start, goal, count=None, cell_m=CELL_M, ring_m=RING_M,
                step_m=5.0):
    """One crossing lane per tank, each at its own X, hooked to the base."""
    blocked, n = grid_1m(g, cell_m)
    s_rc, _ = nearest_free(blocked, to_cell(g, start[0], start[1], n, cell_m))
    g_rc, _ = nearest_free(blocked, to_cell(g, goal[0], goal[1], n, cell_m))
    f_start = flood(blocked, s_rc)      # cost from the start to anywhere
    f_goal = flood(blocked, g_rc)       # cost from anywhere to the base

    # THE CROSSING LINE: the mirror of the start's row, so a lane runs the full
    # depth of the map.
    #
    # AND IT STARTS AT THE MAP CORNER. "X needs to start way over from base at
    # map corner." The first version spread `count` lanes evenly over nine
    # tenths of the half-map, which put the outermost one short of the edge and
    # spent half the budget on lanes the river kills. This walks IN from the
    # edge one step at a time and keeps the ones that work, which is the
    # owner's own fallback rule applied from the start: a lane that fails moves
    # over towards the base, so failures simply advance the march instead of
    # consuming a slot.
    cross_row = n - 1 - s_rc[0]
    base_col = g_rc[1]
    ring_cells = int(ring_m / cell_m)
    edge = 5                              # off the very rim, which is outland
    inward = 1 if base_col > edge else -1
    # FIVE METRES AT A TIME. "and we step x over to the right 5m and try again."
    # Every step is two field lookups and two downhill walks, not two searches,
    # so sweeping the whole half-map at 5 m costs about as much as one search
    # used to. count=None means go all the way to the base's own column.
    step = max(1, int(round(step_m / cell_m)))

    out = []
    col = edge
    while (col - base_col) * inward < 0:
        rc = (cross_row, max(0, min(n - 1, col)))
        col += step * inward
        if count is not None and sum(1 for L in out if L["ok"]) >= count:
            break
        # THE 20 M RING. The crossing point is a target, not an address: if the
        # exact cell is solid, anywhere in the ring counts as landing there.
        here = rc[1]
        try:
            rc, snapped = nearest_free(blocked, rc, limit=ring_cells)
        except ValueError:
            out.append(dict(col=here, ok=False, why="nothing free within the ring"))
            continue
        d1, d2 = f_start[rc], f_goal[rc]
        if not np.isfinite(d1):
            # leg one never gets there - the owner's remedy, one lane over
            out.append(dict(col=here, ok=False, why="leg 1 cannot reach the crossing"))
            continue
        if not np.isfinite(d2):
            # "if the 2nd to home fails ... throw it out and move over"
            out.append(dict(col=here, ok=False, why="leg 2 cannot reach the base"))
            continue
        leg1 = walk_down(f_start, rc)[::-1]      # start -> crossing
        leg2 = walk_down(f_goal, rc)             # crossing -> base
        cells = leg1 + leg2[1:]
        pts = [to_world(g, r, c, cell_m) for r, c in cells]
        length = float(sum(
            np.hypot(pts[i + 1][0] - pts[i][0], pts[i + 1][1] - pts[i][1])
            for i in range(len(pts) - 1)))
        out.append(dict(col=here, ok=True, rc=rc, pts=pts, cells=set(cells),
                        length=length, snapped=snapped,
                        cross=to_world(g, rc[0], rc[1], cell_m)))

    # HOW DIFFERENT ARE THEY REALLY?
    #
    # Measured OUTSIDE the ends, and that is not a kindness to the numbers. The
    # last stretch into the base and the first out of it are common to every
    # route that exists - there is one way through the base's own gap - so
    # counting them makes two genuinely separate crossings look like the same
    # road. The guard is the same END_GUARD_M the route planner already spares
    # when it stamps a corridor, for the same reason.
    guard = 45.0 / cell_m
    def middle(cells, anchor_rc, other_rc):
        return set((r, c) for r, c in cells
                   if np.hypot(r - anchor_rc[0], c - anchor_rc[1]) > guard
                   and np.hypot(r - other_rc[0], c - other_rc[1]) > guard)

    kept = []
    for lane in out:
        if not lane["ok"]:
            continue
        mid = middle(lane["cells"], s_rc, g_rc)
        lane["mid"] = mid
        best = 0.0
        for prev in kept:
            share = len(mid & prev["mid"]) / max(1, len(mid))
            best = max(best, share)
        lane["overlap"] = best
        kept.append(lane)
    return out, blocked, n


def spread_routes(g, start, goal, count=6, cell_m=CELL_M, penalty=6.0):
    """Routes that are actually different, by charging for reused ground.

    The lane scheme does what it says - a crossing per tank, at its own X - but
    measured on monastery the lanes still overlap 56-97% in the middle, because
    the map has two or three ways through and fixing the crossing point does
    not create a fourth. Different starts, same corridor.

    So this asks for the thing the owner actually wants - "can't have them
    running to the same route" - directly: flood, keep the route, make the
    ground it used COST more, flood again. Expensive, not blocked, because
    blocking a corridor can disconnect the map while charging for it only sends
    the next tank round when a round exists.

    Costs one flood per route, so about half a second each.
    """
    blocked, n = grid_1m(g, cell_m)
    s_rc, _ = nearest_free(blocked, to_cell(g, start[0], start[1], n, cell_m))
    g_rc, _ = nearest_free(blocked, to_cell(g, goal[0], goal[1], n, cell_m))
    cost = np.ones((n, n), np.float32)
    guard = 45.0 / cell_m
    out = []
    for k in range(count):
        field = flood(blocked, g_rc, None if k == 0 else cost)
        if not np.isfinite(field[s_rc]):
            out.append(dict(ok=False, why="no route left once the used ground is charged for"))
            break
        cells = walk_down(field, s_rc)
        pts = [to_world(g, r, c, cell_m) for r, c in cells]
        length = float(sum(
            np.hypot(pts[i + 1][0] - pts[i][0], pts[i + 1][1] - pts[i][1])
            for i in range(len(pts) - 1)))
        mid = set((r, c) for r, c in cells
                  if np.hypot(r - s_rc[0], c - s_rc[1]) > guard
                  and np.hypot(r - g_rc[0], c - g_rc[1]) > guard)
        share = max([len(mid & p["mid"]) / max(1, len(mid))
                     for p in out if p.get("ok")] or [0.0])
        out.append(dict(ok=True, pts=pts, cells=cells, mid=mid,
                        length=length, overlap=share))
        # CHARGE FOR IT, in a band the width of a tank rather than one cell -
        # a single-cell line is stepped around for nothing and the next route
        # comes back identical one metre over.
        rr = np.array([c[0] for c in cells])
        cc = np.array([c[1] for c in cells])
        for dr in range(-3, 4):
            for dc in range(-3, 4):
                cost[np.clip(rr + dr, 0, n - 1), np.clip(cc + dc, 0, n - 1)] = penalty
    return out, blocked, n


def components(blocked):
    """A label per free cell: two cells with the same label can reach each
    other. 8-connected with the same no-corner-cutting rule as the flood."""
    from scipy.sparse import coo_matrix
    from scipy.sparse.csgraph import connected_components
    n = blocked.shape[0]
    free = ~blocked
    idx = -np.ones((n, n), np.int64)
    idx[free] = np.arange(free.sum())
    rows, cols = [], []
    for dr, dc in ((-1, 0), (0, -1), (-1, -1), (-1, 1)):
        a = free[max(0, -dr):n - max(0, dr), max(0, -dc):n - max(0, dc)]
        b = free[max(0, dr):n - max(0, -dr), max(0, dc):n - max(0, -dc)]
        ok = a & b
        if dr and dc:
            o1 = free[max(0, -dr):n - max(0, dr), max(0, dc):n - max(0, -dc)]
            o2 = free[max(0, dr):n - max(0, -dr), max(0, -dc):n - max(0, dc)]
            ok = ok & o1 & o2
        rows.append(idx[max(0, -dr):n - max(0, dr), max(0, -dc):n - max(0, dc)][ok])
        cols.append(idx[max(0, dr):n - max(0, -dr), max(0, dc):n - max(0, -dc)][ok])
    r, c = np.concatenate(rows), np.concatenate(cols)
    gph = coo_matrix((np.ones(len(r), np.int8), (r, c)),
                     shape=(int(free.sum()),) * 2).tocsr()
    _, lab = connected_components(gph, directed=False)
    out = -np.ones((n, n), np.int64)
    out[free] = lab
    return out


def corner_sweep(g, goal, cell_m=CELL_M, ring_m=RING_M, step_m=5.0,
                 margin_m=20.0, axis="ns"):
    """Straight lanes across the map at a fixed X, swept from the back corner.

    "thats not starting in the back corner and working across."

    Right - lane_routes moved the CROSSING target across the map but left the
    start pinned at the tank's own base, so every lane was a fan out of one
    point rather than a set of parallel roads. This is the scheme as described:

      * a lane IS a line of constant X, from our back edge to the opposite one
      * the sweep starts at the BACK CORNER - the extreme X - and works across
        in 5 m steps
      * leg one is the straight run down the lane, because "seek same location
        on opposite side same X" is a drive, not a search. Blocked means the
        lane fails, and a failed lane steps over rather than searching round
      * the far end has a 20 m ring: land anywhere inside it and leg one is done
      * leg two hooks that landing point to the base, from the one flood
      * if leg two cannot get home the lane is thrown out and we step over

    One flood total, from the base. Everything else is line tests.
    """
    blocked, n = grid_1m(g, cell_m)
    g_rc, _ = nearest_free(blocked, to_cell(g, goal[0], goal[1], n, cell_m))
    f_goal = flood(blocked, g_rc)
    from scipy.sparse import coo_matrix
    from scipy.sparse.csgraph import connected_components
    ring_cells = int(ring_m / cell_m)
    m = int(margin_m / cell_m)
    step = max(1, int(round(step_m / cell_m)))
    # WHICH WAY THE LANES RUN.
    #
    #   ns   lanes at constant X, crossing north-south, swept west to east
    #   we   lanes at constant Z, crossing west-east, swept north to south
    #
    # Same scheme either way - a lane is a line, both its ends on that line,
    # and the sweep starts at the far corner and steps across. Done by working
    # in (along, across) and only converting to (row, col) at the two places it
    # matters, so there is one implementation rather than a transposed copy
    # that drifts out of step with it.
    ns = (axis == "ns")

    def rc_of(across, along):
        return (along, across) if ns else (across, along)

    # WHICH END IS THE TARGET, and for a west-east sweep the honest answer is
    # "whichever one can get home".
    #
    # North-south the base is at one end of the lane, so crossing to the
    # opposite side has an obvious direction. West-east it is at NEITHER - the
    # base sits in the middle horizontally - and the first version picked the
    # west edge for all of them and reported 34 lanes of "leg 2 cannot reach
    # the base". That was not a bug, it was correct: the west edge is cut off
    # from our base, as the 5 m sweep measured earlier. The scheme simply had
    # no orientation to pick from.
    #
    # So both ends are candidates, tried nearer-to-the-base first, and the lane
    # takes whichever works. North-south is unchanged by this: the base end is
    # excluded because a lane must CROSS, so only one candidate survives.
    base_along = g_rc[0] if ns else g_rc[1]
    prefer_high = base_along <= n // 2

    # WHICH CELLS CAN REACH WHICH, once. Feasibility for every lane becomes a
    # comparison of two labels instead of a search that fails slowly.
    comp = components(blocked)

    home = comp[g_rc]

    out = []
    for col in range(m, n - m, step):
        # THE LANE SPANS WHAT IS REACHABLE, not the map margins.
        #
        # Measured: the base's drivable component reaches about x = -400 to
        # +700 depending on the row, and never the west edge - the river cuts
        # the western third off entirely. So a west-east lane pinned to the map
        # margins can never exist here, and the first version said so 34 times
        # in a row while looking like a broken sweep. A lane crosses as far as
        # the ground allows, and how far that is IS the answer for that row.
        line = comp[col, :] if not ns else comp[:, col]
        have = np.flatnonzero(line == home)
        if len(have) < 2 or (have[-1] - have[0]) < 4 * ring_cells:
            out.append(dict(col=col, ok=False,
                            why="no usable span of our own ground on this line"))
            continue
        lo_end, hi_end = int(have[0]), int(have[-1])
        ends = ([(hi_end, lo_end), (lo_end, hi_end)] if prefer_high
                else [(lo_end, hi_end), (hi_end, lo_end)])

        lane = None
        why = "nothing free in the ring"
        for near_along, far_along in ends:
            lane, why = _one_lane(g, blocked, comp, f_goal, rc_of, col,
                                  near_along, far_along, ring_cells, cell_m)
            if lane is not None:
                break
        if lane is None:
            out.append(dict(col=col, ok=False, why=why))
            continue
        out.append(lane)
    return out, blocked, n


def _one_lane(g, blocked, comp, f_goal, rc_of, col, near_along, far_along,
              ring_cells, cell_m):
    """One lane, one direction. Returns (lane, None) or (None, why it failed)."""
    if True:
        near_rc_raw = rc_of(col, near_along)
        far_rc_raw = rc_of(col, far_along)
        # LEG ONE IS SOUGHT, NOT DRIVEN STRAIGHT.
        #
        # The first version of this took "seek same location on opposite side
        # same X" literally and tested the straight column. All 272 lanes
        # failed, every one of them, and that is not a bug: monastery is 29%
        # blocked and a 1,360 m line at constant X always meets something. The
        # lane is a DESTINATION at that X, and getting there is the search.
        try:
            rc, snapped = nearest_free(blocked, far_rc_raw, limit=ring_cells)
        except ValueError:
            return None, "nothing free in the ring"
        if not np.isfinite(f_goal[rc]):
            return None, "leg 2 cannot reach the base"
        # Feasible first, by connected component - a lookup - and only then
        # the flood that produces the actual route, so a sweep of 272 lanes
        # pays for the handful that survive rather than for all of them.
        try:
            s_rc, _ = nearest_free(blocked, near_rc_raw, limit=ring_cells)
        except ValueError:
            return None, "nothing free at the lane start"
        if comp[s_rc] != comp[rc]:
            return None, "lane start cut off from the far side"
        leg1 = walk_down(flood(blocked, rc), s_rc)
        leg2 = walk_down(f_goal, rc)
        pts = [to_world(g, r, c, cell_m) for r, c in leg1 + leg2]
        length = float(sum(
            np.hypot(pts[i + 1][0] - pts[i][0], pts[i + 1][1] - pts[i][1])
            for i in range(len(pts) - 1)))
        return dict(col=col, ok=True, pts=pts, length=length,
                    start=to_world(g, s_rc[0], s_rc[1], cell_m),
                    cross=to_world(g, rc[0], rc[1], cell_m)), None


def alternatives(g, start, goal, cell_m=CELL_M, budgets=(0.0, 0.05, 0.10, 0.25,
                                                         0.50, 1.00),
                 guard_m=60.0, min_cells=300):
    """Every other way there was, and what each would have cost.

    "can we know if there was another branch we could of taken?"

    Yes, exactly, and from the two floods already in hand. For any cell,

        through[cell] = dist(start -> cell) + dist(cell -> goal)

    is the length of the best route that passes THROUGH that cell. So the whole
    map is priced at once: `through` minus the optimum is how much a detour via
    that cell costs, in metres, for every cell simultaneously.

    A BRANCH IS A SEPARATE CHANNEL, not a fork in a line. Take every cell whose
    route costs no more than the optimum plus a budget - that is the set of
    ground you could drive and still arrive within budget - and cut off the ends
    both routes must share, since there is exactly one way out of a base. What
    is left falls into connected pieces, and each piece is a genuinely
    different way round: you cannot slide from one to another without paying
    more than the budget. Counting them answers "how many roads are there",
    exactly, rather than by sampling lanes and hoping.

    Reported per budget, so the answer is a curve rather than a number: two
    roads within 10%, three within 50%, and so on.
    """
    from scipy.ndimage import label

    blocked, n = grid_1m(g, cell_m)
    s_rc, _ = nearest_free(blocked, to_cell(g, start[0], start[1], n, cell_m))
    g_rc, _ = nearest_free(blocked, to_cell(g, goal[0], goal[1], n, cell_m))
    f_s = flood(blocked, s_rc)
    f_g = flood(blocked, g_rc)
    through = f_s + f_g
    opt = float(through[s_rc])

    rr, cc = np.mgrid[0:n, 0:n]
    guard = guard_m / cell_m
    ends = ((np.hypot(rr - s_rc[0], cc - s_rc[1]) < guard) |
            (np.hypot(rr - g_rc[0], cc - g_rc[1]) < guard))

    rows = []
    for b in budgets:
        within = np.isfinite(through) & (through <= opt * (1.0 + b)) & ~ends
        lab, k = label(within, structure=np.ones((3, 3), int))
        sizes = np.bincount(lab.ravel())
        real = [i for i in range(1, k + 1) if sizes[i] >= min_cells]
        chans = []
        for i in real:
            m = lab == i
            chans.append(dict(cells=int(sizes[i]),
                              best=float(through[m].min()),
                              # where it sits, so two channels can be told apart
                              cx=float(g["wx0"] + (cc[m].mean() + 0.5) * cell_m),
                              cz=float(g["wz1"] - (rr[m].mean() + 0.5) * cell_m)))
        chans.sort(key=lambda c: c["best"])

        # AND THE REAL ANSWER IS HOLES, NOT PIECES.
        #
        # Counting connected pieces of the within-budget ground says ONE, and
        # it is wrong in the way that matters: the road west of the village and
        # the road east of it are joined by the ground north and south, so they
        # are one connected region while plainly being two different ways to
        # go. Connectivity cannot see that. What separates them is that the
        # village is INSIDE the region - a hole - and you cannot slide a route
        # from one side of a hole to the other without crossing it.
        #
        # So the number of genuinely different ways round is one plus the
        # number of obstacles the affordable ground encloses. Each hole is
        # something you may pass on either side.
        full = np.isfinite(through) & (through <= opt * (1.0 + b))
        holes_lab, hk = label(~full, structure=np.array([[0, 1, 0],
                                                         [1, 1, 1],
                                                         [0, 1, 0]]))
        border = set(np.unique(np.concatenate([
            holes_lab[0, :], holes_lab[-1, :],
            holes_lab[:, 0], holes_lab[:, -1]])))
        holes = []
        hsz = np.bincount(holes_lab.ravel())
        for i in range(1, hk + 1):
            if i in border or hsz[i] < min_cells:
                continue
            m = holes_lab == i
            holes.append(dict(cells=int(hsz[i]),
                              cx=float(g["wx0"] + (cc[m].mean() + 0.5) * cell_m),
                              cz=float(g["wz1"] - (rr[m].mean() + 0.5) * cell_m)))
        holes.sort(key=lambda h: -h["cells"])
        rows.append(dict(budget=b, limit=opt * (1.0 + b), channels=chans,
                         holes=holes, ways=1 + len(holes),
                         ground=float(within.mean())))
    return dict(opt=opt, rows=rows, through=through, blocked=blocked, n=n,
                s_rc=s_rc, g_rc=g_rc)


def regret_along(res, pts, cell_m=CELL_M, g=None):
    """For each point on a route, how much the cheapest alternative costs.

    The per-point form of the same question: standing here, what would the best
    route that does NOT continue the way we went have cost? Reported as extra
    metres over the optimum, so a run of zeros means the route was on the only
    sensible road and a spike means there was a real choice at that spot.
    """
    through, n = res["through"], res["n"]
    out = []
    for (x, z) in pts:
        c = int((x - g["wx0"]) / cell_m)
        r = int((g["wz1"] - z) / cell_m)
        if not (0 <= r < n and 0 <= c < n):
            continue
        out.append(float(through[r, c] - res["opt"]))
    return out


def class_routes(g, start, goal, budget=0.25, cell_m=CELL_M, max_holes=6,
                 min_cells=400):
    """One route per genuinely different way round - the tactical roads.

    "we need to try and make those paths as they are important in the maps
    play."

    alternatives() says HOW MANY different ways there are and what each costs.
    This makes them. For every obstacle the affordable ground encloses - every
    thing you can pass on either side of - it emits the best route down each
    side, so the village, the lake and the ridge each yield a pair.

    THE ROUTES ARE FREE ONCE THE FLOODS ARE DONE. Any cell is a waypoint, and
    a waypoint IS a whole route: walk downhill from it in the start field for
    the way in, downhill in the goal field for the way out, and the cost is
    through[cell], already known. So the work is only choosing WHICH cells -
    one on each side of each hole - and the choosing is a masked argmin.

    SIDES ARE TAKEN ACROSS THE LINE OF TRAVEL, not across the x axis. The two
    ways round something are separated by the perpendicular to the start-goal
    line, so this projects each candidate onto that perpendicular and takes the
    cheapest cell with each sign. On a map where the bases are not north-south
    the x axis would split the wrong way and quietly return the same road
    twice.
    """
    from scipy.ndimage import label, binary_dilation

    blocked, n = grid_1m(g, cell_m)
    s_rc, _ = nearest_free(blocked, to_cell(g, start[0], start[1], n, cell_m))
    g_rc, _ = nearest_free(blocked, to_cell(g, goal[0], goal[1], n, cell_m))
    f_s, f_g = flood(blocked, s_rc), flood(blocked, g_rc)
    through = f_s + f_g
    opt = float(through[s_rc])
    afford = np.isfinite(through) & (through <= opt * (1.0 + budget))

    lab, k = label(~afford, structure=np.array([[0, 1, 0], [1, 1, 1], [0, 1, 0]]))
    border = set(np.unique(np.concatenate([lab[0, :], lab[-1, :],
                                           lab[:, 0], lab[:, -1]])))
    sizes = np.bincount(lab.ravel())
    holes = sorted((i for i in range(1, k + 1)
                    if i not in border and sizes[i] >= min_cells),
                   key=lambda i: -sizes[i])[:max_holes]

    # the perpendicular to the line of travel
    vx, vz = g_rc[1] - s_rc[1], g_rc[0] - s_rc[0]
    L = max(1e-6, np.hypot(vx, vz))
    px, pz = -vz / L, vx / L

    rr, cc = np.mgrid[0:n, 0:n]
    out = []
    for hi in holes:
        m = lab == hi
        skirt = binary_dilation(m, np.ones((7, 7), bool)) & afford
        if not skirt.any():
            continue
        hr, hc = rr[m].mean(), cc[m].mean()
        side = (cc - hc) * px + (rr - hr) * pz
        for sgn, name in ((1.0, "one side"), (-1.0, "the other")):
            sel = skirt & (side * sgn > 0)
            if not sel.any():
                continue
            cost = np.where(sel, through, np.inf)
            r0, c0 = np.unravel_index(int(np.argmin(cost)), cost.shape)
            cells = walk_down(f_s, (r0, c0))[::-1] + walk_down(f_g, (r0, c0))[1:]
            pts = [to_world(g, r, c, cell_m) for r, c in cells]
            out.append(dict(hole=int(hi), hole_cells=int(sizes[hi]), side=name,
                            via=to_world(g, r0, c0, cell_m),
                            length=float(through[r0, c0]),
                            extra=float(through[r0, c0] - opt),
                            pts=pts, cells=set(cells)))
    # drop pairs that came back as the same road
    keep = []
    for r in out:
        if any(len(r["cells"] & p["cells"]) / max(1, len(r["cells"])) > 0.9
               for p in keep):
            continue
        keep.append(r)
    return dict(opt=opt, routes=keep, blocked=blocked, n=n)
