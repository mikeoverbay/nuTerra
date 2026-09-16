"""wot_havok.py - read World of Tanks .havok collision files (Havok 2020
binary tagfiles, "TAG0" / SDKV 20200200) straight out of the game packages.

    python wot_havok.py info  <file.havok | pkg:entry>     bodies, shapes, material kinds
    python wot_havok.py obj   <file.havok | pkg:entry> out.obj
    python wot_havok.py dump  <file.havok | pkg:entry>     the whole object tree as JSON
    python wot_havok.py types <file.havok | pkg:entry>     the type table

A "pkg:entry" source is <package name>:<path inside it>, resolved under the
WoT install's res/packages. Everything here was measured on the NA install;
see docs/havok_format.md for the format and the checks behind it.

Cracked 2026-09-16 by the Fable AI Navigation Helper session.
"""
import struct, sys, os, json, zipfile

WOT_PACKAGES = "C:/Games/World_of_Tanks_NA/res/packages"

import sys as _sys; _sys.setrecursionlimit(20000)

import sys as _sys; _sys.setrecursionlimit(20000)

import sys as _sys; _sys.setrecursionlimit(20000)

import sys as _sys; _sys.setrecursionlimit(20000)

# ---------------------------------------------------------------- tagfile container + type table
"""Havok 2020 tagfile (TAG0) reader - first cut. Sections, type strings, type
names, field names, type bodies, ITEM table. Every claim is checked by
'consumed exactly the section' asserts."""

def sections(b, off=0, end=None):
    end = len(b) if end is None else end
    out = []
    while off + 8 <= end:
        hdr = struct.unpack_from(">I", b, off)[0]
        size = hdr & 0x3fffffff; flags = hdr >> 30
        tag = b[off+4:off+8].decode("ascii")
        out.append((tag, off+8, off+size, flags))
        off += size
    return out

def varint(b, p):
    c = b[p]
    if c < 0x80: return c, p+1
    if c < 0xC0: return ((c & 0x3f) << 8) | b[p+1], p+2
    if c < 0xE0: return ((c & 0x1f) << 16) | (b[p+1] << 8) | b[p+2], p+3
    if c == 0xE8: return int.from_bytes(b[p+1:p+5], "big"), p+5          # 32-bit follows (seen: INT_MAX, -1)
    if c < 0xF0: return ((c & 0x0f) << 24) | int.from_bytes(b[p+1:p+4], "big"), p+4
    raise ValueError(f"varint lead {c:#x} at {p}")

def strings(b, s, e):
    return b[s:e].split(b"\0")[:-1]  # trailing NUL

