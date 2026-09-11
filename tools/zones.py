"""The zone map - radius zoning, read from the file the Tank AI session writes.

The owner's idea (2026-09-11): "draw rings and find areas as large as we can
that the ring fits without hitting something. That whole area is safe to
drive in - no collision checks, only zone radius checks." The extraction
(distance transform of the free mask, maximal discs greedily largest-first,
links WALKED between centres) is built in nuTerra, in-process at map load;
Path Studio READS the result, the way it reads the bake, and never cuts a
second one. The contract, as spelled by that session:

    <flight folder>/<map>_zones_<mask>.csv

    # comments anywhere before the column line
    key=value header, one per line - map, mask, mask_rule, grid, cell_m,
        body_r_m, wx_min wx_max wz_min wz_max, radius_rule, link_rule,
        bake_meta_written, written, zones, links
    id,x,z,r_m,y_m,neighbours          <- the column line, verbatim
    0,-112.793,-204.395,57.611,0.024,11 34 44 ...

ids dense from 0 in DESCENDING radius; x z world metres in the bake's frame;
r_m the clear radius, already half a cell back; y_m the terrain height at the
centre; neighbours SPACE-separated ids in the last comma field, symmetric and
written on both sides, empty = an island (reachable ground with no way out
for a body of that radius - not an error). Numbers InvariantCulture: a
decimal POINT always. Unknown header keys are kept and ignored.

Three checks at load, none of them refusing: ids dense and radius
descending, adjacency symmetric, and the bake it was cut from against the
bake on disk (bake_meta_written vs the meta's `written`) - a zone map from a
stale bake is the same trap as a stale bake, and it has to announce itself.
"""
import os


class Zones:
    """One mask's zone map: arrays over the discs, and a neighbour list."""

    def __init__(self, path):
        import numpy as np
        self.path = path
        self.header = {}
        ids, xs, zs, rs, ys, nbrs = [], [], [], [], [], []
        cols = None
        with open(path, encoding="utf-8") as f:
            for raw in f:
                line = raw.strip()
                if not line or line.startswith("#"):
                    continue
                if cols is None:
                    if "=" in line and "," not in line.split("=", 1)[0]:
                        k, _, v = line.partition("=")
                        self.header[k.strip()] = v.strip()
                        continue
                    cols = [c.strip() for c in line.split(",")]
                    assert cols[:6] == ["id", "x", "z", "r_m", "y_m", "neighbours"], \
                        "%s: column line is %r" % (path, line)
                    continue
                parts = line.split(",", 5)
                if len(parts) < 5:
                    continue
                ids.append(int(parts[0]))
                xs.append(float(parts[1])); zs.append(float(parts[2]))
                rs.append(float(parts[3])); ys.append(float(parts[4]))
                nb = parts[5].strip() if len(parts) > 5 else ""
                nbrs.append([int(t) for t in nb.split()] if nb else [])
        self.id = np.asarray(ids, dtype=np.int64)
        self.x = np.asarray(xs, dtype=np.float64)
        self.z = np.asarray(zs, dtype=np.float64)
        self.r = np.asarray(rs, dtype=np.float64)
        self.y = np.asarray(ys, dtype=np.float64)
        self.neighbours = nbrs
        self.mask = self.header.get("mask", "?")
        self.warnings = []
        self._check()

    def __len__(self):
        return len(self.id)

    @property
    def n_links(self):
        return sum(len(n) for n in self.neighbours) // 2

    @property
    def islands(self):
        return int(sum(1 for n in self.neighbours if not n))

    def _check(self):
        import numpy as np
        n = len(self.id)
        if n == 0:
            self.warnings.append("no zones")
            return
        if not np.array_equal(self.id, np.arange(n)):
            self.warnings.append("ids are not dense from 0 in file order")
        # NON-INCREASING, not strictly descending: radii come off the square
        # root of an integer squared distance and 98% of rows tie with the
        # row above on the writer's real file. Only a rise is a fault.
        if n > 1 and (np.diff(self.r) > 1e-6).any():
            self.warnings.append("radii are not descending")
        bad = 0
        for i, nb in enumerate(self.neighbours):
            for j in nb:
                if j < 0 or j >= n or i not in self.neighbours[j]:
                    bad += 1
        if bad:
            self.warnings.append("%d one-sided links" % bad)
        for k in ("zones", "links"):
            if k in self.header:
                try:
                    want = int(self.header[k])
                    have = n if k == "zones" else self.n_links
                    if want != have:
                        self.warnings.append("header says %s=%d, file has %d" % (k, want, have))
                except ValueError:
                    pass

    # A zone map is cut moments after its bake in the same load (three
    # seconds apart on the writer's run), so a small gap is normal and the
    # thing worth catching is a zone CSV left over from an EARLIER session -
    # hours apart. Warn past this.
    STALE_S = 600.0

    def check_bake(self, bake, meta_path=None):
        """Provenance: what the zone map was cut from against the bake on
        disk. `bake_meta_written` is the writer's best evidence today - the
        LAST-WRITE time of <map>_meta.txt in UTC, not a key the bake carries
        (none exists yet). So: against the bake meta's own `written` when the
        writer has landed it, else against the meta file's mtime, which is
        weak (it moves on a copy, and it is the meta's, not the arrays') but
        catches the case that matters. Missing on either side is not a
        mismatch."""
        mine = _parse_utc(self.header.get("bake_meta_written"))
        if mine is None:
            return True
        theirs = _parse_utc(getattr(bake, "meta", {}).get("written"))
        source = "the bake's written key"
        if theirs is None and meta_path and os.path.exists(meta_path):
            theirs = os.path.getmtime(meta_path)
            source = "the meta file's mtime"
        if theirs is None:
            return True
        gap = abs(theirs - mine)
        if gap > self.STALE_S:
            self.warnings.append("cut from a bake %.0f min away from %s - a zone map from another session?"
                                 % (gap / 60.0, source))
            return False
        return True

    # ---- point in a disc --------------------------------------------------
    # A KD-tree over the centres, queried out to the widest radius on the map;
    # every candidate centre within that ball is tested against its own
    # radius. The containing disc need not be a NEAREST centre - a wide disc
    # holds points far from its centre - which is why the ball, not k-nearest.
    _tree = None

    def index(self):
        import numpy as np
        from scipy.spatial import cKDTree
        if len(self) == 0:
            return
        self._tree = cKDTree(np.stack([self.x, self.z], axis=1))
        self._rmax = float(self.r.max())

    def clearance(self, x, z, margin=0.0):
        """How far inside the best disc a point is, less `margin`: positive
        means the point sits in a zone with that much room to the rim
        beyond the margin; None means no disc holds it (or no index)."""
        if self._tree is None:
            self.index()
            if self._tree is None:
                return None
        best = None
        for i in self._tree.query_ball_point((x, z), self._rmax):
            d = ((self.x[i] - x) ** 2 + (self.z[i] - z) ** 2) ** 0.5
            c = self.r[i] - d - margin
            if c > 0.0 and (best is None or c > best):
                best = c
        return best

    def contains(self, x, z, margin=0.0):
        return self.clearance(x, z, margin) is not None

    def summary(self):
        r = self.r
        return ("zones %s: %d discs, %d links, %d islands, widest %.1f m, mean %.1f m, body %s m, grid %s%s" % (
            self.mask, len(self), self.n_links, self.islands,
            float(r.max()) if len(r) else 0.0, float(r.mean()) if len(r) else 0.0,
            self.header.get("body_r_m", "?"), self.header.get("grid", "?"),
            ("; WARNING " + "; ".join(self.warnings)) if self.warnings else ""))

    def links(self):
        """(i, j) pairs, each once."""
        for i, nb in enumerate(self.neighbours):
            for j in nb:
                if j > i:
                    yield i, j


