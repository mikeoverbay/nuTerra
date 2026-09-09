# -*- coding: utf-8 -*-
"""Terrain-only flight bake from the game pkg, and the map's global_AM.

WHAT THIS IS
    Path Studio needs a bake - top.r32, floor.r32, mask.png, meta.txt in
    %TEMP%\\nuTerra\\flight - before it can show a map or plan a route. nuTerra
    writes one on map load (MapFlightBake: two depth renders of the loaded map,
    terrain + models + trees). This is the Python stand-in for maps that have
    never been opened: it reads the terrain heights straight out of the pkg and
    writes the same four files.

WHAT IT IS NOT
    Terrain plus water. Water bodies are raised to their surface as
    MapFlightBake does, so rivers read as a flat surface, not a trench. There
    are no models and no trees here, so `top` equals `floor` and the obstacle
    mask is empty. A route planned on this bake flies
    the ground and knows nothing about buildings. Open the map once in nuTerra
    for the real bake; this one is enough to place lights and see the map.
    meta.txt says `source=python-terrain` so nothing mistakes one for the other.

HOW THE HEIGHTS ARE FOUND
    <game>/res/packages/<map>.pkg holds spaces/<map>/<xxxx><yyyy>o.cdata_processed,
    one zip per 100 m chunk, name = signed 16-bit hex chunk x and z. Inside,
    terrain2/heights is a 36-byte BigWorld header then a 69x69 RGBA PNG whose
    pixels are int32 millimetres. The 69 samples span 106.25 m (68 steps of
    100/64 m) with a two-sample margin, and the chunk's X runs mirrored:
    world_x = -(cx + 1) * 100 .. -cx * 100, world_z = cz * 100 .. (cz + 1) * 100.
    That mapping was not derived - it was SEARCHED: every orientation, mirror,
    span and offset was rasterised and compared against nuTerra's own
    19_monastery floor.r32; with the water raise below, the winner matches
    nuTerra's floors to 0.00-0.04 m RMS on every map that has one (karelia,
    airfield, mannerheim, poland, westfeld, desert, monastery). Do not change
    it without re-running that comparison.

GAME PATH
    Read from nuTerra's own user.config files under %LOCALAPPDATA%\\nuTerra
    (setting GamePath); every root found is tried. Or pass it.

    python tools/terrain_bake.py <map> [--game <root>] [--size 1024]
"""
import glob, io, os, re, struct, sys, zipfile
import numpy as np
from PIL import Image
from scipy import ndimage

# 2048, not 1024. Over a 1200 m map that is 0.59 m per texel instead of
# 1.17 - a 3 m wall is five cells across rather than two, which is the
# difference between the router seeing a building and seeing a smudge.
# flight_plan.Bake reads width/height out of the meta, so a bake at any
# size still loads and old files keep working.
SIZE = 2048
FOLDER = os.path.join(os.environ.get("TEMP", "."), "nuTerra", "flight")
EMPTY = -9999.0
CHUNK_M = 100.0
SAMPLES = 69
SPAN_M = CHUNK_M * 68.0 / 64.0        # 106.25: what the 68 steps of the 69 grid cover
MARGIN_M = -2.0 * CHUNK_M / 64.0      # sample 0 sits two steps before the chunk edge


def game_roots():
    """GAME_PATH values from nuTerra's user.config files, newest first."""
    roots = []
    base = os.path.join(os.environ.get("LOCALAPPDATA", ""), "nuTerra")
    files = sorted(glob.glob(os.path.join(base, "*", "*", "user.config")),
                   key=os.path.getmtime, reverse=True)
    for f in files:
        try:
            txt = open(f, encoding="utf-8", errors="replace").read()
        except OSError:
            continue
        for m in re.finditer(r'name="GamePath"[^>]*>\s*<value>([^<]+)</value>', txt, re.I):
            v = m.group(1).strip()
            if v and v not in roots:
                roots.append(v)
    return roots


def find_pkg(map_name, game=None):
    roots = [game] if game else game_roots()
    for r in roots:
        p = os.path.join(r, "res", "packages", map_name + ".pkg")
        if os.path.exists(p):
            return p
    raise FileNotFoundError("no %s.pkg under res/packages of %s" % (map_name, roots or "any known game path"))