def parse(b):
    top = sections(b)
    assert top[0][0] == "TAG0"
    t0s, t0e = top[0][1], top[0][2]
    secs = {t: (s, e) for t, s, e, f in sections(b, t0s, t0e)}
    out = {"secs": secs}
    out["sdk"] = b[secs["SDKV"][0]:secs["SDKV"][1]].decode()
    ts, te = secs["TYPE"]
    tsub = {t: (s, e) for t, s, e, f in sections(b, ts, te)}
    out["tsub"] = tsub
    tstr = [x.decode() for x in strings(b, *tsub["TST1"])]
    fstr = [x.decode() for x in strings(b, *tsub["FST1"])]
    # TNA1: type names
    s, e = tsub["TNA1"]; p = s
    n, p = varint(b, p)
    types = [None]  # index 0 = none
    for i in range(1, n):
        ni, p = varint(b, p); nt, p = varint(b, p)
        targs = []
        for _ in range(nt):
            an, p = varint(b, p); av, p = varint(b, p)
            targs.append((tstr[an], av))
        types.append({"name": tstr[ni], "targs": targs})
    assert p <= e and not any(b[p:e]), ("TNA1 leftover", p, e, b[p:e])
    # TBDY
    s, e = tsub["TBDY"]; p = s
    while p < e:
        ti, p = varint(b, p)
        if ti == 0: continue
        t = types[ti]
        t["parent"], p = varint(b, p)
        fl, p = varint(b, p); t["flags"] = fl
        if fl & 0x01: t["subtype"], p = varint(b, p)
        if fl & 0x02 and (t.get("subtype", 0) & 0xF) >= 6: t["elem"], p = varint(b, p)
        if fl & 0x04: t["version"], p = varint(b, p)
        if fl & 0x08: t["size"], p = varint(b, p); t["align"], p = varint(b, p)
        if fl & 0x10: t["abstract"], p = varint(b, p)
        if fl & 0x20:
            nm, p = varint(b, p); t["mhi"] = nm >> 16; nm &= 0xffff; mem = []
            for _ in range(nm):
                fn, p = varint(b, p); ff, p = varint(b, p); fo, p = varint(b, p); ft, p = varint(b, p)
                mem.append((fstr[fn], ff, fo, ft))
            t["members"] = mem
        if fl & 0x40:
            ni_, p = varint(b, p); t["ifaces"] = []
            for _ in range(ni_):
                a, p = varint(b, p); c, p = varint(b, p); t["ifaces"].append((a, c))
        assert not (fl & ~0x7f), f"unknown flag bits {fl:#x} on {t['name']}"
    assert p <= e and not any(b[p:e]), ("TBDY leftover", p, e, b[p:e])
    out["types"] = types
    # INDX / ITEM
    s, e = secs["INDX"]
    isub = {t: (s_, e_) for t, s_, e_, f in sections(b, s, e)}
    s, e = isub["ITEM"]
    items = []
    for o in range(s, e, 12):
        ti, doff, cnt = struct.unpack_from("<III", b, o)
        items.append((ti & 0x00ffffff, ti >> 24, doff, cnt))
    out["items"] = items
    out["isub"] = isub
    return out

def tname(types, i):
    if i == 0: return "void"
    t = types[i]; n = t["name"]
    if t["targs"]: n += "<" + ",".join(f"{k}={v}" for k, v in t["targs"]) + ">"
    return n

# ---------------------------------------------------------------- objects in DATA
"""Object reader over a parsed TAG0 file. Layout rules (measured on WoT .havok):
pointer / array / string / relptr / relarray = a 4-byte ITEM index; int size from
the subtype flags; float 4; record = members at their offsets, parents first;
tuple = (subtype>>8) elements of the element type. A type with flags 0 takes
everything from its parent (TagTools' superType)."""

K_BOOL, K_STR, K_INT, K_FLT, K_PTR, K_REC, K_ARR, K_TUP = 2, 3, 4, 5, 6, 7, 8, 0x28

