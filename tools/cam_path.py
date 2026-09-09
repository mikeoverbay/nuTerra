"""
The .campath binary format - camera flight paths for nuTerra.

One file is one flight AND the seed that produced it: a header, a flat array of
fixed-size point records, then the handful of points that were clicked to make
them. Sequential, no seeking, no parsing. Reading it in VB.NET is a BinaryReader
loop, which is the point - the clever part happens here in Python and nuTerra
just plays back the numbers.

Kept in its own module rather than inside the exporter so the writer and any
future reader cannot drift apart. If the layout below changes, it changes in one
place, and LAYOUT_DOC is the text to paste into the VB side.

--------------------------------------------------------------------------
Version 2 - why the magic changed
--------------------------------------------------------------------------
The magic is "NCP2", not "NCP1" with a bumped version field. The header grew
from 64 bytes to 128, so a version 1 reader pointed at a version 2 file would
start reading points from offset 64 - the middle of the header - and get
plausible-looking garbage rather than an error. A flight through the ground is a
much worse failure than a refusal to load. nuTerra already logs "bad magic" and
gives up cleanly, so changing it makes an old build fail in the one way that
cannot be mistaken for working.

Version 1 files are not readable and not upgradable: they never carried a seed,
so there is nothing to convert them from.

--------------------------------------------------------------------------
Header - 128 bytes, little endian
--------------------------------------------------------------------------
  off  type     field
    0  char[4]  magic         "NCP2"
    4  uint16   version       2
    6  uint16   flags         bit 0 set = closed loop (last point joins first)
    8  uint32   count         number of point records
   12  uint32   stride        bytes per point record, 32
   16  float32  total_len     path length in metres
   20  char[40] map           map name, ASCII, null padded
   60  uint32   header_size   128. Points start here, seeds after them.
   64  int64    created       unix seconds UTC when the file was written
   72  uint32   seed_count    number of seed records, may be 0
   76  uint32   seed_stride   bytes per seed record, 12
   80  float32  seed_heading  departure heading, radians, same convention as
                              a point's heading
   84  float32  seed_radius   loop radius asked for, metres
   88  uint32   seed_points   waypoints asked for around the ring
   92  int32    seed_side     the turn direction verbatim as Path Studio
                              carries it: +1 left, -1 right. SIGNED, and
                              stored without translation - mapping it to
                              0/1 lost the distinction, because -1 is
                              truthy and both sides came out the same.
   96  uint32   light_count   number of light records, may be 0
  100  uint32   light_stride  bytes per light record, 72 (36 before the
                              shape fields, 32 before `curve`)
  104  uint32   bulb_count    number of bulb records, may be 0
  108  uint32   bulb_stride   bytes per bulb record, 232 (224 before the two
                              angles). Skip by THIS, never by the constant.
  112  char[16] reserved      zeroed

The light fields come out of what version 2 already reserved, so the header is
still 128 bytes and this is still NCP2. Every file written before lights existed
has zeros there and reads back as "no lights" without a special case.

Read `header_size` and `stride` and skip by them rather than assuming. A later
version can grow either end without breaking a reader that respects them.

--------------------------------------------------------------------------
Point record - 32 bytes, 8 x float32, little endian
--------------------------------------------------------------------------
    0  x, y, z     eye position in world metres. y is absolute, not AGL.
   12  heading     yaw in radians. atan2(dx, dz): 0 looks down +Z, and it
                   increases toward +X. Same convention flight_plan.py uses.
   16  tilt        pitch in radians. POSITIVE LOOKS UP, negative looks down.
   20  roll        bank in radians. POSITIVE BANKS RIGHT (right side down),
                   which is the direction of a right hand turn.
   24  s           distance from the first point along the path, metres.
   28  speed       metres per second at this point.

`s` and `speed` are both derivable from the positions, and are stored anyway:
they cost 8 bytes per point and save the playback code from having to integrate
arc length before it can do anything.

--------------------------------------------------------------------------
Seed record - 12 bytes, at header_size + count * stride
--------------------------------------------------------------------------
    0  float32  x     world metres
    4  float32  z     world metres
    8  uint32   kind  0 = start, 1 = target, in the order they were placed

These are what was CLICKED, not what was flown - the start the drag began at
and the targets the route was told to visit. The flown path is a consequence of
them plus the terrain, and cannot be reversed back into them, which is why they
are stored rather than derived. With the seed in the file a route can be
reproduced, adjusted and regenerated later; without it, the only record of the
intent was in the operator's head.

--------------------------------------------------------------------------
Bulb record - 232 bytes, after the lights. A BULB is a light attached to a
MODEL: it is placed once, in the model's own space, by the Light Bulb Placer,
and nuTerra puts one at every instance of that model on the map. The lights
above are placed on the map by Path Studio; these are placed on a model. Both
end up in the same lamp path.

  off  type       field
    0  char[160]  primitives  the model's .primitives path, UTF-8, NUL padded.
                              The only identity a model has across maps, and
                              the key the Placer's list shows.
  160  uint32     type        0 point, 1 cone, 2 inverse cone (omni EXCEPT
                              inside the cone - a cowled street lamp),
                              3 dual cowled (see ang0 / ang1)
  164  float32x3  pos         model space, metres from the model origin
  176  float32x3  aim         model space point the cone looks at. Ignored
                              for a point light.
  188  float32    cone        full cone angle, degrees
  192  float32    blend       0..1, how much of the cone is soft edge
  196  float32x3  color       sRGB 0..1, as the picker holds it
  208  float32    level       0..1, fraction of the global light gain
  212  float32    range       metres
  216  float32    vol_mix     0..1, how much it scatters into fog
  220  uint32     curve       shaft falloff curve, 0..2
  224  float32    ang0        HALF angle from the axis, degrees - see below
  228  float32    ang1        the second half angle, degrees

ang0 and ang1 are read per type, and one pair of fields serves all three aimed
kinds:

  type            lit where              ang0            ang1
  0 point         everywhere             -               -
  1 cone          inside ang1            inner hot edge  outer edge
  2 inverse cone  outside ang0           dark edge       soft-out edge
  3 dual cowled   BETWEEN ang0 and ang1  the cap cut     the base cut

The DUAL COWLED lamp is two inverse lobes on ONE shared axis, so what it lights
is a toroidal band: a lamp on a vertical post, where the cap blocks the light
going up and the post blocks it going down. With the axis pointing up, ang0 is
how much of straight-up the cap swallows and ang1 is where the post cuts the
band off on the way down; the band's width is ang1 - ang0, and `blend` softens
both of its edges.

A file written before these two fields is 224 bytes a bulb and reads back with
ang0 = ang1 = 0, which means "fall back to `blend`" and renders exactly as it
did. Readers go by the bulb_stride in the header, never by 232 - the same rule
the light record's `curve` field follows.

Light record - 72 bytes, at header_size + count * stride + seed_count * seed_stride
              (36 before the shape fields, 32 before `curve`)
--------------------------------------------------------------------------
    0  x, y, z   world metres. y is metres ABOVE THE TERRAIN, not absolute -
                 Path Studio places lights on a 2D map and has no height
                 control, so it writes 0 and nuTerra resolves the ground.
                 This is the one field here that does NOT match the point
                 record's convention, and it is deliberate.
   12  r, g, b   colour, 0..1, sRGB as authored in the picker. Not linear -
                 linearise on load, the same as any other authored colour.
   24  level     brightness, 0..1
   28  range     radius of influence in metres, 0.1 .. 50
   32  curve     which fog falloff curve the lamp's SHAFT uses, 0..2 - a row
                 of VM_FOG_Curve_<n>.png beside the .campath (tools/fog_curve.py).
                 Files written before this field are 32 bytes a light and read
                 back as curve 0; readers go by light_stride, never by 32.
   36  kind      uint32. 0 point, 1 cone, 2 inverse cone, 3 dual cowled - the
                 same kinds as the bulb record, with the same meaning.
   40  aim       x, y, z: metres FROM THE LIGHT to the point it looks at, on
                 the world axes. (0, -1, 0) is straight down. An OFFSET, not a
                 point, so the light can be moved without re-aiming it; a
                 reader normalises it into the direction. Ignored for a point.
   52  cone      full cone angle, degrees - the legacy field, kept truthful as
                 2 * ang1 for a cone, as the Bulb Placer does.
   56  blend     0..1 soft edge; for a dual cowl the softness of both cuts.
   60  ang0      the two HALF angles from the axis, degrees, read per kind
   64  ang1      exactly as the bulb record's (see there). Both 0 means
                 "fall back to blend".
   68  vol_mix   0..1, how much the light scatters into fog.
                 Files written before these are 36 bytes a light (32 before
                 curve) and read back as a point light aimed straight down
                 with vol_mix 1 - which is what nuTerra assumed for every map
                 light until now, so nothing already authored changes.

Lights sit at the TAIL, after the seeds, so a reader that only wants the flight
path can stop at seed_count and never know they are there. Their block is found
by arithmetic from the header, never by seeking from the end.

--------------------------------------------------------------------------
Angle conventions, spelled out because they are where this will go wrong
--------------------------------------------------------------------------
nuTerra's MapCamera is an orbit rig - LOOK_AT plus CAM_X_ANGLE, CAM_Y_ANGLE
and VIEW_RADIUS - so heading and tilt map onto CAM_X_ANGLE and CAM_Y_ANGLE.
Note CAM_Y_ANGLE is clamped to about -1.57 .. 1.3 there, and tilt here is
already well inside that.

Roll is played back by rotating the up vector about forward before LookAt, so
the whole view basis banks and everything downstream banks with it.
"""

