# -*- coding: utf-8 -*-
"""One headless still from the owner's saved camera, with map-setting overrides.

    python tools/still_from_snapshot.py <run_name> [key=value ...] [--arg <nuTerra arg>]...
    python tools/still_from_snapshot.py --restore

The camera and map come from %TEMP%\\nuTerra\\snapshot.txt - the owner's last
Snapshot - so a tuning loop shoots the view he chose, every time, and a
before/after diff means something. The work settings file
(%TEMP%\\nuTerra\\MapSettings\\<map>.txt) is rewritten from the SHIPPED file
(nuTerra/MapSettings/<map>.txt) plus the overrides, nuTerra is launched with
cam= still=1 out=<scratch>/<run_name>, the script waits for still_000.png,
kills the process and prints the path.

NEVER uses snap or snapquit: those overwrite the owner's snapshot.txt, and an
agent render silently replacing the camera he just saved happened three times
in the lights session.

It DOES overwrite the work settings copy. Run with --restore when the loop is
done, or the owner launches into the last experiment.

Overrides are the map-settings keys (fog_density=0.03 tonemap_exposure=1.0 ...).
--arg passes anything else straight to nuTerra (--arg foggain=1.5).
"""
import os, re, subprocess, sys, time, shutil

EXE = r"C:\nuTerra\nuTerra\bin\Debug\net8.0-windows\nuTerra.exe"
WD = os.path.dirname(EXE)
SHIPPED_DIR = r"C:\nuTerra\nuTerra\MapSettings"
TEMP = os.environ["TEMP"]
SNAP = os.path.join(TEMP, "nuTerra", "snapshot.txt")
WORK_DIR = os.path.join(TEMP, "nuTerra", "MapSettings")
OUT_ROOT = os.path.join(TEMP, "nuTerra", "stills")


def read_snapshot():
    txt = open(SNAP, encoding="utf-8", errors="replace").read()
    m_map = re.search(r"^\s*map:\s*(\S+)", txt, re.M)
    m_cam = re.search(r"^\s*(cam=[-0-9.,]+)", txt, re.M)
    if not (m_map and m_cam):
        sys.exit("snapshot.txt has no map/cam line - take a Snapshot in nuTerra first")
    return m_map.group(1), m_cam.group(1)


def write_settings(map_name, overrides):
    src = os.path.join(SHIPPED_DIR, map_name + ".txt")
    dst = os.path.join(WORK_DIR, map_name + ".txt")
    lines = open(src, encoding="utf-8-sig").read().splitlines()
    out, seen = [], set()
    for l in lines:
        m = re.match(r"^([a-z_0-9]+)=(.*)$", l.strip())
        if m and m.group(1) in overrides:
            out.append("%s=%s" % (m.group(1), overrides[m.group(1)]))
            seen.add(m.group(1))
        else:
            out.append(l)
    for k, v in overrides.items():
        if k not in seen:
            out.append("%s=%s" % (k, v))
    os.makedirs(WORK_DIR, exist_ok=True)
    with open(dst, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(out) + "\n")
    return dst


def restore(map_name):
    src = os.path.join(SHIPPED_DIR, map_name + ".txt")
    dst = os.path.join(WORK_DIR, map_name + ".txt")
    shutil.copyfile(src, dst)
    print("restored", dst, "from the shipped file")


def main(argv):
    map_name, cam = read_snapshot()
    if "--restore" in argv:
        restore(map_name)
        return 0
    if len(argv) < 2:
        print(__doc__)
        return 1
    name = argv[1]
    overrides, extra = {}, []
    i = 2
    while i < len(argv):
        a = argv[i]
        if a == "--arg":
            extra.append(argv[i + 1]); i += 2; continue
        k, v = a.split("=", 1)
        overrides[k] = v
        i += 1
    write_settings(map_name, overrides)

    subprocess.call(["taskkill", "/IM", "nuTerra.exe", "/F"], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
    time.sleep(0.8)

    out_dir = os.path.join(OUT_ROOT, name)
    shutil.rmtree(out_dir, ignore_errors=True)
    os.makedirs(out_dir, exist_ok=True)
    still = os.path.join(out_dir, "still", "still_000.png")
    log = open(os.path.join(out_dir, "stdout.txt"), "w", encoding="utf-8")
    p = subprocess.Popen([EXE, map_name, cam, "still=1", "out=" + out_dir] + extra,
                         cwd=WD, stdout=log, stderr=subprocess.STDOUT)
    t0 = time.time()
    while time.time() - t0 < 240:
        if os.path.exists(still) and os.path.getsize(still) > 0:
            time.sleep(1.5)
            break
        if p.poll() is not None:
            break
        time.sleep(1.0)
    p.kill()
    log.close()
    ok = os.path.exists(still)
    print("map:", map_name, cam)
    print("still:", still if ok else "MISSING", "after %.0f s" % (time.time() - t0))
    print("overrides:", overrides, "args:", extra)
    print("when done: python tools/still_from_snapshot.py --restore")
    if not ok:
        print(open(os.path.join(out_dir, "stdout.txt"), encoding="utf-8", errors="replace").read()[-1500:])
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main(sys.argv))
