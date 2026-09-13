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


def flood(blocked, goal_rc):
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
        vals.append(np.full(ia.shape, w, np.float32))

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