import datetime
import os
import struct
import time


def campath_dir():
    """The folder .campath files live in.

    SEARCHED, not computed. It used to be two directories up from this file plus
    "nuTerra/cam_paths", which is right in the repo and wrong everywhere else -
    running from the copy PathStudio deploys, it resolved to
    PathStudio\\bin\\Debug\\net6.0-windows\\nuTerra\\cam_paths, a folder that
    does not exist. Path Studio then found no saved path to draw, and the
    exporter would have written new ones into bin.

    Walk up from this file and take the first that exists:

        <dir>/nuTerra/cam_paths   the repo, and a deployed copy under it
        <dir>/cam_paths           installed beside nuTerra.exe

    When neither exists yet - a first run on a fresh install - fall back to
    whichever candidate has a parent that does, so the first export lands
    somewhere sensible instead of creating a stray tree.
    """
    here = os.path.dirname(os.path.abspath(__file__))
    d = here
    while True:
        for cand in (os.path.join(d, "nuTerra", "cam_paths"),
                     os.path.join(d, "cam_paths")):
            if os.path.isdir(cand):
                return cand
        parent = os.path.dirname(d)
        if parent == d:
            break
        d = parent

    d = here
    while True:
        if os.path.isdir(os.path.join(d, "nuTerra")):
            return os.path.join(d, "nuTerra", "cam_paths")
        parent = os.path.dirname(d)
        if parent == d:
            break
        d = parent
    return os.path.join(os.path.dirname(here), "cam_paths")


