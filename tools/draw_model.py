"""Draw any WoT .primitives model straight out of the installed packages.

    python tools/draw_model.py env_19_08_StreetLamp02
    python tools/draw_model.py StreetLamp02 --campath nuTerra/cam_paths/19_monastery.campath
    python tools/draw_model.py bld_19_01_Vhouse_05 --lod 1 --out house.png

Nothing here talks to nuTerra. It reads the .pkg, parses the mesh the way
PrimitiveLoader.vb does, and rasterises it - so it answers "what does this
model actually look like, and where in it is the bulb" without launching the
viewer, loading a map, or waiting for a bake.

WHY IT EXISTS: a bulb is authored INSIDE a light fixture, and whether the
fixture encloses it decides whether the lamp shadow bake seals the light in.
That question is unanswerable from a frame - a sealed bulb and a wrong range
both look like a lamp that does not light - and it is obvious in one drawing.
See docs/primitives_reader.md for the format and for how the parse is checked.

Everything is validated rather than assumed: the printed bounding box is
directly comparable with nuTerra's own "bulb placer: <model> - N mesh(es), box
A x B x C m" log line, and if those disagree the parse is wrong, not the model.
"""
import argparse
import glob
import os
import struct
import sys
import zipfile

import numpy as np
from PIL import Image, ImageDraw

DEFAULT_WOT = "C:/Games/World_of_Tanks_NA"

# Stride per vertex format name, from PrimitiveLoader.load_primitives_vertices.
# Only the position (the first three floats) is read here, but the stride has
# to be exact or every vertex after the first is garbage.
STRIDES = {
    "xyznuv": 32, "BPVTxyznuv": 24,
    "xyznuvtb": 32, "BPVTxyznuvtb": 32,
    "xyznuviiiwwtb": 37, "BPVTxyznuviiiww": 32, "BPVTxyznuviiiwwtb": 40,
}


# --------------------------------------------------------------------- pkg
def find_in_packages(wot, needle, lod):
    """The pkg entry whose path contains needle at the requested LOD."""
    root = os.path.join(wot, "res", "packages")
    pkgs = sorted(glob.glob(os.path.join(root, "*.pkg")))
    if not pkgs:
        sys.exit("no .pkg files under %s" % root)

    want_lod = "/lod%d/" % lod
    for pkg in pkgs:
        try:
            with zipfile.ZipFile(pkg) as z:
                for n in z.namelist():
                    if (needle in n and want_lod in n
                            and n.endswith(".primitives_processed")):
                        return pkg, n
        except zipfile.BadZipFile:
            continue
    return None, None


# ---------------------------------------------------------------- sections
def read_sections(raw):
    """Section table lives at the END of the file.

    Last 4 bytes are the table's own length; the table starts at
    len-4-that. Each entry is size:u32, 16 unused bytes, namelen:u32, name,
    padded to 4. Bodies run from offset 4, each padded to 4 as well - so the
    entries carry no offsets and the only way to locate a body is to add up
    every size before it, in order.
    """
    table_start = struct.unpack_from("<I", raw, len(raw) - 4)[0]
    p = len(raw) - 4 - table_start
    out, off = {}, 4
    while p < len(raw) - 4:
        size = struct.unpack_from("<I", raw, p)[0]
        p += 4 + 16
        nlen = struct.unpack_from("<I", raw, p)[0]
        p += 4
        name = raw[p:p + nlen].decode("ascii", "replace")
        p += nlen + ((4 - nlen % 4) if nlen % 4 else 0)
        out[name] = (off, size)
        off += size + ((4 - size % 4) if size % 4 else 0)
    return out


def cstr(buf, at, n):
    s = buf[at:at + n]
    k = s.find(b"\0")
    return s[:k if k >= 0 else n].decode("ascii", "replace")


def read_mesh(raw, sections, verbose=True):
    """Positions and triangles, in nuTerra's space.

    Sections come in two layouts (see VISUAL_PROCESSED_FORMAT notes): one
    global "vertices"/"indices" pair, or one "<base>.vertices" per mesh. Both
    are handled by taking every section that ends in the right suffix.
    """
    vnames = [k for k in sections if k == "vertices" or k.endswith(".vertices")]
    inames = [k for k in sections if k == "indices" or k.endswith(".indices")]
    if not vnames or not inames:
        sys.exit("no vertices/indices sections - found %s" % sorted(sections))

    all_v, all_t, base = [], [], 0
    for vn, inm in zip(sorted(vnames), sorted(inames)):
        voff, vsize = sections[vn]
        vfmt = cstr(raw, voff, 64)
        if vfmt not in STRIDES:
            sys.exit("unknown vertex format %r in %s" % (vfmt, vn))
        stride = STRIDES[vfmt]

        # A BPVT section carries a SECOND 64-byte format string plus 4 bytes
        # before the count - 68 more - so the body starts at 136, not 68. A
        # 132-byte guess matches by integer-division coincidence and silently
        # shifts every vertex by one float.
        cnt_at = voff + 64 + (68 if vfmt.startswith("BPVT") else 0)
        n = struct.unpack_from("<I", raw, cnt_at)[0]
        body = cnt_at + 4
        if body + n * stride > voff + vsize + 4:
            sys.exit("%s: %d verts at stride %d overruns its section" % (vn, n, stride))

        v = np.empty((n, 3), np.float64)
        for i in range(n):
            x, y, z = struct.unpack_from("<3f", raw, body + i * stride)
            v[i] = (-x, y, z)          # DirectX -> OpenGL, as the loader does

        ioff, _ = sections[inm]
        itype = cstr(raw, ioff, 64)
        nidx, ngroups = struct.unpack_from("<2I", raw, ioff + 64)
        code = "I" if itype == "list32" else "H"
        idx = np.array(struct.unpack_from("<%d%s" % (nidx, code), raw, ioff + 72),
                       np.int64)
        # Winding flipped with the X mirror, or every face points inward.
        t = idx.reshape(-1, 3)[:, [1, 0, 2]] + base

        if verbose:
            print("  %-22s %-18s stride %2d  verts %5d  tris %5d  groups %d"
                  % (vn, vfmt, stride, n, len(t), ngroups))
        all_v.append(v)
        all_t.append(t)
        base += n

    return np.concatenate(all_v), np.concatenate(all_t)


