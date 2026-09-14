"""The editable path graph - points, the lines between them, and the areas
those lines enclose.

WHY A GRAPH AND NOT A LIST OF PATHS. The owner's rules for the editor are all
rules about structure:

    "if I delete a point some where in middle, it creates 2 lines"
    "if I drag an end point on to a line on a point, its a fork point and
     both lines become a fork path"
    "I want to rebuild the path every time i do anything to it"
    "select the verts that enclose the area"

Every one of those is free if the stored thing is a graph and the paths are
DERIVED from it, and every one of them is a special case if the stored thing
is a list of paths. Delete a middle point and its two edges go with it; the
next walk simply finds two runs where it found one. Drag an end onto another
point and the two merge; that node's degree climbs to three and the walk
emits three runs from it, which IS the fork. Nothing has to detect any of it.

So: nodes and edges are the truth, and `rebuild()` is the only thing that
produces paths. It is cheap enough to run on every single edit, which is what
the owner asked for and what keeps the drawing honest - there is no path
object that can fall out of step with the points under it.

The module holds no GL, no pygame and no viewer state, so it can be run and
checked without a window.
"""

import math


# A vertex is a fork when three or more edges meet at it. Two is a through
# point, one is an end, zero is a point not yet joined to anything.
FORK_DEGREE = 3