MAGIC = b"NCP2"
MAGIC_V1 = b"NCP1"
VERSION = 2
HEADER_SIZE = 128
STRIDE = 32
SEED_STRIDE = 12
LIGHT_STRIDE = 72        # what this writer emits: the 36 below + kind, aim,
                         # cone, blend, ang0, ang1, vol_mix
LIGHT_STRIDE_MIN = 32    # what a reader must accept: files from before `curve`
LIGHT_FMT = "<8fII3f5f"  # x y z r g b level range | curve | kind | aim xyz |
                         # cone blend ang0 ang1 vol_mix  = 72 bytes
LIGHT_FIELDS = 18
BULB_STRIDE = 232        # what this writer emits: the 224 below + ang0, ang1
BULB_STRIDE_MIN = 224    # what a reader must accept: 160-byte name + 16 words,
                         # files from before the two angles
BULB_NAME_LEN = 160
BULB_POINT, BULB_CONE, BULB_INVERSE_CONE, BULB_DUAL_COWL = 0, 1, 2, 3

# The trailing 16s is what is LEFT of version 2's 32 reserved bytes after the
# light pair and then the bulb pair were taken from the front of it. Total is
# still 128 and this is still NCP2: a file from before either reads zeros there.
HEAD_FMT = "<4sHHIIf40sIqIIffIiIIII16s"
# The record through `curve`, which every bulb file ever written has, and the
# full one. Unpack the first from any file and take the angles only when the
# stride says they are there.
BULB_FMT_MIN = "<160sI3f3fff3ffffI"
BULB_FMT = BULB_FMT_MIN + "2f"