class Reader:
    def __init__(self, b):
        self.b = b; self.r = parse(b); self.types = self.r["types"]
        self.items = self.r["items"]; self.d0 = self.r["secs"]["DATA"][0]
        self.cache = {}
    def sup(self, ti):
        t = self.types[ti]
        while not (t.get("flags", 0) & 1): t = self.types[t["parent"]]
        return t
    def size(self, ti):
        t = self.sup(ti)
        if "size" in t: return t["size"]
        st = t["subtype"]; k = st & 0xff
        if k == K_TUP: return (st >> 8) * self.size(t["elem"])
        raise KeyError(("no size", t["name"]))
    def members(self, t):
        out = []
        if t.get("parent"): out += self.members(self.types[t["parent"]])
        out += t.get("members", [])
        return out
    def item(self, idx):
        if idx == 0: return None
        if idx in self.cache: return self.cache[idx]
        self.cache[idx] = "<cycle>"      # a pointer back up the tree stops here
        ti, fl, off, cnt = self.items[idx]
        sz = self.size(ti)
        vals = [self.read(ti, self.d0 + off + i * sz) for i in range(cnt)]
        if self.sup(ti)["name"] == "char": vals = bytes(v & 0xff for v in vals).rstrip(b"\0").decode("latin1")
        self.cache[idx] = vals
        return vals
    def read(self, ti, off):
        t0 = self.types[ti]; t = self.sup(ti); st = t["subtype"]; k = st & 0xff
        if k & 0x80 and (k & 0x7f) == K_STR: k = K_STR
        b = self.b
        if k == K_BOOL: return b[off] != 0
        if k in (K_STR, K_PTR, K_ARR):
            idx = struct.unpack_from("<I", b, off)[0]
            v = self.item(idx)
            if k == K_PTR and isinstance(v, list): v = v[0] if len(v) == 1 else v
            return v
        if k == K_INT:
            n = 1 if st & 0x2000 else 2 if st & 0x4000 else 4 if st & 0x8000 else 8 if st & 0x10000 else 4
            return int.from_bytes(b[off:off+n], "little", signed=bool(st & 0x200))
        if k == K_FLT: return struct.unpack_from("<f", b, off)[0]
        if k == K_TUP:
            n = st >> 8; es = self.size(t["elem"])
            return [self.read(t["elem"], off + i * es) for i in range(n)]
        if k == K_REC and t0["name"] == "hkHalf16":
            return struct.unpack("<f", bytes(2) + b[off:off+2])[0]
        if k == K_REC:
            d = {"__t": t0["name"]}
            for name, mfl, moff, mti in self.members(t0):
                d[name] = self.read(mti, off + moff)
            return d
        return f"<kind {k:#x} {t['name']}>"

def trim(v, n=8):
    if isinstance(v, dict): return {k: trim(x, n) for k, x in v.items()}
    if isinstance(v, list):
        if len(v) > n: return [trim(x, n) for x in v[:n]] + [f"... {len(v)} total"]
        return [trim(x, n) for x in v]
    return v

# ---------------------------------------------------------------- shapes to triangles
"""Turn a parsed WoT .havok into triangles. Shared vertices: 64-bit packed
x 21 bits / y 21 bits / z 22 bits over the tree domain. Packed vertices:
32-bit x 11 / y 11 / z 10 bits scaled by the section's codecParms
(offset xyz, scale xyz). Primitive = 4 uint8 local indices; a quad when the
4th differs from the 3rd."""

def walk(v, f):
    if isinstance(v, dict):
        if f(v): yield v
        for x in v.values(): yield from walk(x, f)
    elif isinstance(v, list):
        for x in v: yield from walk(x, f)

def mesh_tree_tris(t, convex_pieces=False):
    dom = t["domain"]; mn, mx = dom["min"], dom["max"]
    shared = t["sharedVertices"] or []; sidx = t["sharedVerticesIndex"] or []
    packed = t["packedVertices"] or []
    def shared_v_raw(p):
        return (mn[0] + (p & 0x1FFFFF) * (mx[0]-mn[0]) / 0x1FFFFF,
                mn[1] + ((p >> 21) & 0x1FFFFF) * (mx[1]-mn[1]) / 0x1FFFFF,
                mn[2] + ((p >> 42) & 0x3FFFFF) * (mx[2]-mn[2]) / 0x3FFFFF)
    def shared_v(i): return shared_v_raw(shared[sidx[i]])
    tris = []
    if not t["sections"] or not t["primitives"]: return tris     # an empty tree
    if convex_pieces:
        # convex pieces: sharedVerticesIndex holds (info, firstVertex) pairs;
        # each piece is the shared vertices from firstVertex to the next first.
        # entry = (info, firstVertex) and one extra word when info & 0x40 is set
        # (seen: 0x1112 no extra, 0x4F52 / 0x1052 with an extra 0x3C24)
        # info & 0x80: the piece is an EXTERN shape instance (info, first, externIndex, 0),
        # drawn from shape.externShapes by the caller, so it owns no vertex range here.
        firsts = []; i = 0
        while i + 1 < len(sidx):
            info, first = sidx[i], sidx[i + 1]
            if info & 0x80: i += 4; continue
            firsts.append(first); i += 3 if info & 0x40 else 2
        firsts.append(len(shared))
        for k in range(len(firsts) - 1):
            pts = [shared_v_raw(shared[j]) for j in range(firsts[k], firsts[k + 1])]
            tris += convex_tris(pts)
        return tris
    for s in t["sections"]:
        cp = s["codecParms"]; npk = s["numPackedVertices"]
        def packed_v(i):
            p = packed[s["firstPackedVertexIndex"] + i]
            return (cp[0] + (p & 0x7FF) * cp[3], cp[1] + ((p >> 11) & 0x7FF) * cp[4], cp[2] + ((p >> 22) & 0x3FF) * cp[5])
        def vert(i):
            return packed_v(i) if i < npk else shared_v(s["firstSharedVertexIndex"] + i - npk)
        for k in range(s["numPrimitives"]):
            a, b, c, d = t["primitives"][s["firstPrimitiveIndex"] + k]["indices"]
            if a == c or a == b or b == c: continue        # degenerate = an unused slot (seen as 222,173,222,173)
            nv = npk + len(sidx) - s["firstSharedVertexIndex"]
            if max(a, b, c, d) >= nv: continue              # out of range: not a vertex reference
            va, vb, vc = vert(a), vert(b), vert(c)
            tris.append((va, vb, vc))
            if d != c: tris.append((va, vc, vert(d)))
    return tris