def _parse_utc(text):
    """An ISO time as the writer spells it (2026-09-11T22:22:45Z) to seconds
    since the epoch; None for nothing or anything else."""
    if not text:
        return None
    import datetime
    t = text.strip()
    try:
        if t.endswith("Z"):
            t = t[:-1] + "+00:00"
        d = datetime.datetime.fromisoformat(t)
        if d.tzinfo is None:
            d = d.replace(tzinfo=datetime.timezone.utc)
        return d.timestamp()
    except ValueError:
        return None


def zones_path(folder, map_name, mask="tank"):
    return os.path.join(folder, "%s_zones_%s.csv" % (map_name, mask))


def find_zones(folder, map_name):
    """Every mask's zone map on disk for a map, as {mask: path}."""
    out = {}
    prefix = map_name + "_zones_"
    try:
        for fn in os.listdir(folder):
            if fn.startswith(prefix) and fn.endswith(".csv"):
                out[fn[len(prefix):-4]] = os.path.join(folder, fn)
    except OSError:
        pass
    return out


def load_zones(folder, map_name, bake=None):
    """Every zone map for a map, checked against the bake. {mask: Zones}."""
    out = {}
    for mask, path in sorted(find_zones(folder, map_name).items()):
        try:
            z = Zones(path)
        except Exception as e:
            print("zones: %s would not read: %s" % (path, e))
            continue
        if bake is not None:
            z.check_bake(bake, os.path.join(folder, map_name + "_meta.txt"))
        print(z.summary())
        out[mask] = z
    return out


if __name__ == "__main__":
    import sys
    import radar_commit as nav
    name = sys.argv[1] if len(sys.argv) > 1 else nav.MAP
    b = nav.Bake(nav.FOLDER, name)
    load_zones(nav.FOLDER, name, b)