def _s16(h):
    v = int(h, 16)
    return v - 0x10000 if v >= 0x8000 else v


def read_chunks(pkg, map_name):
    """{(cx, cz): 69x69 float metres}, row = png row, col = png col."""
    z = zipfile.ZipFile(pkg)
    pat = re.compile(r"spaces/%s/([0-9a-f]{4})([0-9a-f]{4})o\.cdata_processed$" % re.escape(map_name), re.I)
    chunks = {}
    for n in z.namelist():
        m = pat.match(n)
        if not m:
            continue
        inner = zipfile.ZipFile(io.BytesIO(z.read(n)))
        if "terrain2/heights" not in inner.namelist():
            continue
        h = inner.read("terrain2/heights")
        _magic, w, hh, _comp, _ver, _hmin, _hmax, _a, _b = struct.unpack("<5I2f2I", h[:36])
        im = Image.open(io.BytesIO(h[36:])); im.load()
        if im.mode != "RGBA" or im.size != (w, hh):
            raise ValueError("%s: unexpected heights png %s %s" % (n, im.mode, im.size))
        a = np.frombuffer(im.tobytes(), dtype="<i4").reshape(hh, w).astype(np.float64) * 0.001
        chunks[(_s16(m.group(1)), _s16(m.group(2)))] = a
    if not chunks:
        raise ValueError("no terrain chunks in " + pkg)
    return chunks


def footprint(chunks):
    xs = [c[0] for c in chunks]; zs = [c[1] for c in chunks]
    # X is mirrored: chunk cx covers -(cx+1)*100 .. -cx*100
    wx_min = -(max(xs) + 1) * CHUNK_M
    wx_max = -min(xs) * CHUNK_M
    wz_min = min(zs) * CHUNK_M
    wz_max = (max(zs) + 1) * CHUNK_M
    return wx_min, wx_max, wz_min, wz_max


def footprint_of(map_name, game=None):
    """The map's world bounds, from the pkg's chunk NAMES alone.

    read_chunks + footprint gives the same answer and decodes every chunk's
    height png to do it, which is the slow part of a bake. The bounds are in
    the file names - one namelist and some hex - so a map with no bake can
    still be placed in the world and drawn.
    """
    pkg = find_pkg(map_name, game)
    pat = re.compile(r"spaces/%s/([0-9a-f]{4})([0-9a-f]{4})o\.cdata_processed$"
                     % re.escape(map_name), re.I)
    coords = []
    with zipfile.ZipFile(pkg) as z:
        for n in z.namelist():
            m = pat.match(n)
            if m:
                coords.append((_s16(m.group(1)), _s16(m.group(2))))
    if not coords:
        raise ValueError("no terrain chunks in " + pkg)
    return footprint(dict.fromkeys(coords))


def rasterise(chunks, size=SIZE):
    """Floor heights on a size x size grid over the footprint; row 0 = wz_max."""
    wx_min, wx_max, wz_min, wz_max = footprint(chunks)
    out = np.full((size, size), EMPTY)
    cols = np.arange(size); rows = np.arange(size)
    xs = wx_min + (cols + 0.5) * (wx_max - wx_min) / size
    zs = wz_max - (rows + 0.5) * (wz_max - wz_min) / size
    n = SAMPLES
    for (cx, cz), a in chunks.items():
        g = a[:, ::-1]                        # X mirrored within the chunk
        ox = -(cx + 1) * CHUNK_M
        oz = cz * CHUNK_M
        cm = (xs >= ox) & (xs < ox + CHUNK_M)
        rm = (zs >= oz) & (zs < oz + CHUNK_M)
        if not cm.any() or not rm.any():
            continue
        fc = (xs[cm] - ox - MARGIN_M) / SPAN_M * (n - 1)
        fr = (zs[rm] - oz - MARGIN_M) / SPAN_M * (n - 1)
        FR, FC = np.meshgrid(fr, fc, indexing="ij")
        out[np.ix_(rm, cm)] = ndimage.map_coordinates(
            g, [FR.ravel(), FC.ravel()], order=1, mode="nearest").reshape(FR.shape)
    return out, (wx_min, wx_max, wz_min, wz_max)