def convex_tris(pts):
    """Triangles of the convex hull of a point set. A flat set (a planar
    polygon, common for trigger volumes and thin plates) falls back to a
    2-D hull in its own plane, both faces."""
    import numpy as np
    if len(pts) < 3: return []
    a = np.array(pts, dtype=float)
    try:
        from scipy.spatial import ConvexHull
        ch = ConvexHull(a)
        return [tuple(pts[i] for i in simp) for simp in ch.simplices]
    except Exception:
        pass
    c = a.mean(0); u, s, vt = np.linalg.svd(a - c)
    ax = vt[:2]; p2 = (a - c) @ ax.T
    try:
        from scipy.spatial import ConvexHull
        order = ConvexHull(p2).vertices
    except Exception:
        return [tuple(pts[:3])] if len(pts) >= 3 else []
    ring = [pts[i] for i in order]; out = []
    for k in range(1, len(ring) - 1):
        out.append((ring[0], ring[k], ring[k + 1])); out.append((ring[0], ring[k + 1], ring[k]))
    return out

def hull_tris(h, radius=0.0):
    vs = [(v["x"], v["y"], v["z"]) for v in h["vertices"]]; idx = h["indices"]; tris = []
    if not h["faces"]:
        # no face table: a sphere (1 vertex + convexRadius), a capsule (2) or a
        # bare point cloud. Build the hull ourselves when there are enough points.
        return convex_tris(vs)
    for f in h["faces"]:
        fi = idx[f["firstIndex"]: f["firstIndex"] + f["numIndices"]]
        for k in range(1, len(fi) - 1): tris.append((vs[fi[0]], vs[fi[k]], vs[fi[k+1]]))
    return tris

def bodies(root):
    """(name, position, orientation, shape) for every body cinfo."""
    return [(b.get("name"), b["position"], b["orientation"], b["shape"]) for b in walk(root, lambda d: d["__t"].endswith("BodyCinfo") and "position" in d)]

def shape_tris(sh, xf=None):
    out = []
    if sh is None: return out
    t = sh["__t"]
    if t == "hknpCompressedMeshShape":
        out += mesh_tree_tris(sh["data"]["meshTree"], sh.get("numTriangles", 1) == 0 and sh.get("numConvexShapes", 0) > 0)
        for inst in sh.get("externShapes") or []:            # convex pieces stored as shape instances
            out += instance_tris(inst)
    elif t == "hknpTriangleShape":
        vs = [(v["x"], v["y"], v["z"]) for v in sh["hull"]["vertices"]]
        out.append(tuple(vs[:3]))
        if len(vs) == 4: out.append((vs[0], vs[2], vs[3]))
    elif t in ("hknpConvexShape", "hknpConvexPolytopeShape", "hknpCapsuleShape", "hknpSphereShape", "hknpBoxShape", "hknpCylinderShape"): out += hull_tris(sh["hull"], sh.get("convexRadius", 0.0))
    elif t == "hknpCompoundShape":
        for inst in sh["instances"]["elements"]: out += instance_tris(inst)
    else: print("unhandled shape", t, file=sys.stderr)
    return out

