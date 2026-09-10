"""Watch the live trace without touching the mouse.

    python live_trace_probe.py [map] [--ms 90] [--gif live_trace.gif]
                               [--frames-dir <dir>] [--every 0.35]

Runs the REAL Path Studio - the same Studio class, the same trace_live, the
same repaint - on a Tk root parked off-screen, and records the exact images it
hands to its canvas. Nothing is clicked, nothing is scrolled, no window is
brought to the front, so it can run while the machine is being used for
something else.

The frames are not screenshots. ImageTk.PhotoImage is wrapped and the PIL
image it is given is kept, so what lands in the GIF is precisely the pixels
the canvas got, at the moment it got them.

Why this exists: a still says the code ran, not that the scan MOVES, and
driving the pointer to prove it is both rude and unreliable - a wheel event
meant for the map list once fired two real map loads and left a dialog open.
This is the same evidence with none of that.

Waypoints come from the map's committed plan, the way radar_tangent's own
main() picks them, so it flies the route the navigator is actually asked to
fly rather than something invented here.
"""
import os
import sys
import time

import tkinter as tk
from PIL import ImageTk

import path_studio as PS
import radar_commit as nav

HERE = os.path.dirname(os.path.abspath(__file__))


def arg(name, default, cast=str):
    if name in sys.argv:
        i = sys.argv.index(name)
        if i + 1 < len(sys.argv):
            return cast(sys.argv[i + 1])
    return default


def main():
    plain = [a for a in sys.argv[1:] if not a.startswith("--")]
    # A bare value after a --flag is that flag's argument, not the map name.
    flagged = set()
    for f in ("--ms", "--gif", "--frames-dir", "--every"):
        if f in sys.argv and sys.argv.index(f) + 1 < len(sys.argv):
            flagged.add(sys.argv[sys.argv.index(f) + 1])
    plain = [a for a in plain if a not in flagged]

    map_name = plain[0] if plain else "19_monastery"
    ms = arg("--ms", 90, int)
    every = arg("--every", 0.35, float)
    gif = arg("--gif", os.path.join(HERE, "live_trace.gif"))
    frames_dir = arg("--frames-dir", None)
    if frames_dir:
        os.makedirs(frames_dir, exist_ok=True)

    kept = {}
    real = ImageTk.PhotoImage

    class Spy(real):
        def __init__(self, image=None, **kw):
            if image is not None:
                kept["im"] = image
            real.__init__(self, image, **kw)

    PS.ImageTk.PhotoImage = Spy

    root = tk.Tk()
    # Off the visible desktop. It still has a real size, real events and a
    # real event loop; it just does not sit on top of anyone's work.
    root.geometry("1324x835+4000+4000")
    studio = PS.Studio(root)
    # ...and locked, so that even if it is dragged into view, or a window
    # manager decides to put it somewhere else, a click cannot land in the
    # middle of the run. Off-screen is where it goes; locked is why it is
    # safe there.
    studio.set_test_lock(True, "live_trace_probe is driving.")
    root.update()

    studio.load_named(map_name)
    for _ in range(600):
        root.update()
        if studio.bake is not None and not studio.busy:
            break
        time.sleep(0.05)
    if studio.bake is None:
        print("%s has no height map - bake it in the Studio first" % map_name)
        return 2

    plan = os.path.join(nav.FOLDER, map_name + "_plan.csv")
    if not os.path.exists(plan):
        print("no plan at %s - generate a path for %s first" % (plan, map_name))
        return 2
    nx, nz = nav.load_plan(plan)
    n = max(4, len(nx) // 12)
    pts = [(float(nx[i]), float(nz[i])) for i in range(0, len(nx), n)]
    studio.start = pts[0]
    studio.targets = pts[1:]
    studio.edit_path = True
    studio.vars["trace_ms"].set(ms)
    print("%s: %d waypoints, %d ms a step" % (map_name, len(pts), ms))

    studio.trace_live()

    frames = []
    t0 = time.time()
    nxt = t0 + 0.4
    while time.time() - t0 < 300:
        root.update()
        now = time.time()
        if now >= nxt and "im" in kept:
            frames.append(kept["im"].copy())
            if frames_dir:
                frames[-1].save(os.path.join(frames_dir,
                                             "f%03d.png" % (len(frames) - 1)))
            nxt = now + every
        if studio.live is None and frames:
            break
        time.sleep(0.005)

    root.update()
    if "im" in kept:
        frames.append(kept["im"].copy())
    status = studio.status.get()
    root.destroy()

    print(status)
    if gif and frames:
        # Half size and a shared palette: 800 px of true colour times eighty
        # frames is a file nobody will open.
        small = [f.convert("RGB").resize((f.width // 2, f.height // 2))
                 for f in frames]
        small = [f.quantize(colors=192, dither=0) for f in small]
        small[0].save(gif, save_all=True, append_images=small[1:],
                      duration=int(every * 1000), loop=0, optimize=True)
        print("%d frames -> %s (%.1f MB)"
              % (len(small), gif, os.path.getsize(gif) / 1e6))
    return 0


if __name__ == "__main__":
    sys.exit(main())
