"""Model instances and their bounding boxes, straight out of a map's space.bin.

Path Studio's Python bake used to write `top` equal to `floor` - terrain only,
nothing standing on it - so the router saw a map with no obstacles at all and
would happily fly a course through a town. nuTerra's own GPU bake draws the
models, but only after you have opened the map in it once, and the Python bake
overwrote that with a flat one.

Everything needed is in the map's own space.bin. The bounds are NOT in the
.visual_processed files: BSMO carries a visibility box per model, so one pkg
and one file answers the whole question.

Format, transcribed from nuTerra's Space.bin reader (modSpaceBin.vb,
modSpacedBinVars.vb) - that is the reference implementation, this is a port:

    at 0x14      int32 table_size
    then         table_size x { char[4] magic, int32 version,
                                int64 offset, int64 length }

Each section is a run of BWArrays, and a BWArray is self describing:

    uint32 item_size, uint32 count, then item_size * count bytes

which is what makes this tractable - the arrays before the one you want can be
skipped without knowing their contents, so only the ORDER matters.

    BSMI (v3)   0 transforms            Matrix4, 64 B, row major
                1 chunk_models
                2 visibility_masks      uint32 flags
                3 model_BSMO_indexes    uint32 index, uint32 extras

    BSMO (v3)   0 models_loddings       1 tbl_2
                2 models_colliders      3 bsp_material_kinds
                4 models_visibility_bounds   Matrix2x3, 24 B: min xyz, max xyz
                5 model_info_items      ... and nine more

Only instances whose visibility mask carries CAPTURE_THE_FLAG (bit 0) are
placed, matching what nuTerra renders.
"""
import io
import re
import struct
import zipfile

import numpy as np

CAPTURE_THE_FLAG = 1 << 0

# Order of the BWArrays inside each section. Index, not offset - see the module
# docstring for why that is enough.
BSMI_TRANSFORMS = 0
BSMI_VIS_MASKS = 2
BSMI_MODEL_INDEX = 3
BSMO_VIS_BOUNDS = 4


def read_space_bin(pkg_path, map_name):
    """The raw space.bin for a map, out of its .pkg."""
    z = zipfile.ZipFile(pkg_path)
    want = re.compile(r"spaces/%s/space\.bin$" % re.escape(map_name), re.I)
    for n in z.namelist():
        if want.match(n):
            return z.read(n)
    raise FileNotFoundError("no spaces/%s/space.bin in %s" % (map_name, pkg_path))


def sections(buf):
    """{magic: (version, offset, length)} from the header table at 0x14."""
    (table_size,) = struct.unpack_from("<i", buf, 0x14)
    out, p = {}, 0x18
    for _ in range(table_size):
        magic, version, offset, length = struct.unpack_from("<4siqq", buf, p)
        out[magic.decode("ascii", "replace")] = (version, offset, length)
        p += 24
    return out


def bwarrays(buf, offset, n):
    """The first `n` BWArrays at `offset`, each as (item_size, count, memoryview)."""
    out, p = [], offset
    for _ in range(n):
        item_size, count = struct.unpack_from("<II", buf, p)
        p += 8
        body = memoryview(buf)[p:p + item_size * count]
        out.append((item_size, count, body))
        p += item_size * count
    return out


def read_instances(pkg_path, map_name):
    """Every placed model as (matrix4x4 row major, bmin xyz, bmax xyz), world space.

    The matrix carries nuTerra's DirectX -> OpenGL mirror, so the coordinates
    match the terrain footprint terrain_bake.py already works in: X is mirrored,
    which is the same S * M * S with S = diag(-1, 1, 1) that modSpaceBin applies
    by negating M12, M13, M21, M31 and M41.
    """
    buf = read_space_bin(pkg_path, map_name)
    sec = sections(buf)
    for need in ("BSMI", "BSMO"):
        if need not in sec:
            raise ValueError("%s has no %s section" % (map_name, need))

    bsmi = bwarrays(buf, sec["BSMI"][1], BSMI_MODEL_INDEX + 1)
    bsmo = bwarrays(buf, sec["BSMO"][1], BSMO_VIS_BOUNDS + 1)

    isz, icount, ibody = bsmi[BSMI_TRANSFORMS]
    if isz != 64:
        raise ValueError("BSMI transforms are %d bytes, expected 64" % isz)
    mats = np.frombuffer(ibody, dtype="<f4", count=icount * 16).reshape(icount, 4, 4).copy()

    # The X mirror, exactly as modSpaceBin does it.
    for r, c in ((0, 1), (0, 2), (1, 0), (2, 0), (3, 0)):
        mats[:, r, c] *= -1.0

    msz, mcount, mbody = bsmi[BSMI_VIS_MASKS]
    masks = np.frombuffer(mbody, dtype="<u4", count=mcount)

    xsz, xcount, xbody = bsmi[BSMI_MODEL_INDEX]
    if xsz != 8:
        raise ValueError("BSMI model indexes are %d bytes, expected 8" % xsz)
    idx = np.frombuffer(xbody, dtype="<u4", count=xcount * 2).reshape(xcount, 2)[:, 0]

    bsz, bcount, bbody = bsmo[BSMO_VIS_BOUNDS]
    if bsz != 24:
        raise ValueError("BSMO visibility bounds are %d bytes, expected 24" % bsz)
    bounds = np.frombuffer(bbody, dtype="<f4", count=bcount * 6).reshape(bcount, 2, 3)

    n = min(len(mats), len(masks), len(idx))
    keep = (masks[:n] & CAPTURE_THE_FLAG) != 0
    keep &= idx[:n] < bcount

    out = []
    for k in np.nonzero(keep)[0]:
        b = bounds[idx[k]]
        # Bounds are authored in the unmirrored frame; mirroring X swaps which
        # side is min and which is max, the same swap MapLoader does when it
        # fills bmin.X from Row1.X.
        bmin = np.array([-b[1][0], b[0][1], b[0][2]], dtype=np.float64)
        bmax = np.array([-b[0][0], b[1][1], b[1][2]], dtype=np.float64)
        out.append((mats[k].astype(np.float64), bmin, bmax))
    return out