# ------------------------------------------------------------------- bulbs
def read_bulbs(campath, model_needle):
    """Every bulb record in a .campath that targets this model.

    A record begins with its primitives path in a 160-byte field, so finding
    the path finds the record - which beats walking the header, where the bulb
    block sits behind five counts and strides. Layout from MapCamPath.Load:
    kind at +160, pos at +164, aim at +176.
    """
    if not campath or not os.path.exists(campath):
        return []
    cam = open(campath, "rb").read()
    key = model_needle.encode("ascii")
    out, at = [], cam.find(key)
    while at >= 0:
        start = cam.rfind(b"content/", 0, at)
        if start >= 0:
            kind = struct.unpack_from("<I", cam, start + 160)[0]
            pos = np.array(struct.unpack_from("<3f", cam, start + 164))
            aim = np.array(struct.unpack_from("<3f", cam, start + 176))
            out.append((kind, pos, aim))
        at = cam.find(key, at + 1)
    return out


# -------------------------------------------------------------- rasteriser
BG = np.array([18, 20, 24], np.float64)
GREY = np.array([196, 200, 208], np.float64)
RED = np.array([228, 52, 46], np.float64)
MARK = np.array([255, 226, 96], np.float64)


def render(verts, tris, mark, hilite_r, eye, up, W, H, zoom_m=None):
    """Orthographic, z-buffered, flat two-sided Lambert from a headlight.

    Written out rather than handed to a plotting library so the silhouette is
    the real triangles - a scatter of vertices cannot show whether a fixture
    is a closed shell, which is the whole question this tool answers.
    """
    f = eye / np.linalg.norm(eye)
    r = np.cross(f, up)
    r /= np.linalg.norm(r)
    u = np.cross(r, f)
    M = np.stack([r, u, f])

    V = verts @ M.T
    B = mark @ M.T

    if zoom_m is None:
        mn, mx = V[:, :2].min(0), V[:, :2].max(0)
        cx, cy = (mn + mx) * 0.5
        sx_m, sy_m = (mx - mn) * 1.06
        scale = min(W / max(sx_m, 1e-6), H / max(sy_m, 1e-6))
    else:
        cx, cy = B[0], B[1]
        scale = min(W, H) / zoom_m

    def project(P):
        return ((P[..., 0] - cx) * scale + W * 0.5,
                H * 0.5 - (P[..., 1] - cy) * scale)

    sx, sy = project(V)
    depth = V[:, 2]
    img = np.tile(BG, (H, W, 1))
    zbuf = np.full((H, W), np.inf)

    near = np.linalg.norm(verts - mark, axis=1)[tris].min(1) < hilite_r

    a, b, c = tris[:, 0], tris[:, 1], tris[:, 2]
    nrm = np.cross(verts[b] - verts[a], verts[c] - verts[a])
    ln = np.linalg.norm(nrm, axis=1)
    ln[ln == 0] = 1.0
    shade = 0.25 + 0.75 * np.abs((nrm / ln[:, None]) @ f)

    for t in np.argsort(-depth[tris].mean(1)):        # far to near
        i0, i1, i2 = tris[t]
        x = np.array([sx[i0], sx[i1], sx[i2]])
        y = np.array([sy[i0], sy[i1], sy[i2]])
        z = np.array([depth[i0], depth[i1], depth[i2]])
        x0, x1 = int(max(0, np.floor(x.min()))), int(min(W - 1, np.ceil(x.max())))
        y0, y1 = int(max(0, np.floor(y.min()))), int(min(H - 1, np.ceil(y.max())))
        if x1 < x0 or y1 < y0:
            continue
        px, py = np.meshgrid(np.arange(x0, x1 + 1) + 0.5, np.arange(y0, y1 + 1) + 0.5)
        d = (y[1] - y[2]) * (x[0] - x[2]) + (x[2] - x[1]) * (y[0] - y[2])
        if abs(d) < 1e-12:
            continue
        w0 = ((y[1] - y[2]) * (px - x[2]) + (x[2] - x[1]) * (py - y[2])) / d
        w1 = ((y[2] - y[0]) * (px - x[2]) + (x[0] - x[2]) * (py - y[2])) / d
        w2 = 1.0 - w0 - w1
        inside = (w0 >= 0) & (w1 >= 0) & (w2 >= 0)
        if not inside.any():
            continue
        zz = w0 * z[0] + w1 * z[1] + w2 * z[2]
        sub = zbuf[y0:y1 + 1, x0:x1 + 1]
        win = inside & (zz < sub)
        if not win.any():
            continue
        sub[win] = zz[win]
        img[y0:y1 + 1, x0:x1 + 1][win] = (RED if near[t] else GREY) * shade[t]

    bx, by = project(B[None, :])
    bx, by = float(bx[0]), float(by[0])
    yy, xx = np.mgrid[0:H, 0:W]
    rr = np.hypot(xx - bx, yy - by)
    k = 2.2 if zoom_m is not None else 1.0
    img[(rr > 7 * k) & (rr < 9.5 * k)] = MARK
    img[((np.abs(xx - bx) < 14 * k) & (np.abs(yy - by) < 1.2 * k)) |
        ((np.abs(yy - by) < 14 * k) & (np.abs(xx - bx) < 1.2 * k))] = MARK
    if zoom_m is not None:                             # the radius, to scale
        hr = hilite_r * scale
        img[(rr > hr - 1.0) & (rr < hr + 1.0)] = np.array([120, 90, 90])

    return Image.fromarray(img.clip(0, 255).astype(np.uint8))


