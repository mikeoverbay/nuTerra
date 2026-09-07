"""Pull the compiled shaders out of a WoT .fxo and disassemble them.

The route, from docs/terrain_blend.md "Reading the game's shaders":

    shaders.pkg                     a zip
      -> shaders/<area>/<name>.fxo  also a zip
           -> "effect"              an ARIEDX11 blob
                -> N x raw DXBC     found by the "DXBC" magic,
                                    size is the u32 at +24

The header region before the first DXBC carries the parameter and technique
names, so the strings dump tells you which blob is worth reading before you
open any assembly.

Usage
-----
    python fxo_dump.py list  [filter]
    python fxo_dump.py dump  <entry-substring> [out_dir]
    python fxo_dump.py strings <entry-substring> [min_len]

`dump` writes blob_NN.dxbc plus header.txt, and disassembles each blob with
fxc.exe if one can be found in the Windows SDK.
"""
import io
import os
import re
import struct
import subprocess
import sys
import zipfile

PKG = r"C:\Games\World_of_Tanks_NA\res\packages\shaders.pkg"
DXBC = b"DXBC"


def _pkg():
    return zipfile.ZipFile(PKG)


def find_entry(sub):
    """The single shaders.pkg entry whose name contains `sub`."""
    with _pkg() as z:
        names = [n for n in z.namelist() if sub.lower() in n.lower()]
    if not names:
        raise SystemExit("no entry matching %r" % sub)
    if len(names) > 1:
        exact = [n for n in names if n.lower().endswith(sub.lower())]
        if len(exact) == 1:
            return exact[0]
        raise SystemExit("ambiguous, matches:\n  " + "\n  ".join(names[:20]))
    return names[0]


def effect_bytes(entry):
    """The 'effect' member of the .fxo, which is itself a zip."""
    with _pkg() as z:
        raw = z.read(entry)
    inner = zipfile.ZipFile(io.BytesIO(raw))
    names = inner.namelist()
    pick = "effect" if "effect" in names else names[0]
    return inner.read(pick), names


def split_blobs(blob):
    """Every DXBC container in `blob`, as (offset, bytes).

    Size is the u32 at +24 of each container. Scanning for the magic rather
    than walking a table because the container list format is not documented
    anywhere we trust, and the magic is unambiguous.
    """
    out, pos = [], 0
    while True:
        i = blob.find(DXBC, pos)
        if i < 0:
            return out
        if i + 28 > len(blob):
            return out
        size = struct.unpack_from("<I", blob, i + 24)[0]
        if size <= 0 or i + size > len(blob):
            pos = i + 4
            continue
        out.append((i, blob[i:i + size]))
        pos = i + size


def printable(data, min_len=5):
    return re.findall(rb"[ -~]{%d,}" % min_len, data)


def find_fxc():
    roots = [r"C:\Program Files (x86)\Windows Kits\10\bin",
             r"C:\Program Files\Windows Kits\10\bin"]
    found = []
    for r in roots:
        if not os.path.isdir(r):
            continue
        for dirpath, _dirs, files in os.walk(r):
            if "fxc.exe" in files and os.sep + "x64" in dirpath:
                found.append(os.path.join(dirpath, "fxc.exe"))
    return sorted(found)[-1] if found else None


def cmd_list(args):
    filt = args[0] if args else ""
    with _pkg() as z:
        for i in sorted(z.infolist(), key=lambda e: e.filename):
            if filt.lower() in i.filename.lower():
                print("%9d  %s" % (i.file_size, i.filename))


def cmd_strings(args):
    entry = find_entry(args[0])
    min_len = int(args[1]) if len(args) > 1 else 6
    data, names = effect_bytes(entry)
    blobs = split_blobs(data)
    head_end = blobs[0][0] if blobs else len(data)
    print("entry   : %s" % entry)
    print("members : %s" % ", ".join(names))
    print("effect  : %d bytes, %d DXBC blob(s), header %d bytes"
          % (len(data), len(blobs), head_end))
    print()
    for s in printable(data[:head_end], min_len):
        print("   " + s.decode("ascii", "replace"))


def cmd_dump(args):
    entry = find_entry(args[0])
    out = args[1] if len(args) > 1 else "fxo_out"
    os.makedirs(out, exist_ok=True)
    data, _names = effect_bytes(entry)
    blobs = split_blobs(data)
    head_end = blobs[0][0] if blobs else len(data)

    with open(os.path.join(out, "header.txt"), "w", encoding="utf-8") as f:
        f.write("entry: %s\n" % entry)
        f.write("effect: %d bytes, %d blobs\n\n" % (len(data), len(blobs)))
        for s in printable(data[:head_end], 6):
            f.write(s.decode("ascii", "replace") + "\n")

    fxc = find_fxc()
    print("entry %s -> %d blob(s) in %s" % (entry, len(blobs), out))
    print("fxc: %s" % (fxc or "NOT FOUND - writing .dxbc only"))
    for n, (off, b) in enumerate(blobs):
        p = os.path.join(out, "blob_%02d.dxbc" % n)
        with open(p, "wb") as f:
            f.write(b)
        line = "  blob %02d  at %8d  %7d bytes" % (n, off, len(b))
        if fxc:
            asm = p.replace(".dxbc", ".asm")
            r = subprocess.run([fxc, "/dumpbin", p, "/Fc", asm],
                               capture_output=True)
            if r.returncode == 0 and os.path.exists(asm):
                with open(asm, "r", errors="replace") as f:
                    text = f.read()
                # the shader model line is the quickest label
                m = re.search(r"^(vs|ps|cs|gs|hs|ds)_\d_\d", text, re.M)
                line += "  %-6s %5d asm lines" % (m.group(0) if m else "?",
                                                  text.count("\n"))
        print(line)


if __name__ == "__main__":
    if len(sys.argv) < 2:
        raise SystemExit(__doc__)
    {"list": cmd_list, "dump": cmd_dump, "strings": cmd_strings}[sys.argv[1]](sys.argv[2:])