def read_water(pkg, map_name):
    """Water bodies from space.bin's BWWa section: [(x0, x1, z0, z1, y)] in
    world metres, X already mirrored the way every loader here mirrors it.

    space.bin: at 0x14 an int32 table size, then 24-byte entries of
    magic(4) version(i32) offset(i64) length(i64). BWWa (version 3): u32 entry
    size, u32 count, then count entries whose first six floats are bbox min and
    bbox max with equal Y - the water rectangle at surface height."""
    z = zipfile.ZipFile(pkg)
    want = ("spaces/%s/space.bin" % map_name).lower()
    name = next((n for n in z.namelist() if n.lower() == want), None)
    if name is None:
        return []
    raw = z.read(name)
    (table,) = struct.unpack_from("<i", raw, 0x14)
    pos = 0x18
    sections = {}
    for _ in range(table):
        magic = raw[pos:pos + 4].decode("ascii", "replace")
        version, offset, length = struct.unpack_from("<iqq", raw, pos + 4)
        sections[magic] = (version, offset, length)
        pos += 24
    if "BWWa" not in sections:
        return []
    _ver, off, _len = sections["BWWa"]
    ds, count = struct.unpack_from("<II", raw, off)
    bodies = []
    for e in range(count):
        base = off + 8 + e * ds
        x0, y0, z0, x1, y1, z1 = struct.unpack_from("<6f", raw, base)
        bodies.append((min(-x0, -x1), max(-x0, -x1), min(z0, z1), max(z0, z1), y0))
    return bodies


def add_water(floor, fp, bodies):
    """Raise floor to each body's surface inside its rectangle - MapFlightBake
    does the same, so a river reads as a flat surface, not a trench. Returns the
    number of cells raised."""
    size = floor.shape[0]
    wx_min, wx_max, wz_min, wz_max = fp
    raised = 0
    for x0, x1, z0, z1, y in bodies:
        c0 = max(0, int(np.floor((x0 - wx_min) / (wx_max - wx_min) * size)))
        c1 = min(size, int(np.ceil((x1 - wx_min) / (wx_max - wx_min) * size)))
        r0 = max(0, int(np.floor((wz_max - z1) / (wz_max - wz_min) * size)))
        r1 = min(size, int(np.ceil((wz_max - z0) / (wz_max - wz_min) * size)))
        if c1 <= c0 or r1 <= r0:
            continue
        block = floor[r0:r1, c0:c1]
        low = (block < y) & (block > EMPTY + 1)
        raised += int(low.sum())
        block[low] = y
    return raised


def write_bake(map_name, floor, fp, folder=FOLDER, top=None, n_boxes=0):
    os.makedirs(folder, exist_ok=True)
    stem = os.path.join(folder, map_name)
    size = floor.shape[0]
    f32 = floor.astype("<f4")
    open(stem + "_floor.r32", "wb").write(f32.tobytes())
    # top carries the model boxes when there are any; without them it is the
    # ground again and the router sees a map with nothing standing on it.
    t32 = f32 if top is None else top.astype("<f4")
    open(stem + "_top.r32", "wb").write(t32.tobytes())
    Image.fromarray(np.zeros((size, size), np.uint8), "L").convert("RGBA").save(stem + "_mask.png")
    wx_min, wx_max, wz_min, wz_max = fp
    lines = [
        "# nuTerra flight bake",
        "map=" + map_name,
        "width=%d" % size, "height=%d" % size,
        "wx_min=%.3f" % wx_min, "wx_max=%.3f" % wx_max,
        "wz_min=%.3f" % wz_min, "wz_max=%.3f" % wz_max,
        "empty=%.3f" % EMPTY,
        "obstacle_min_h=1.000",
        "source=" + ("python-boxes" if top is not None else "python-terrain"),
        "model_boxes=%d" % n_boxes,
        "#",
    ] + ([
        "# TERRAIN + MODEL BOXES - written by tools/terrain_bake.py.",
        "# top is the ground raised to each placed model's visibility box, read",
        "# from the map's own space.bin (BSMI transforms, BSMO bounds). Trees",
        "# are NOT in it - those live in the SpTr section and are not read yet.",
        "#",
        "# A box is not geometry. Visibility bounds carry culling margin and are",
        "# solid where the model is not, so this marks noticeably more ground",
        "# blocked than nuTerra's GPU bake, which rasterises real triangles:",
        "# measured 41.7%% against 24.1%% on 07_lakeville. It errs toward calling",
        "# ground blocked, which is the right way round for a router, but open",
        "# the map in nuTerra when you can - that bake is the accurate one.",
    ] if top is not None else [
        "# TERRAIN ONLY - written by tools/terrain_bake.py from the pkg heights.",
        "# top equals floor: no models, no trees, no obstacles. Open the map once",
        "# in nuTerra for the real bake, which overwrites this one.",
    ]) + [
        "#",
        "# row 0 is the wz_max edge, rows increase toward wz_min",
        "# col 0 is the wx_min edge, cols increase toward wx_max",
        "# world_x = wx_min + (col + 0.5) * (wx_max - wx_min) / width",
        "# world_z = wz_max - (row + 0.5) * (wz_max - wz_min) / height",
    ]
    open(stem + "_meta.txt", "w", encoding="utf-8").write("\n".join(lines) + "\n")
    return stem