FLAG_CLOSED = 1

SEED_START = 0
SEED_TARGET = 1

LAYOUT_DOC = __doc__


def pack_seed(start=None, heading=0.0, radius=0.0, waypoints=0, side=0,
              targets=()):
    """Gather the clicked inputs into the shape write_path wants."""
    return {
        "start": tuple(start) if start else None,
        "heading": float(heading),
        "radius": float(radius),
        "waypoints": int(waypoints),
        # Verbatim. See the header note - translating this threw the
        # distinction away.
        "side": int(side),
        "targets": [tuple(t) for t in (targets or ())],
    }


def pack_light(x, z, color="#ffffff", level=1.0, rng=12.0, y=0.0, curve=0,
               kind=0, aim=(0.0, -1.0, 0.0), cone=0.0, blend=0.0,
               ang0=0.0, ang1=0.0, vol_mix=1.0):
    """One light, in the tuple order the record is written in.

    `color` may be "#rrggbb" or an (r, g, b) triple of 0..1 floats. Path Studio
    holds the hex form because that is what a colour picker speaks. The shape
    arguments default to what every light was before they existed: a point,
    aimed down, fully in the fog.
    """
    if isinstance(color, str):
        h = color.lstrip("#")
        if len(h) == 3:
            h = "".join(c * 2 for c in h)
        rgb = tuple(int(h[i:i + 2], 16) / 255.0 for i in (0, 2, 4))
    else:
        rgb = tuple(float(c) for c in color)
    return (float(x), float(y), float(z),
            rgb[0], rgb[1], rgb[2],
            float(level), float(rng), int(curve),
            int(kind), float(aim[0]), float(aim[1]), float(aim[2]),
            float(cone), float(blend), float(ang0), float(ang1), float(vol_mix))


def light_bytes(lt):
    """The record for one pack_light tuple. One place, used by every writer."""
    if len(lt) != LIGHT_FIELDS:
        raise ValueError(f"light needs {LIGHT_FIELDS} fields, got {len(lt)}")
    return struct.pack(LIGHT_FMT, *[float(v) for v in lt[:8]], int(lt[8]),
                       int(lt[9]), *[float(v) for v in lt[10:18]])


def pack_bulb(primitives, type=BULB_POINT, pos=(0.0, 0.0, 0.0), aim=(0.0, -1.0, 0.0),
              cone=150.0, blend=0.35, color="#ffffff", level=0.5, rng=20.0,
              vol_mix=0.45, curve=0, ang0=0.0, ang1=0.0):
    """One bulb, as the dict the reader returns and the writer takes.

    ang0 / ang1 are the two half angles from the axis, in degrees, read per
    type - see the Bulb record in the module doc. Both 0 means "fall back to
    `blend`", which is what a file from before them reads back as.
    """
    if isinstance(color, str):
        h = color.lstrip("#")
        if len(h) == 3:
            h = "".join(c * 2 for c in h)
        rgb = tuple(int(h[i:i + 2], 16) / 255.0 for i in (0, 2, 4))
    else:
        rgb = tuple(float(c) for c in color)
    return {"primitives": primitives, "type": int(type),
            "pos": tuple(float(v) for v in pos), "aim": tuple(float(v) for v in aim),
            "cone": float(cone), "blend": float(blend), "color": rgb,
            "level": float(level), "range": float(rng), "vol_mix": float(vol_mix),
            "curve": int(curve), "ang0": float(ang0), "ang1": float(ang1)}