def instance_tris(inst):
    """An hknpShapeInstance: its shape's triangles through rotation, translation, scale."""
    if not inst or inst.get("shape") is None: return []
    q = inst["rotation"]; tr = inst["translation"]; sc = inst["scale"]
    return [tuple(rot(q, (p[0]*sc["x"], p[1]*sc["y"], p[2]*sc["z"]), (tr["x"], tr["y"], tr["z"])) for p in tri) for tri in shape_tris(inst["shape"])]

def rot(q, p, t):
    x, y, z, w = q; px, py, pz = p
    # q * p * q^-1
    ix =  w*px + y*pz - z*py; iy =  w*py + z*px - x*pz; iz =  w*pz + x*py - y*px; iw = -x*px - y*py - z*pz
    return (ix*w - iw*x - iy*z + iz*y + t[0], iy*w - iw*y - iz*x + ix*z + t[1], iz*w - iw*z - ix*y + iy*x + t[2])

def bounds(tris):
    xs = [p[i] for tri in tris for p in tri for i in (0,)]; ys = [p[1] for tri in tris for p in tri]; zs = [p[2] for tri in tris for p in tri]
    return (min(xs), min(ys), min(zs)), (max(xs), max(ys), max(zs))

def write_obj(path, groups):
    n = 1
    with open(path, "w") as f:
        for name, tris in groups:
            f.write(f"g {name}\n")
            for tri in tris:
                for p in tri: f.write(f"v {p[0]:.5f} {p[1]:.5f} {p[2]:.5f}\n")
                f.write(f"f {n} {n+1} {n+2}\n"); n += 3