def bake(map_name, game=None, size=SIZE, folder=FOLDER, log=print):
    pkg = find_pkg(map_name, game)
    log("terrain bake: reading %s" % pkg)
    chunks = read_chunks(pkg, map_name)
    log("terrain bake: %d chunks" % len(chunks))
    floor, fp = rasterise(chunks, size)
    bodies = read_water(pkg, map_name)
    raised = add_water(floor, fp, bodies)
    log("terrain bake: %d water bodies raised %d cells" % (len(bodies), raised))

    # Models, as their visibility boxes out of space.bin. Without these the
    # router sees no buildings at all and will plan straight through a town.
    top = None
    n_boxes = 0
    try:
        import space_models
        inst = space_models.read_instances(pkg, map_name)
        top = floor.copy()
        n_boxes = space_models.stamp(top, fp, inst)
        log("terrain bake: %d model instances, %d boxes stamped" % (len(inst), n_boxes))
    except Exception as e:
        top = None
        log("terrain bake: no model boxes (%s) - top will equal floor" % e)

    stem = write_bake(map_name, floor, fp, folder, top=top, n_boxes=n_boxes)
    log("terrain bake: wrote %s_{floor,top}.r32 / _mask.png / _meta.txt  footprint x %.0f..%.0f z %.0f..%.0f"
        % (stem, fp[0], fp[1], fp[2], fp[3]))
    return stem


def bake_is_python(folder, map_name):
    p = os.path.join(folder, map_name + "_meta.txt")
    try:
        # ANY python source, not one exact spelling. The writer emits
        # "source=python-boxes" now that it records model boxes, and this
        # still looked for "source=python-terrain" - so every python bake
        # reported as a nuTerra one, and the "replace the real bake?" guard
        # in Path Studio warned about overwriting something it had written
        # itself while giving no warning for the case it exists to catch.
        return "source=python" in open(p, encoding="utf-8", errors="replace").read()
    except OSError:
        return False


def load_global_am(map_name, game=None):
    """The map's global_AM.dds as a PIL image, or None if the pkg has none."""
    pkg = find_pkg(map_name, game)
    z = zipfile.ZipFile(pkg)
    want = ("spaces/%s/global_am.dds" % map_name).lower()
    for n in z.namelist():
        if n.lower() == want:
            im = Image.open(io.BytesIO(z.read(n))); im.load()
            return im.convert("RGB")
    return None


def main(argv):
    args = [a for a in argv[1:] if not a.startswith("--")]
    if not args:
        print(__doc__); return 1
    game = argv[argv.index("--game") + 1] if "--game" in argv else None
    size = int(argv[argv.index("--size") + 1]) if "--size" in argv else SIZE
    bake(args[0], game=game, size=size)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