def bulb_bytes(b):
    """One bulb record, BULB_STRIDE bytes."""
    name = b["primitives"].encode("utf-8")[:BULB_NAME_LEN - 1]
    return struct.pack(BULB_FMT, name, int(b["type"]),
                       *[float(v) for v in b["pos"]], *[float(v) for v in b["aim"]],
                       float(b["cone"]), float(b["blend"]),
                       *[float(v) for v in b["color"]],
                       float(b["level"]), float(b["range"]), float(b["vol_mix"]),
                       int(b["curve"]),
                       float(b.get("ang0", 0.0)), float(b.get("ang1", 0.0)))


def read_bulbs(raw, off, count, stride):
    """The bulb block of a file, as a list of pack_bulb dicts.

    By the stride the file DECLARES, not by the record size this version
    knows. A file from before the two angles is 224 bytes a bulb and reads
    back with ang0 = ang1 = 0, the same way a pre-`curve` light reads as
    curve 0.
    """
    out = []
    for i in range(count):
        o = off + i * stride
        (name, typ, px, py, pz, ax, ay, az, cone, blend, r, g, b,
         level, rng, vol_mix, curve) = struct.unpack(BULB_FMT_MIN,
                                                     raw[o:o + BULB_STRIDE_MIN])
        if stride >= BULB_STRIDE:
            ang0, ang1 = struct.unpack("<2f", raw[o + BULB_STRIDE_MIN:o + BULB_STRIDE])
        else:
            ang0 = ang1 = 0.0
        out.append({"primitives": name.split(b"\0", 1)[0].decode("utf-8", "replace"),
                    "type": typ, "pos": (px, py, pz), "aim": (ax, ay, az),
                    "cone": cone, "blend": blend, "color": (r, g, b),
                    "level": level, "range": rng, "vol_mix": vol_mix, "curve": curve,
                    "ang0": ang0, "ang1": ang1})
    return out


def write_path(path, points, map_name, closed=True, total_len=None,
               seed=None, created=None, lights=(), bulbs=()):
    """Write a .campath.

    points: sequence of (x, y, z, heading, tilt, roll, s, speed).
    seed:   the dict pack_seed returns, or None when there is nothing to record
            - a command line export has no clicks behind it.
    lights: sequence of tuples from pack_light, or empty. Written after the
            seeds, and counted in the header so a reader knows without probing.
    """
    n = len(points)
    if n == 0:
        raise ValueError("refusing to write an empty path")

    if total_len is None:
        total_len = float(points[-1][6])

    seed = seed or pack_seed()
    rows = []
    if seed.get("start"):
        rows.append((seed["start"][0], seed["start"][1], SEED_START))
    for t in seed.get("targets", ()):
        rows.append((t[0], t[1], SEED_TARGET))

    name = map_name.encode("ascii", "replace")[:39]
    flags = FLAG_CLOSED if closed else 0
    when = int(time.time() if created is None else created)

    head = struct.pack(
        HEAD_FMT,
        MAGIC, VERSION, flags, n, STRIDE, float(total_len), name, HEADER_SIZE,
        when, len(rows), SEED_STRIDE,
        float(seed.get("heading", 0.0)), float(seed.get("radius", 0.0)),
        int(seed.get("waypoints", 0)), int(seed.get("side", 0)),
        len(lights), LIGHT_STRIDE if lights else 0,
        len(bulbs), BULB_STRIDE if bulbs else 0,
        b"")
    assert len(head) == HEADER_SIZE, len(head)

    with open(path, "wb") as f:
        f.write(head)
        for p in points:
            if len(p) != 8:
                raise ValueError(f"point needs 8 fields, got {len(p)}")
            f.write(struct.pack("<8f", *[float(v) for v in p]))
        for (x, z, kind) in rows:
            f.write(struct.pack("<ffI", float(x), float(z), int(kind)))
        for lt in lights:
            f.write(light_bytes(lt))
        for b in bulbs:
            f.write(bulb_bytes(b))

    return (HEADER_SIZE + n * STRIDE + len(rows) * SEED_STRIDE
            + len(lights) * LIGHT_STRIDE + len(bulbs) * BULB_STRIDE)