def main():
    ap = argparse.ArgumentParser(description=__doc__,
                                 formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("model", help="substring of the model path, e.g. StreetLamp02")
    ap.add_argument("--wot", default=DEFAULT_WOT, help="WoT install (default %s)" % DEFAULT_WOT)
    ap.add_argument("--lod", type=int, default=0)
    ap.add_argument("--campath", default=None, help=".campath to read bulb records from")
    ap.add_argument("--radius", type=float, default=0.5,
                    help="metres around the mark to paint red (default 0.5)")
    ap.add_argument("--out", default=None, help="output PNG (default <model>.png)")
    args = ap.parse_args()

    pkg, name = find_in_packages(args.wot, args.model, args.lod)
    if not name:
        sys.exit("no lod%d .primitives_processed matching %r" % (args.lod, args.model))
    print("%s\n  in %s" % (name, os.path.basename(pkg)))

    with zipfile.ZipFile(pkg) as z:
        raw = z.read(name)
    verts, tris = read_mesh(raw, read_sections(raw))

    lo, hi = verts.min(0), verts.max(0)
    print("  box %.2f x %.2f x %.2f m   (compare nuTerra's own bulb placer log)"
          % tuple(hi - lo))

    stem = os.path.basename(name).split(".")[0]
    bulbs = read_bulbs(args.campath, stem + ".primitives")
    if bulbs:
        for kind, pos, aim in bulbs:
            d = aim - pos
            print("  bulb kind %d  pos %s  aim %s  (points %s)"
                  % (kind, np.round(pos, 3), np.round(aim, 3), np.round(d / (np.linalg.norm(d) or 1), 2)))
        mark = bulbs[0][1]
    else:
        if args.campath:
            print("  no bulb record for this model in %s" % args.campath)
        mark = (lo + hi) * 0.5
        print("  no bulb - marking the centre of the box instead")

    NARROW, TALL = 210, 900
    tiles = [
        (render(verts, tris, mark, args.radius, np.array([0.0, 0.0, 1.0]),
                np.array([0.0, 1.0, 0.0]), NARROW, TALL), "front (+Z)"),
        (render(verts, tris, mark, args.radius, np.array([1.0, 0.0, 0.0]),
                np.array([0.0, 1.0, 0.0]), NARROW, TALL), "side (+X)"),
        (render(verts, tris, mark, args.radius, np.array([0.7, -0.3, 0.75]),
                np.array([0.0, 1.0, 0.0]), NARROW, TALL), "three-quarter"),
        (render(verts, tris, mark, args.radius, np.array([0.7, -0.3, 0.75]),
                np.array([0.0, 1.0, 0.0]), 620, TALL, zoom_m=args.radius * 2.8),
         "close-up, %.1f m across - ring is the %.2f m radius"
         % (args.radius * 2.8, args.radius)),
    ]

    gap = 14
    sheet = Image.new("RGB", (sum(t[0].width for t in tiles) + gap * (len(tiles) + 1),
                              TALL + gap * 2 + 26), (10, 11, 14))
    d = ImageDraw.Draw(sheet)
    x = gap
    for im, title in tiles:
        sheet.paste(im, (x, gap))
        d.text((x + 4, TALL + gap + 6), title, fill=(150, 156, 168))
        x += im.width + gap

    out = args.out or (stem + ".png")
    sheet.save(out)
    print("  wrote %s %s" % (out, sheet.size))


if __name__ == "__main__":
    main()
