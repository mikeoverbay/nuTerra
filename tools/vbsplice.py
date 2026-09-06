#!/usr/bin/env python3
"""Anchor-checked text splicing for VB source, without the ''' collision.

THE PROBLEM this exists to remove:

VB's XML doc comments start with three apostrophes. Python's triple-quoted
strings end at three apostrophes. So the obvious way to script an edit -

    new = '''    ''' <summary>...          # <-- the string ended at the '''
                                          #     and the rest is a syntax error

- fails at PARSE time, with a message pointing at a line number rather than at
the real cause. It cost several minutes more than once. Worse, one attempt at a
work-around fell back to an index-based cut and silently deleted the wrong
region of modTypeStructures.vb - ModelBatch, the region markers and
DECAL_INDEX_LIST all went with it.

THE RULE: VB text lives in its own .txt file, written with a plain file write,
and never appears inside a Python string literal. This script joins the two.

USAGE

    # replace an exact block
    python tools/vbsplice.py replace <target.vb> <old.txt> <new.txt>

    # insert before / after an exact anchor
    python tools/vbsplice.py before  <target.vb> <anchor.txt> <insert.txt>
    python tools/vbsplice.py after   <target.vb> <anchor.txt> <insert.txt>

    # check an anchor without writing anything
    python tools/vbsplice.py check   <target.vb> <anchor.txt>

WHAT IT GUARANTEES

  - the anchor must appear EXACTLY ONCE. Zero means the file has moved on and
    the edit is stale; two or more means it would land somewhere arbitrary.
    Both refuse to write.
  - never an offset or index cut. Whole-string replacement only, so a bad
    anchor deletes nothing.
  - the file keeps its OWN encoding and line endings - see probe(). A stray
    lone CR makes git treat a .vb as binary and report the whole file rewritten.
  - written to a temp file and moved into place, so an interrupted run cannot
    leave a half-written source file.
"""
import io
import os
import sys


def probe(path):
    """What the file already IS: BOM or not, CRLF or not.

    PRESERVED, not forced. An earlier version wrote every file as
    UTF-8-with-BOM and CRLF, because that is what the .vb sources are - and
    running it on a markdown file would then add a BOM that was never there.
    Match the file; do not impose on it.
    """
    raw = io.open(path, "rb").read()
    bom = raw.startswith(b"\xef\xbb\xbf")
    crlf = b"\r\n" in raw
    return ("utf-8-sig" if bom else "utf-8"), ("\r\n" if crlf else "\n")


def read(path, enc="utf-8-sig"):
    return io.open(path, encoding=enc).read()


def write(path, text, enc, nl):
    tmp = path + ".tmp"
    io.open(tmp, "w", encoding=enc, newline=nl).write(text)
    os.replace(tmp, path)


def count_or_die(haystack, needle, what):
    n = haystack.count(needle)
    if n == 1:
        return
    print("REFUSED: %s appears %d times, expected exactly 1" % (what, n))
    if n == 0:
        print("  the file has moved on - re-read it and rebuild the anchor")
    else:
        print("  the anchor is ambiguous - include more surrounding lines")
    sys.exit(1)


def main(argv):
    if len(argv) < 4:
        print(__doc__)
        return 1

    mode, target = argv[1], argv[2]
    enc, nl = probe(target)
    anchor = read(argv[3])
    src = read(target, enc)

    count_or_die(src, anchor, "anchor")

    if mode == "check":
        print("ok: anchor found exactly once in %s" % os.path.basename(target))
        return 0

    if len(argv) < 5:
        print("REFUSED: %s needs a fourth argument" % mode)
        return 1
    payload = read(argv[4])

    if mode == "replace":
        out = src.replace(anchor, payload, 1)
    elif mode == "before":
        out = src.replace(anchor, payload + anchor, 1)
    elif mode == "after":
        out = src.replace(anchor, anchor + payload, 1)
    else:
        print("REFUSED: unknown mode %r" % mode)
        return 1

    write(target, out, enc, nl)
    print("%s: %s applied (%+d chars)" % (os.path.basename(target), mode,
                                          len(out) - len(src)))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
