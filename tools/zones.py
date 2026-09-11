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

    def check_bake(self, bake):
        """Provenance against the bake in memory: the `written` the zone map
        was cut from must be the bake's own. Missing on either side is not
        a mismatch - the key is new and older bakes do not carry it."""
        mine = self.header.get("bake_meta_written")
        theirs = getattr(bake, "meta", {}).get("written")
        if mine and theirs and mine != theirs:
            self.warnings.append("cut from a bake written %s; the bake on disk says %s" % (mine, theirs))
            return False
        return True

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
            z.check_bake(bake)
        print(z.summary())
        out[mask] = z
    return out