class PathEdit(object):
    """Nodes, edges, and everything derived from them."""

    # WHICH SIDE A POINT SERVES, as a bitmask on the point itself.
    #
    # "i need team id in the path points." A mask and not a number, because a
    # vertex can genuinely belong to both: where the two sets of roads run the
    # same street they share ground, and a point there serves whoever drives
    # it. 0 is a point drawn by hand that belongs to neither side yet.
    TEAM_1, TEAM_2, TEAM_BOTH = 1, 2, 3

    def __init__(self, snap_m=0.5, snap_on=True):
        self.nodes = {}          # id -> dict(x, z, team, msg, note, spd)
        self.edges = {}          # id -> [id, ...], symmetric
        self.paths = []          # [[id, ...], ...] - derived, never stored
        self.faces = []          # [Face, ...]      - derived, never stored
        self.snap_m = snap_m
        self.snap_on = snap_on
        self.builds = 0
        self._next = 1

    def clear(self):
        """Empty the graph and start ids again from one."""
        self.nodes.clear()
        self.edges.clear()
        self.paths, self.faces = [], []
        self._next = 1

    # ---- the grid ------------------------------------------------------
    def snap(self, x, z):
        """Round to the grid, in METRES.

        The whole editor works in world metres and only turns them into pixels
        to draw. A snap expressed in pixels would change size with the zoom,
        which is the opposite of what a snap is for.
        """
        if not self.snap_on or not self.snap_m or self.snap_m <= 0.0:
            return x, z
        s = self.snap_m
        return round(x / s) * s, round(z / s) * s

    # ---- mutation ------------------------------------------------------
    def add(self, x, z, snap=True, team=0):
        i = self._next
        self._next += 1
        if snap:
            x, z = self.snap(x, z)
        self.nodes[i] = dict(x=float(x), z=float(z), team=int(team),
                             start=False, start_team=0,
                             msg="", note="", spd="")
        self.edges[i] = []
        return i

    def team_of(self, i):
        return self.nodes.get(i, {}).get("team", 0)

    def edge_team(self, a, b):
        """Which side a LINE serves, worked out from its two ends.

        Both ends in common is the honest answer - a line from a team-1 point
        to a point both teams use is still team 1's line. Only when the ends
        have nothing in common (a hand-drawn join between the two sets) does
        it fall back to naming both.
        """
        ta, tb = self.team_of(a), self.team_of(b)
        return (ta & tb) or (ta | tb)

    def link(self, a, b):
        if a == b or a not in self.nodes or b not in self.nodes:
            return
        if b not in self.edges[a]:
            self.edges[a].append(b)
        if a not in self.edges[b]:
            self.edges[b].append(a)

    def unlink(self, a, b):
        if a in self.edges:
            self.edges[a] = [n for n in self.edges[a] if n != b]
        if b in self.edges:
            self.edges[b] = [n for n in self.edges[b] if n != a]

    def remove(self, i):
        # THE CROWN PASSES DOWN THE PATH. "if they are deleted, the next one
        # down takes the crown." A start is where tanks are sent, so it cannot
        # simply vanish with the point that held it - the run would still be
        # there with nothing telling anyone to drive it.
        #
        # Down the path means the neighbour that continues the run: the one
        # with the fewest other ways out, so the crown walks along the road
        # rather than hopping onto a fork and claiming three of them.
        if self.nodes.get(i, {}).get("start"):
            heirs = [n for n in self.edges.get(i, ()) if n in self.nodes]
            if heirs:
                heirs.sort(key=lambda n: (self.degree(n), n))
                self.nodes[heirs[0]]["start"] = True
        for n in list(self.edges.get(i, ())):
            self.edges[n] = [m for m in self.edges[n] if m != i]
        self.edges.pop(i, None)
        self.nodes.pop(i, None)

    def degree(self, i):
        return len(self.edges.get(i, ()))

    def split_edge(self, a, b, i):
        """Put node `i` in the middle of the edge a-b.

        Dropping a point onto a line has to break the line, or the point sits
        on top of it without being part of it and the run still walks straight
        past. This is what makes a fork ON a line possible at all.
        """
        # The new point stands on that line, so it serves what the line did.
        if i in self.nodes:
            self.nodes[i]["team"] |= self.edge_team(a, b)
        self.unlink(a, b)
        self.link(a, i)
        self.link(b, i)

    def merge(self, src, dst):
        """Fold `src` into `dst`, keeping every edge src had.

        This is what a dragged end landing on another point does. If the two
        runs met at a point that already had two edges, dst comes out with
        three and is a fork from this moment on - no flag is set anywhere.
        """
        for n in list(self.edges.get(src, ())):
            self.link(dst, n)
        # THE SURVIVOR INHERITS BOTH SIDES. Merging a team-2 end onto a team-1
        # point makes a junction that both use, and the mask has to say so or
        # the fork would claim to serve only whoever happened to be dragged
        # onto rather than dragged.
        if src in self.nodes and dst in self.nodes:
            self.nodes[dst]["team"] |= self.nodes[src].get("team", 0)
            self.nodes[dst]["start_team"] = (
                self.nodes[dst].get("start_team", 0) |
                self.nodes[src].get("start_team", 0))
        self.remove(src)
        return dst

    # ---- breaking ------------------------------------------------------
    def payload(self, i):
        """Everything about a point EXCEPT which point it is.

        Position is not in here either: the two callers below decide where a
        new point goes, and both of them put it somewhere the original was
        not. What travels is what the point MEANS - which side it serves and
        what it tells a tank that reaches it.
        """
        n = self.nodes[i]
        return dict(team=n.get("team", 0), start=n.get("start", False),
                    start_team=n.get("start_team", 0),
                    msg=n["msg"], note=n["note"], spd=n["spd"])

    def _spawn(self, x, z, data):
        j = self.add(x, z, snap=False, team=data["team"])
        self.nodes[j].update(start=data.get("start", False),
                             start_team=data.get("start_team", 0),
                             msg=data["msg"],
                             note=data["note"], spd=data["spd"])
        return j

    def break_at(self, i):
        """Cut every line through a point, capping each branch with its own
        new end point in the same place.

        The owner: "break the paths at that point and add new points to
        replace the lines it broke by removing them... copy the data in the
        selected point other than ID to the other new end line points."

        So this is not a delete. A delete at a through point leaves two runs
        that stop one point short of where they used to meet; this leaves both
        of them reaching exactly as far as before, each now ending in its own
        point rather than sharing one. A fork of three comes apart into three
        ends. Every new end carries the original's team, message, note and
        speed cap - the id is the only thing that cannot be shared, because it
        is the thing that says these are now separate points.
        """
        if i not in self.nodes:
            return []
        n = self.nodes[i]
        data = self.payload(i)
        made = [self._spawn(n["x"], n["z"], data)
                for _ in self.edges.get(i, ())]
        for j, nb in zip(made, list(self.edges.get(i, ()))):
            self.link(j, nb)
        self.remove(i)
        return made

    def break_edge(self, a, b, pull=0.4):
        """Cut one line, leaving a dangling end on each side of the gap.

        The ends are pulled back towards their own anchors rather than both
        landing on the midpoint, because two points at the same coordinate
        cannot be told apart on the map or under the pointer - the break has
        to be visible to be worth making. Each new end copies the anchor it
        stays attached to, so a message on one end of a road does not jump to
        the other half of it.
        """
        if b not in self.edges.get(a, ()):
            return []
        pa, pb = self.nodes[a], self.nodes[b]
        self.unlink(a, b)
        made = []
        for anchor, other in ((a, b), (b, a)):
            p, q = self.nodes[anchor], self.nodes[other]
            j = self._spawn(p["x"] + (q["x"] - p["x"]) * pull,
                            p["z"] + (q["z"] - p["z"]) * pull,
                            self.payload(anchor))
            self.link(j, anchor)
            made.append(j)
        return made

    # ---- queries -------------------------------------------------------
    def near_node(self, x, z, tol_m, skip=None):
        best, bd = None, float(tol_m)
        for i, n in self.nodes.items():
            if i == skip:
                continue
            d = math.hypot(n["x"] - x, n["z"] - z)
            if d < bd:
                best, bd = i, d
        return best

    def near_edge(self, x, z, tol_m, skip=None):
        """The closest point ON an edge, as (a, b, x, z), or None.

        `skip` drops any edge touching that node, so a point being dragged
        never lands on its own line.
        """
        best, bd = None, float(tol_m)
        for a in self.edges:
            for b in self.edges[a]:
                if a > b or a == skip or b == skip:
                    continue
                p, q = self.nodes[a], self.nodes[b]
                dx, dz = q["x"] - p["x"], q["z"] - p["z"]
                ll = dx * dx + dz * dz
                t = 0.0 if ll <= 0.0 else max(0.0, min(
                    1.0, ((x - p["x"]) * dx + (z - p["z"]) * dz) / ll))
                px, pz = p["x"] + t * dx, p["z"] + t * dz
                d = math.hypot(x - px, z - pz)
                if d < bd:
                    best, bd = (a, b, px, pz), d
        return best

    # ---- derived: the runs ---------------------------------------------
    def rebuild(self):
        """Recompute paths and faces from the graph. Call after EVERY edit.

        A run is the stretch between two nodes that are not simple through
        points - so it starts and ends at an end, a fork, or an isolated node,
        and passes through any number of degree-2 points on the way.
        """
        self.builds += 1
        seen, out = set(), []
        for a in self.nodes:
            if self.degree(a) == 2:
                continue
            for b in self.edges[a]:
                key = (a, b) if a < b else (b, a)
                if key in seen:
                    continue
                run = self._walk(a, b)
                for i in range(len(run) - 1):
                    p, q = run[i], run[i + 1]
                    seen.add((p, q) if p < q else (q, p))
                out.append(run)
        # A RING OF NOTHING BUT THROUGH POINTS has no node to start at, so the
        # loop above never reaches it. Without this a closed circuit of paths
        # would draw as no path at all while still enclosing an area.
        for a in self.nodes:
            if self.degree(a) != 2:
                continue
            for b in self.edges[a]:
                key = (a, b) if a < b else (b, a)
                if key in seen:
                    continue
                run = self._walk(a, b)
                for i in range(len(run) - 1):
                    p, q = run[i], run[i + 1]
                    seen.add((p, q) if p < q else (q, p))
                out.append(run)
        self.paths = out
        self.find_faces()
        return out

    def _walk(self, a, b):
        run, prev, cur = [a, b], a, b
        while True:
            nxt = [n for n in self.edges.get(cur, ()) if n != prev]
            if self.degree(cur) != 2 or len(nxt) != 1:
                break
            prev, cur = cur, nxt[0]
            run.append(cur)
            if cur == a:
                break
        return run

    # ---- derived: the enclosed areas -----------------------------------
    def find_faces(self):
        """Every region the paths enclose, as the ring of verts around it.

        "select the verts that enclose the area" - so an area is not a shape
        the user draws and maintains, it is a FACE of the graph. Sort each
        node's neighbours by bearing; then, arriving at v from u, leave by the
        neighbour sitting just clockwise of u around v. That walk always
        closes on itself, and every closed walk is one region of the drawing.

        Move a vertex and the area moves with it. Delete one and the ring
        opens and it stops being an area. Nothing to keep in step.
        """
        order = {}
        for a in self.edges:
            na = self.nodes[a]
            order[a] = sorted(
                self.edges[a],
                key=lambda b, na=na: math.atan2(self.nodes[b]["z"] - na["z"],
                                                self.nodes[b]["x"] - na["x"]))
        used, rings = set(), []
        for a0 in list(self.edges):
            for b0 in self.edges[a0]:
                if (a0, b0) in used:
                    continue
                ring, u, v, guard = [], a0, b0, 0
                while guard < 100000:
                    guard += 1
                    used.add((u, v))
                    ring.append(u)
                    nb = order.get(v, ())
                    if u not in nb:
                        break
                    i = nb.index(u)
                    w = nb[(i - 1) % len(nb)]
                    u, v = v, w
                    if (u, v) == (a0, b0):
                        break
                if len(ring) >= 3:
                    rings.append(ring)
        # THE OUTSIDE OF THE DRAWING IS TOLD APART BY SIGN, NOT BY SIZE.
        #
        # With the clockwise-next rule above, every enclosed region comes out
        # with a POSITIVE shoelace and the one walk that goes round the
        # outside of everything comes out negative. Measured on a bare
        # triangle: +40.0 for the face, -40.0 for the outside.
        #
        # Filtering by "the biggest walk is the outside" instead looks right
        # and is wrong, which cost a debugging round: hang one dead-end spur
        # off that triangle and the outer walk becomes [1,3,2,4,2] - out along
        # the spur and back - which encloses no extra area, so the two are
        # +40.0 and -40.0 and the size test throws away the real face. Every
        # graph with a loop and a tail hits that, which is most of them.
        out = []
        for r in rings:
            s = 0.0
            for i in range(len(r)):
                p, q = self.nodes[r[i]], self.nodes[r[(i + 1) % len(r)]]
                s += p["x"] * q["z"] - q["x"] * p["z"]
            out.append(Face(r, s * 0.5))
        self.faces = [f for f in out if f.area > 1e-6]
        return self.faces

    def face_at(self, x, z):
        """The smallest enclosed area containing this point, or None.

        Smallest, so a region inside another region picks the inner one.
        """
        best = None
        for f in self.faces:
            if f.contains(self.nodes, x, z) and (best is None or
                                                 f.area < best.area):
                best = f
        return best

    # ---- what the panel reads ------------------------------------------
    def starts(self):
        """Every point tanks are sent to, in id order."""
        return [i for i, n in sorted(self.nodes.items()) if n.get("start")]

    def ends(self):
        """Every point where a run stops - a start is usually one of these."""
        return [i for i in sorted(self.nodes) if self.degree(i) == 1]

    def forks(self):
        return [i for i in self.nodes if self.degree(i) >= FORK_DEGREE]

    def run_length(self, run):
        L = 0.0
        for i in range(len(run) - 1):
            a, b = self.nodes[run[i]], self.nodes[run[i + 1]]
            L += math.hypot(a["x"] - b["x"], a["z"] - b["z"])
        return L

    def kind(self, i):
        d = self.degree(i)
        return ("fork" if d >= FORK_DEGREE else
                "end" if d == 1 else "through" if d == 2 else "loose")

    # ---- saving --------------------------------------------------------
    def to_dict(self):
        """Plain data, for json.dump. Edges go out once, not twice."""
        return dict(
            snap_m=self.snap_m,
            nodes=[dict(id=i, x=n["x"], z=n["z"], team=n.get("team", 0),
                        start=bool(n.get("start", False)),
                        start_team=int(n.get("start_team", 0)),
                        msg=n["msg"], note=n["note"], spd=n["spd"])
                   for i, n in sorted(self.nodes.items())],
            edges=sorted({(min(a, b), max(a, b))
                          for a in self.edges for b in self.edges[a]}))

    @classmethod
    def from_dict(cls, d):
        pe = cls(snap_m=d.get("snap_m", 0.5))
        for n in d.get("nodes", ()):
            i = int(n["id"])
            # A GRAPH SAVED BEFORE start_team EXISTED has only the shared
            # `team` mask on its starts. Falling back to it reproduces exactly
            # what that file used to mean rather than leaving every start in
            # it teamless, which would read as "nobody spawns here".
            st = int(n.get("start_team", 0))
            if not st and n.get("start"):
                st = int(n.get("team", 0))
            pe.nodes[i] = dict(x=float(n["x"]), z=float(n["z"]),
                               team=int(n.get("team", 0)),
                               start=bool(n.get("start", False)),
                               start_team=st,
                               msg=n.get("msg", ""), note=n.get("note", ""),
                               spd=n.get("spd", ""))
            pe.edges[i] = []
            pe._next = max(pe._next, i + 1)
        for a, b in d.get("edges", ()):
            pe.link(int(a), int(b))
        pe.rebuild()
        return pe


class Face(object):
    """One enclosed region: the ring of vertex ids, and its area in m^2.

    `area` is the SIGNED shoelace over the ring. Enclosed regions are
    positive; the walk round the outside of the whole drawing is negative and
    is what find_faces drops. Read `abs(area)` if all you want is the size.
    """

    __slots__ = ("ring", "area")

    def __init__(self, ring, area):
        self.ring = ring
        self.area = area

    def __len__(self):
        return len(self.ring)

    def contains(self, nodes, x, z):
        """Even-odd ray cast against the ring."""
        hit = False
        r = self.ring
        j = len(r) - 1
        for i in range(len(r)):
            a, b = nodes[r[i]], nodes[r[j]]
            if ((a["z"] > z) != (b["z"] > z) and
                    x < (b["x"] - a["x"]) * (z - a["z"]) /
                    (b["z"] - a["z"]) + a["x"]):
                hit = not hit
            j = i
        return hit