def read_path(path):
    """Read a .campath back. Returns (meta dict, list of point tuples)."""
    with open(path, "rb") as f:
        raw = f.read()

    if len(raw) < 8:
        raise ValueError("file is too short to have a header")

    magic = raw[:4]
    if magic == MAGIC_V1:
        raise ValueError(
            "this is a version 1 .campath, which carried no seed points. "
            "Regenerate it in Path Studio.")
    if magic != MAGIC:
        raise ValueError(f"bad magic {magic!r}, expected {MAGIC!r}")
    if len(raw) < HEADER_SIZE:
        raise ValueError("file is shorter than its header")

    (_magic, version, flags, count, stride, total_len, name, header_size,
     created, seed_count, seed_stride, seed_heading, seed_radius,
     seed_points, seed_side, light_count, light_stride,
     bulb_count, bulb_stride, _res) = struct.unpack(HEAD_FMT, raw[:HEADER_SIZE])

    if bulb_count and bulb_stride < BULB_STRIDE_MIN:
        raise ValueError(f"bulb_count {bulb_count} with bulb_stride "
                         f"{bulb_stride}, expected at least {BULB_STRIDE_MIN}")

    # A file written before lights existed has zeros in both, which reads as no
    # lights without a version test. A count with no stride is a corrupt header,
    # not an old file, so say so rather than reading garbage.
    if light_count and light_stride < LIGHT_STRIDE_MIN:
        raise ValueError(f"light_count {light_count} with light_stride "
                         f"{light_stride}, expected at least {LIGHT_STRIDE_MIN}")

    if stride < STRIDE:
        raise ValueError(f"stride {stride} is smaller than version 2's {STRIDE}")
    if header_size < HEADER_SIZE:
        raise ValueError(f"header_size {header_size} is smaller than {HEADER_SIZE}")

    want = (header_size + count * stride + seed_count * seed_stride
            + light_count * light_stride + bulb_count * bulb_stride)
    if len(raw) != want:
        raise ValueError(f"file is {len(raw)} bytes, header implies {want}")

    pts = []
    for i in range(count):
        off = header_size + i * stride
        pts.append(struct.unpack("<8f", raw[off:off + STRIDE]))

    base = header_size + count * stride
    start = None
    targets = []
    for i in range(seed_count):
        off = base + i * seed_stride
        x, z, kind = struct.unpack("<ffI", raw[off:off + SEED_STRIDE])
        if kind == SEED_START:
            start = (x, z)
        else:
            targets.append((x, z))

    lbase = header_size + count * stride + seed_count * seed_stride
    lights = []
    for i in range(light_count):
        off = lbase + i * light_stride
        x, y, z, r, g, b, level, rng = struct.unpack("<8f", raw[off:off + 32])
        # The curve came after the 32-byte record. Go by the stride the file
        # declares: an old file reads as curve 0, a new one as written.
        curve = (struct.unpack("<I", raw[off + 32:off + 36])[0]
                 if light_stride >= 36 else 0)
        # The shape came after the 36-byte record. Same rule: by the stride,
        # and an older file reads as the point light aimed down it always was.
        if light_stride >= 72:
            (kind, ax, ay, az, cone, blend,
             ang0, ang1, vol_mix) = struct.unpack("<I3f5f", raw[off + 36:off + 72])
        else:
            kind, (ax, ay, az) = 0, (0.0, -1.0, 0.0)
            cone = blend = ang0 = ang1 = 0.0
            vol_mix = 1.0
        lights.append({"x": x, "y": y, "z": z, "r": r, "g": g, "b": b,
                       "level": level, "range": rng, "curve": int(curve),
                       "kind": int(kind), "aim": (ax, ay, az), "cone": cone,
                       "blend": blend, "ang0": ang0, "ang1": ang1,
                       "vol_mix": vol_mix})

    bulbs = read_bulbs(raw, lbase + light_count * light_stride, bulb_count, bulb_stride)

    meta = {
        "version": version,
        "closed": bool(flags & FLAG_CLOSED),
        "count": count,
        "stride": stride,
        "header_size": header_size,
        "total_len": total_len,
        "map": name.split(b"\0", 1)[0].decode("ascii", "replace"),
        "bytes": len(raw),
        "created": created,
        "created_iso": datetime.datetime.fromtimestamp(
            created, datetime.timezone.utc).astimezone().isoformat(" ", "seconds"),
        "seed": {
            "start": start,
            "heading": seed_heading,
            "radius": seed_radius,
            "waypoints": seed_points,
            "side": seed_side,
            "targets": targets,
        },
        # In meta rather than a third return value, so every existing caller of
        # read_path keeps working unchanged.
        "lights": lights,
        # Model-attached lights, see the bulb record above. Placed by the Light
        # Bulb Placer in nuTerra; Path Studio only carries them through.
        "bulbs": bulbs,
    }
    return meta, pts


