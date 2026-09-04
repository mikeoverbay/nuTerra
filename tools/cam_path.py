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
   96  char[32] reserved      zeroed

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

HEAD_FMT = "<4sHHIIf40sIqIIffIi32s"

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


def write_path(path, points, map_name, closed=True, total_len=None,
               seed=None, created=None):
    """Write a .campath.

    points: sequence of (x, y, z, heading, tilt, roll, s, speed).
    seed:   the dict pack_seed returns, or None when there is nothing to record
            - a command line export has no clicks behind it.
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

    return HEADER_SIZE + n * STRIDE + len(rows) * SEED_STRIDE


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
     seed_points, seed_side, _res) = struct.unpack(HEAD_FMT, raw[:HEADER_SIZE])

    if stride < STRIDE:
        raise ValueError(f"stride {stride} is smaller than version 2's {STRIDE}")
    if header_size < HEADER_SIZE:
        raise ValueError(f"header_size {header_size} is smaller than {HEADER_SIZE}")

    want = header_size + count * stride + seed_count * seed_stride
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
    }
    return meta, pts


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
