"""
The .campath binary format - camera flight paths for nuTerra.

One file is one closed or open flight: a small header, then a flat array of
fixed-size point records. Sequential, no seeking, no parsing. Reading it in
VB.NET is a BinaryReader loop, which is the point - the clever part happens
here in Python and nuTerra just plays back the numbers.

Kept in its own module rather than inside the exporter so the writer and any
future reader cannot drift apart. If the layout below changes, it changes in
one place, and LAYOUT_DOC is the text to paste into the VB side.

--------------------------------------------------------------------------
Header - 64 bytes, little endian
--------------------------------------------------------------------------
  off  type    field
    0  char[4] magic       "NCP1"
    4  uint16  version     1
    6  uint16  flags       bit 0 set = closed loop (last point joins first)
    8  uint32  count       number of point records that follow
   12  uint32  stride      bytes per record, 32 for version 1
   16  float32 total_len   path length in metres
   20  char[40] map        map name, ASCII, null padded
   60  uint32  reserved    0

Read `stride` and skip by it rather than assuming 32. A later version can add
fields to the end of the record and an old reader still works.

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
they cost 8 bytes per point and save the playback code from having to
integrate arc length before it can do anything.

--------------------------------------------------------------------------
Angle conventions, spelled out because they are where this will go wrong
--------------------------------------------------------------------------
nuTerra's MapCamera is an orbit rig - LOOK_AT plus CAM_X_ANGLE, CAM_Y_ANGLE
and VIEW_RADIUS - so heading and tilt map onto CAM_X_ANGLE and CAM_Y_ANGLE.
Note CAM_Y_ANGLE is clamped to about -1.57 .. 1.3 there, and tilt here is
already well inside that.

**roll has nowhere to go in that rig yet.** The orbit camera has no roll axis.
The value is written because banking is the thing being asked for and a format
change later is far more expensive than eight unused bytes now - but playing it
back needs a roll added to the view matrix, which does not exist today.
"""

import struct

MAGIC = b"NCP1"
VERSION = 1
HEADER_SIZE = 64
STRIDE = 32

FLAG_CLOSED = 1

LAYOUT_DOC = __doc__


def write_path(path, points, map_name, closed=True, total_len=None):
    """Write a .campath.

    points: sequence of (x, y, z, heading, tilt, roll, s, speed).
    """
    n = len(points)
    if n == 0:
        raise ValueError("refusing to write an empty path")

    if total_len is None:
        total_len = float(points[-1][6])

    name = map_name.encode("ascii", "replace")[:39]
    flags = FLAG_CLOSED if closed else 0

    head = struct.pack(
        "<4sHHIIf40sI",
        MAGIC, VERSION, flags, n, STRIDE, float(total_len), name, 0)
    assert len(head) == HEADER_SIZE, len(head)

    with open(path, "wb") as f:
        f.write(head)
        for p in points:
            if len(p) != 8:
                raise ValueError(f"point needs 8 fields, got {len(p)}")
            f.write(struct.pack("<8f", *[float(v) for v in p]))

    return HEADER_SIZE + n * STRIDE


def read_path(path):
    """Read a .campath back. Returns (meta dict, list of point tuples)."""
    with open(path, "rb") as f:
        raw = f.read()

    if len(raw) < HEADER_SIZE:
        raise ValueError("file is shorter than its header")

    magic, version, flags, count, stride, total_len, name, _ = struct.unpack(
        "<4sHHIIf40sI", raw[:HEADER_SIZE])

    if magic != MAGIC:
        raise ValueError(f"bad magic {magic!r}, expected {MAGIC!r}")
    if stride < STRIDE:
        raise ValueError(f"stride {stride} is smaller than version 1's {STRIDE}")

    want = HEADER_SIZE + count * stride
    if len(raw) != want:
        raise ValueError(f"file is {len(raw)} bytes, header implies {want}")

    pts = []
    for i in range(count):
        off = HEADER_SIZE + i * stride
        pts.append(struct.unpack("<8f", raw[off:off + STRIDE]))

    meta = {
        "version": version,
        "closed": bool(flags & FLAG_CLOSED),
        "count": count,
        "stride": stride,
        "total_len": total_len,
        "map": name.split(b"\0", 1)[0].decode("ascii", "replace"),
        "bytes": len(raw),
    }
    return meta, pts


def verify(path, points, tol=1e-3):
    """Read a file back and check it against what was meant to be in it.

    Writing and then trusting the write is how a format bug ships. This is
    cheap enough to run on every export.
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
        return False, f"largest field error {worst:.6f} at point {worst_at[0]} field {worst_at[1]}"
    return True, f"{meta['count']} points, largest round-trip error {worst:.2e}"


def describe(path):
    """One-screen summary of a file on disk."""
    import math
    meta, pts = read_path(path)
    ys = [p[1] for p in pts]
    rolls = [abs(p[5]) for p in pts]
    tilts = [p[4] for p in pts]

    lines = [
        f"{path}",
        f"  map {meta['map']}   version {meta['version']}   "
        f"{'closed loop' if meta['closed'] else 'open path'}",
        f"  {meta['count']} points over {meta['total_len']:.0f} m "
        f"({meta['total_len'] / max(1, meta['count'] - 1):.2f} m apart), "
        f"{meta['bytes']} bytes",
        f"  altitude {min(ys):.1f} .. {max(ys):.1f} m",
        f"  tilt  {math.degrees(min(tilts)):+.1f} .. {math.degrees(max(tilts)):+.1f} deg",
        f"  roll  up to {math.degrees(max(rolls)):.1f} deg",
    ]
    return "\n".join(lines)


if __name__ == "__main__":
    import sys
    if len(sys.argv) > 1:
        for p in sys.argv[1:]:
            print(describe(p))
    else:
        print(LAYOUT_DOC)