def _bulb_block(path):
    """The raw bulb records of a file, how many, and the stride they are at.

    The STRIDE comes back with the bytes because the two travel together: a
    file written before the ang0 / ang1 pair holds 224-byte records, and
    stamping this version's 232 on them would make every record after the
    first read at the wrong offset. Accept anything from BULB_STRIDE_MIN up -
    rejecting an older stride here would report "no bulbs" and quietly drop
    the operator's placed lights on the next route regenerate.
    """
    if not path or not os.path.exists(path):
        return b"", 0, 0
    with open(path, "rb") as f:
        raw = f.read()
    if len(raw) < HEADER_SIZE or raw[:4] != MAGIC:
        return b"", 0, 0
    (_m, _v, _fl, count, stride, _tl, _nm, header_size, _cr,
     seed_count, seed_stride, _sh, _sr, _sp, _sd,
     lc, ls, bc, bs, _res) = struct.unpack(HEAD_FMT, raw[:HEADER_SIZE])
    if not bc or bs < BULB_STRIDE_MIN:
        return b"", 0, 0
    off = header_size + count * stride + seed_count * seed_stride + lc * ls
    end = off + bc * bs
    if end > len(raw):
        return b"", 0, 0
    return raw[off:end], bc, bs


def copy_with_lights(src, dst, lights=(), bulbs=None):
    """Copy a generated .campath to its destination, with lights attached.

    Surgical on purpose. Reading the file and writing it out again through
    write_path would round-trip every header field through Python floats and
    risk losing one; this replaces only the light block and patches the two
    header words that describe it. Everything else is copied byte for byte.

    BULBS ARE KEPT. Path Studio does not edit them - the Light Bulb Placer in
    nuTerra does - so unless a list is passed, the bulb block already in the
    DESTINATION survives a route regenerate or a light save, and a fresh
    generated source with none cannot wipe them. Pass bulbs=() to drop them
    on purpose.
    """
    with open(src, "rb") as f:
        raw = f.read()
    if len(raw) < HEADER_SIZE or raw[:4] != MAGIC:
        raise ValueError("source is not a version 2 .campath")

    (_m, _v, _fl, count, stride, _tl, _nm, header_size, _cr,
     seed_count, seed_stride, _sh, _sr, _sp, _sd,
     _lc, _ls, _bc, _bs, _res) = struct.unpack(HEAD_FMT, raw[:HEADER_SIZE])

    if bulbs is None:
        bulb_raw, bulb_n, bulb_s = _bulb_block(dst)
        if not bulb_n:
            bulb_raw, bulb_n, bulb_s = _bulb_block(src)
    else:
        bulb_raw = b"".join(bulb_bytes(b) for b in bulbs)
        bulb_n = len(bulbs)
        bulb_s = BULB_STRIDE

    # Truncate at the end of the seeds - anything past that is a light block
    # from a previous save and must not be appended to.
    body_end = header_size + count * stride + seed_count * seed_stride
    if body_end > len(raw):
        raise ValueError("source header describes more data than the file holds")

    rows = [pack_light(**lt) if isinstance(lt, dict) else lt for lt in lights]
    head = bytearray(raw[:HEADER_SIZE])
    struct.pack_into("<II", head, 96, len(rows), LIGHT_STRIDE if rows else 0)
    # The stride the copied BYTES are at, not the one this version writes -
    # see _bulb_block.
    struct.pack_into("<II", head, 104, bulb_n, bulb_s if bulb_n else 0)

    with open(dst, "wb") as f:
        f.write(bytes(head))
        f.write(raw[HEADER_SIZE:body_end])
        for lt in rows:
            f.write(light_bytes(lt))
        f.write(bulb_raw)

    return body_end + len(rows) * LIGHT_STRIDE + len(bulb_raw)