def corners(bmin, bmax):
    """The eight corners of an axis aligned box."""
    return np.array([[x, y, z]
                     for x in (bmin[0], bmax[0])
                     for y in (bmin[1], bmax[1])
                     for z in (bmin[2], bmax[2])], dtype=np.float64)


def stamp(top, fp, instances, min_h=0.5, max_extent=None):
    """Raise `top` to each instance's box height over its footprint.

    The box is transformed by the instance matrix and then re-bounded on XZ -
    so a rotated building stamps the axis aligned rectangle AROUND its true
    footprint. That is conservative: it can only ever call more ground blocked
    than the model really covers, never less, which is the right way round for
    a router that must not fly through a wall.

    Anything shorter than min_h above its own base is skipped - kerbs, road
    furniture and ground clutter would otherwise pepper the mask.

    Anything WIDER than max_extent is skipped too, and that matters more. A
    handful of entries are map-scale scenery rather than obstacles: on
    07_lakeville three models of 406 exceed 500 m, one of them 22473 x 12700 x
    41278 m - the sky dome. Stamped, it would mark the entire map blocked at
    once. The median model is 2.7 x 2.0 x 2.6 m and p90 is under 15 m, so a
    threshold at the map's own width separates scenery from buildings cleanly
    without a magic number: nothing wider than the whole map is something to
    fly around.
    """
    wx_min, wx_max, wz_min, wz_max = fp
    if max_extent is None:
        max_extent = max(wx_max - wx_min, wz_max - wz_min)
    size = top.shape[0]
    sx = size / (wx_max - wx_min)
    sz = size / (wz_max - wz_min)
    placed = 0
    for m, bmin, bmax in instances:
        c = corners(bmin, bmax)
        # row-major, translation in row 3 - the same convention nuTerra reads
        w = c @ m[:3, :3] + m[3, :3]
        y_hi = float(w[:, 1].max())
        if y_hi - float(w[:, 1].min()) < min_h:
            continue
        x0, x1 = float(w[:, 0].min()), float(w[:, 0].max())
        z0, z1 = float(w[:, 2].min()), float(w[:, 2].max())
        if (x1 - x0) > max_extent or (z1 - z0) > max_extent:
            continue

        c0 = int(np.floor((x0 - wx_min) * sx))
        c1 = int(np.ceil((x1 - wx_min) * sx))
        r0 = int(np.floor((wz_max - z1) * sz))
        r1 = int(np.ceil((wz_max - z0) * sz))
        c0, c1 = max(0, c0), min(size, c1)
        r0, r1 = max(0, r0), min(size, r1)
        if c1 <= c0 or r1 <= r0:
            continue

        # Fill the ORIENTED footprint, not the axis aligned box around it.
        #
        # A building at 45 degrees has an axis aligned bound with twice its
        # area, and stamping that was marking half the map blocked. The eight
        # transformed corners project to a convex polygon on XZ - at most a
        # hexagon for a box - and a half plane test against its edges fills
        # exactly that, which is the footprint the model actually occupies.
        cols = wx_min + (np.arange(c0, c1) + 0.5) / sx
        rows = wz_max - (np.arange(r0, r1) + 0.5) / sz
        gx = cols[None, :]
        gz = rows[:, None]

        hull = _hull2d(np.stack([w[:, 0], w[:, 2]], axis=1))
        inside = np.ones((r1 - r0, c1 - c0), dtype=bool)
        for i in range(len(hull)):
            ax, az = hull[i]
            bx, bz = hull[(i + 1) % len(hull)]
            # counter-clockwise hull, so inside is cross >= 0 on every edge
            inside &= ((bx - ax) * (gz - az) - (bz - az) * (gx - ax)) >= -1e-6
        if not inside.any():
            continue

        block = top[r0:r1, c0:c1]
        np.maximum(block, np.where(inside, y_hi, -np.inf), out=block)
        placed += 1
    return placed


def _hull2d(pts):
    """Counter-clockwise convex hull of a few 2D points - monotone chain."""
    p = np.unique(np.round(pts, 4), axis=0)
    if len(p) < 3:
        return p
    p = p[np.lexsort((p[:, 1], p[:, 0]))]

    def half(ps):
        out = []
        for q in ps:
            while len(out) >= 2:
                (ax, ay), (bx, by) = out[-2], out[-1]
                if (bx - ax) * (q[1] - ay) - (by - ay) * (q[0] - ax) > 0:
                    break
                out.pop()
            out.append(q)
        return out[:-1]

    return np.array(half(p) + half(p[::-1]))