# ---------------------------------------------------------------- BigWorld packed XML (for material_kinds.xml)
"""BigWorld packed XML (magic 0x62A14E45) -> nested dict. Types: 0 section,
1 string, 2 int, 3 floats, 4 bool, 5 blob."""
def decode(b):
    assert struct.unpack_from("<I", b, 0)[0] == 0x62A14E45, "not packed xml"
    p = 5; strs = []
    while True:
        e = b.index(b"\0", p); s = b[p:e].decode("latin1"); p = e + 1
        if s == "": break
        strs.append(s)
    def section(p):
        n = struct.unpack_from("<H", b, p)[0]; p += 2
        own = struct.unpack_from("<I", b, p)[0]; p += 4
        kids = []
        for _ in range(n):
            ni, d = struct.unpack_from("<HI", b, p); p += 6; kids.append((strs[ni], d))
        base = p; prev = own & 0x0fffffff
        val = value(own >> 28, b[base - (own & 0x0fffffff):base]) if False else None
        # own value occupies [p, p+ownlen)
        ownlen = own & 0x0fffffff; owntype = own >> 28
        val = value(owntype, b[p:p+ownlen]); p += ownlen; prev = ownlen
        out = {"": val} if val not in (None, "") else {}
        for name, d in kids:
            ln = (d & 0x0fffffff) - prev; ty = d >> 28
            chunk = b[p:p+ln]
            v = section_from(chunk) if ty == 0 else value(ty, chunk)
            out.setdefault(name, []).append(v); p += ln; prev = d & 0x0fffffff
        return out
    def section_from(chunk):
        # nested section: same layout, offsets relative to its own start
        nonlocal b
        saved = b; b = chunk
        try: return section(0)
        finally: b = saved
    def value(ty, c):
        if ty == 1: return c.decode("latin1")
        if ty == 2: return int.from_bytes(c, "little", signed=True) if c else 0
        if ty == 3: return list(struct.unpack("<%df" % (len(c)//4), c))
        if ty == 4: return len(c) > 0
        if ty == 5: return c
        return c
    return section(p)

# ---------------------------------------------------------------- CLI

def read_source(src):
    if ":" in src and not os.path.exists(src):
        pkg, entry = src.split(":", 1)
        return zipfile.ZipFile(os.path.join(WOT_PACKAGES, pkg)).read(entry)
    return open(src, "rb").read()

def material_kinds():
    """id -> name from system/data/material_kinds.xml in misc.pkg (packed XML)."""
    try:
        d = decode(zipfile.ZipFile(os.path.join(WOT_PACKAGES, "misc.pkg")).read("system/data/material_kinds.xml"))
        return {k["id"][0]: (k["desc"][0] if isinstance(k["desc"][0], str) else "?") for k in d["kind"]}
    except Exception:
        return {}

def info(rt, mk=None):
    mk = mk or {}
    for nv in rt[0]["namedVariants"] or []:
        v = nv["variant"]
        print(f"variant {nv['name']!r} {nv['className']} -> {v['__t'] if isinstance(v, dict) else v}")
        for h in (v.get("resourceHandles") or []) if isinstance(v, dict) else []:
            hv = h["variant"]
            if not isinstance(hv, dict): continue
            print(f"  handle {h.get('name')!r} -> {hv['__t']}")
            if hv["__t"] == "hknpPhysicsSystemData":
                for b in hv["bodyCinfos"]:
                    sh = b["shape"]; tris = shape_tris(sh)
                    kinds = sorted({i["shape"]["__t"][4:] for i in sh["instances"]["elements"] if i["shape"]}) if sh and sh["__t"] == "hknpCompoundShape" else []
                    bb = bounds(tris) if tris else None
                    print(f"    body {b.get('name')!r:30} {sh['__t'][4:] if sh else None:22} tris {len(tris):6d} motion {b['motionType']} filter {b['collisionFilterInfo']} {kinds} pos {[round(x,3) for x in b['position'][:3]]} bounds {bb and [[round(x,3) for x in p] for p in bb]}")
            elif hv["__t"] == "HKBodyFlagsData":
                for i in hv["flags_"]:
                    print(f"    flags {i['name']!r:30} collisionFlags {i['collisionFlags']} normalMatKind {i['normalMatKind']} ({mk.get(i['normalMatKind'], '?')}) destroyedMatKind {i['destroyedMatKind']} ({mk.get(i['destroyedMatKind'], '-')})")
                print(f"    bounds {[round(x,3) for x in hv['minBounds_'][:3]]} {[round(x,3) for x in hv['maxBounds_'][:3]]}")

def main(argv):
    if len(argv) < 3: print(__doc__); return 1
    cmd, src = argv[1], argv[2]
    b = read_source(src); rd = Reader(b); rt = rd.item(1)
    if cmd == "info": info(rt, material_kinds())
    elif cmd == "dump": print(json.dumps(trim(rt, 64), indent=1, default=str))
    elif cmd == "types":
        for i, t in enumerate(rd.types):
            if not t: continue
            print(f"[{i}] {tname(rd.types, i)} parent={t.get('parent')} flags={t.get('flags')} sub={t.get('subtype')} size={t.get('size')}")
            for m in t.get("members", []): print(f"      +{m[2]:<4} {m[0]:<28} {tname(rd.types, m[3])}")
    elif cmd == "obj":
        groups = []
        for name, pos, ori, sh in bodies(rt):
            tris = [tuple(rot(ori, p, pos[:3]) for p in tri) for tri in shape_tris(sh)]
            groups.append((name or "body", tris))
        write_obj(argv[3], groups); print("wrote", argv[3], sum(len(g[1]) for g in groups), "triangles")
    else: print(__doc__); return 1
    return 0

if __name__ == "__main__":
    sys.exit(main(sys.argv))