def verify(path, points, seed=None, tol=1e-3):
    """Read a file back and check it against what was meant to be in it.

    Writing and then trusting the write is how a format bug ships. This is cheap
    enough to run on every export, and it checks the SEED too - the seed is the
    half that nothing downstream would notice was wrong.
    """
    meta, got = read_path(path)
    if meta["count"] != len(points):
        return False, f"count {meta['count']} != {len(points)}"

    worst = 0.0
    worst_at = None
    for i, (a, b) in enumerate(zip(points, got)):
        for j in range(8):
            d = abs(float(a[j]) - b[j])
            if d > worst:
                worst, worst_at = d, (i, j)

    if worst > tol:
        return False, (f"largest field error {worst:.6f} at point "
                       f"{worst_at[0]} field {worst_at[1]}")

    if seed is not None:
        s = meta["seed"]
        if bool(s["start"]) != bool(seed.get("start")):
            return False, "seed start present in one and not the other"
        if seed.get("start"):
            for k in (0, 1):
                if abs(s["start"][k] - float(seed["start"][k])) > tol:
                    return False, f"seed start differs on axis {k}"
        if len(s["targets"]) != len(seed.get("targets", ())):
            return False, (f"seed targets {len(s['targets'])} != "
                           f"{len(seed.get('targets', ()))}")
        for i, (a, b) in enumerate(zip(seed.get("targets", ()), s["targets"])):
            if abs(float(a[0]) - b[0]) > tol or abs(float(a[1]) - b[1]) > tol:
                return False, f"seed target {i} differs"
        if abs(s["heading"] - float(seed.get("heading", 0.0))) > tol:
            return False, "seed heading differs"
        if s["waypoints"] != int(seed.get("waypoints", 0)):
            return False, "seed waypoints differ"
        if s["side"] != int(seed.get("side", 0)):
            return False, "seed side differs"

    n_seed = 1 if meta["seed"]["start"] else 0
    n_seed += len(meta["seed"]["targets"])
    return True, (f"{meta['count']} points and {n_seed} seed points, "
                  f"largest round-trip error {worst:.2e}")


def describe(path):
    """One-screen summary of a file on disk."""
    import math
    meta, pts = read_path(path)
    ys = [p[1] for p in pts]
    rolls = [abs(p[5]) for p in pts]
    tilts = [p[4] for p in pts]
    sd = meta["seed"]

    lines = [
        f"{path}",
        f"  map {meta['map']}   version {meta['version']}   "
        f"{'closed loop' if meta['closed'] else 'open path'}",
        f"  created {meta['created_iso']}",
        f"  {meta['count']} points over {meta['total_len']:.0f} m "
        f"({meta['total_len'] / max(1, meta['count'] - 1):.2f} m apart), "
        f"{meta['bytes']} bytes",
        f"  altitude {min(ys):.1f} .. {max(ys):.1f} m",
        f"  tilt  {math.degrees(min(tilts)):+.1f} .. {math.degrees(max(tilts)):+.1f} deg",
        f"  roll  up to {math.degrees(max(rolls)):.1f} deg",
    ]

    if sd["start"]:
        lines.append(
            f"  seed  start {sd['start'][0]:.1f}, {sd['start'][1]:.1f}  "
            f"heading {math.degrees(sd['heading']):.1f} deg  "
            f"{'left' if sd['side'] > 0 else 'right'}  "
            f"radius {sd['radius']:.0f} m  {sd['waypoints']} waypoints")
    else:
        lines.append("  seed  none recorded")
    if sd["targets"]:
        lines.append(f"  seed  {len(sd['targets'])} target(s): " + ", ".join(
            f"({x:.0f}, {z:.0f})" for x, z in sd["targets"]))

    return "\n".join(lines)


if __name__ == "__main__":
    import sys
    if len(sys.argv) > 1:
        for p in sys.argv[1:]:
            print(describe(p))
    else:
        print(LAYOUT_DOC)
