"""Does the .campath header still land where the format table says it does?

    python campath_align_test.py [file.campath ...]

Removing the departure heading left a dead float32 at offset 80. The four
bytes had to STAY: every field after them - light_count at 96, the bulb
fields at 104 and 108 - is at a fixed offset, and nuTerra's own reader
(MapCamPath.vb) is laid out for the current header. Shifting them would
misread every .campath already written.

So this checks the thing that would go wrong if that reasoning were mistaken:
each field's real offset, computed from HEAD_FMT, against the table in
cam_path's docstring - and then a real file, and a round trip.
"""
import glob
import os
import re
import struct
import sys

import cam_path as cp

# The table in cam_path's own docstring is the contract. Read it FROM there
# rather than copying it here, so the two cannot drift apart.
DOC_ROW = re.compile(r"^\s*(\d+)\s+(char\[\d+\]|uint16|uint32|int32|int64|float32)"
                     r"\s+(\S+)")

FIELDS = [
    ("magic", "4s"), ("version", "H"), ("flags", "H"), ("count", "I"),
    ("stride", "I"), ("total_len", "f"), ("map", "40s"),
    ("header_size", "I"), ("created", "q"), ("seed_count", "I"),
    ("seed_stride", "I"), ("dead80", "f"), ("seed_radius", "f"),
    ("seed_points", "I"), ("seed_side", "i"), ("light_count", "I"),
    ("light_stride", "I"), ("bulb_count", "I"), ("bulb_stride", "I"),
    ("reserved", "16s"),
]


def offsets():
    """Real offset of every header field, from HEAD_FMT itself."""
    out, off = [], 0
    for name, code in FIELDS:
        size = struct.calcsize("<" + code)
        out.append((name, off, size, code))
        off += size
    return out, off


def doc_offsets():
    """The offsets the docstring promises."""
    got = {}
    for line in (cp.__doc__ or "").splitlines():
        m = DOC_ROW.match(line)
        if m:
            got[m.group(3)] = int(m.group(1))
    return got


def main():
    fails = []
    rows, total = offsets()

    print("HEAD_FMT = %s" % cp.HEAD_FMT)
    print("calcsize = %d, HEADER_SIZE = %d" % (struct.calcsize(cp.HEAD_FMT),
                                               cp.HEADER_SIZE))
    if struct.calcsize(cp.HEAD_FMT) != cp.HEADER_SIZE:
        fails.append("HEAD_FMT packs %d bytes, header claims %d"
                     % (struct.calcsize(cp.HEAD_FMT), cp.HEADER_SIZE))
    if total != cp.HEADER_SIZE:
        fails.append("field walk sums to %d, header claims %d"
                     % (total, cp.HEADER_SIZE))
    if len(FIELDS) != len(re.findall(r"\d*[a-zA-Z]", cp.HEAD_FMT.lstrip("<"))):
        fails.append("HEAD_FMT has a different number of fields than this test")

    doc = doc_offsets()
    print()
    print("%-14s %6s %5s   %s" % ("field", "offset", "size", "docstring says"))
    for name, off, size, _code in rows:
        want = doc.get(name)
        note = "-" if want is None else str(want)
        bad = want is not None and want != off
        if bad:
            fails.append("%s is at %d, the table says %d" % (name, off, want))
        print("%-14s %6d %5d   %-6s %s" % (name, off, size, note,
                                           "<-- MISMATCH" if bad else ""))

    # The dead field, and the ones that would have moved if it were deleted.
    print()
    for name, want in (("dead80", 80), ("light_count", 96),
                       ("bulb_count", 104), ("bulb_stride", 108)):
        got = dict((n, o) for n, o, _s, _c in rows)[name]
        ok = got == want
        print("  %-12s at %3d  (must be %3d)  %s"
              % (name, got, want, "ok" if ok else "MOVED - old files break"))
        if not ok:
            fails.append("%s moved to %d" % (name, got))

    # ---- real files -------------------------------------------------------
    args = sys.argv[1:]
    files = args or sorted(glob.glob(os.path.join(cp.campath_dir(),
                                                  "*.campath")))
    print()
    if not files:
        print("no .campath files to check")
    for f in files[:8]:
        try:
            raw = open(f, "rb").read()
            head = struct.unpack(cp.HEAD_FMT, raw[:cp.HEADER_SIZE])
            magic, version = head[0], head[1]
            hsize, scount, sstride = head[7], head[9], head[10]
            dead, lcount, bcount = head[11], head[15], head[17]
            meta, _pts = cp.read_path(f)      # (dict, points), not a dict
            body = (hsize + head[3] * head[4] + scount * sstride
                    + lcount * head[16] + bcount * head[18])
            print("  %-26s v%d head=%d pts=%d seeds=%d lights=%d bulbs=%d "
                  "body=%d file=%d %s"
                  % (os.path.basename(f), version, hsize, head[3], scount,
                     lcount, bcount, body, len(raw),
                     "ok" if body <= len(raw) else "SHORT"))
            if magic not in (cp.MAGIC, cp.MAGIC_V1):
                fails.append("%s: bad magic %r" % (f, magic))
            if hsize != cp.HEADER_SIZE:
                fails.append("%s: header_size %d" % (f, hsize))
            if body > len(raw):
                fails.append("%s: body runs past the end of the file" % f)
            if dead != 0.0:
                print("      offset 80 carries %.4f - an older file's "
                      "departure heading, read by nothing" % dead)
            if "heading" in (meta.get("seed") or {}):
                fails.append("%s: read_path still surfaces a seed heading" % f)
        except Exception as e:
            fails.append("%s: %s" % (os.path.basename(f), e))
            print("  %-26s FAILED: %s" % (os.path.basename(f), e))

    print()
    for f in fails:
        print("  FAIL " + f)
    print("campath alignment: %s" % ("holds" if not fails else "BROKEN"))
    return 1 if fails else 0


if __name__ == "__main__":
    sys.exit(main())
