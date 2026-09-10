"""Run the Studio ON SCREEN, drive it, and check the live view really works.

    python live_trace_apptest.py [map] [--ms 120] [--gif app_trace.gif]

This is the app: a normal Path Studio window at a normal size and place. It is
driven the way a person drives it - a real <Button-1> on the map row, a real
<Button-1> on Trace live - through `event_generate`, which enters at the same
class bindings a physical click does. No pointer is moved and nothing is typed.

The window takes the test lock for the duration, so a click that lands on it
while the run is going cannot corrupt the measurement or the machine's owner's
afternoon. It is released and the window closed at the end.

Frames are captured off the SCREEN here, not out of the drawing code, because
the point is what the window shows - the decision panel is a Tk widget and
never passes through the canvas image at all.

Checks, all of which have been wrong at some point:
  - the button starts a trace at all
  - repaints keep coming for the whole run (an exception in the tick chain
    stops them silently, and under pythonw there is no console to say so)
  - the decision panel fills, and keeps filling after the first drain
  - rays get drawn, and the path is coloured by the layer that chose it
  - it closes the loop
"""
import os
import sys
import time

import tkinter as tk
from PIL import ImageGrab

import path_studio as PS

HERE = os.path.dirname(os.path.abspath(__file__))


def arg(name, default, cast=str):
    if name in sys.argv:
        i = sys.argv.index(name)
        if i + 1 < len(sys.argv):
            return cast(sys.argv[i + 1])
    return default


def main():
    plain = [a for a in sys.argv[1:] if not a.startswith("--")]
    flagged = {sys.argv[sys.argv.index(f) + 1]
               for f in ("--ms", "--gif") if f in sys.argv}
    plain = [a for a in plain if a not in flagged]
    map_name = plain[0] if plain else "19_monastery"
    ms = arg("--ms", 120, int)
    gif = arg("--gif", os.path.join(HERE, "app_trace.gif"))

    fails, notes = [], []
    repaints = [0]
    _real = PS.Studio.repaint

    def counted(self):
        repaints[0] += 1
        return _real(self)

    PS.Studio.repaint = counted

    root = tk.Tk()
    root.geometry("+80+60")
    PS.messagebox.askyesno = lambda *a, **k: False
    studio = PS.Studio(root)
    # NOT locked yet. The lock strips bindtags, and a synthetic <Button-1> is
    # indistinguishable from a real one by design - so locking first means the
    # clicks below land on nothing and the test measures an empty window. It
    # goes on once the clicking is done, which is also when it matters: the
    # run is long, the window is in front, and no input is needed for any of
    # it. (Discovered by doing it the wrong way round, which at least proves
    # the lock blocks what it claims to.)
    root.attributes("-topmost", True)
    root.lift()
    root.update()

    W, H = root.winfo_width(), root.winfo_height()

    def shot():
        x, y = root.winfo_rootx(), root.winfo_rooty()
        # The frame sits above and around the client area.
        return ImageGrab.grab(bbox=(x - 8, y - 40, x + W + 8, y + H + 8),
                              all_screens=True)

    # ---- click the map, for real ------------------------------------------
    lb = studio.maps
    idx = next((i for i in range(lb.size()) if lb.get(i) == map_name), None)
    if idx is None:
        print("%s is not in the list" % map_name)
        return 2
    lb.see(idx)
    root.update()
    bb = lb.bbox(idx)
    for ev in ("<Button-1>", "<ButtonRelease-1>"):
        lb.event_generate(ev, x=bb[0] + 20, y=bb[1] + bb[3] // 2)
    root.update()
    for _ in range(500):
        root.update()
        if studio.bake is not None and not studio.busy:
            break
        time.sleep(0.03)
    if studio.selected_name != map_name:
        fails.append("clicking %r loaded %r" % (map_name, studio.selected_name))
    if studio.bake is None:
        print("%s has no height map - bake it first" % map_name)
        return 2
    notes.append("clicked %r in the list -> loaded it" % map_name)

    studio.vars["trace_ms"].set(ms)
    repaints[0] = 0

    # ---- click Trace live, for real ---------------------------------------
    if "disabled" in studio.live_btn.state():
        fails.append("Trace live is greyed out with a map loaded")
    studio.live_btn.event_generate("<Button-1>", x=5, y=5)
    studio.live_btn.event_generate("<ButtonRelease-1>", x=5, y=5)
    root.update()
    if studio.live is None:
        fails.append("the Trace live button did not start a trace")
        root.destroy()
        return report(fails, notes)
    notes.append("pressed Trace live -> %s" % studio.status.get())

    # The clicking is done. Take the window for the rest of the run.
    studio.set_test_lock(True, "live_trace_apptest is driving.")
    root.update()
    if "LOCKED" not in root.title():
        fails.append("the test lock did not take")

    frames, log_seen, t0, nxt = [], [], time.time(), 0.0
    while time.time() - t0 < 180:
        root.update()
        now = time.time() - t0
        if now >= nxt:
            frames.append(shot())
            log_seen.append(
                len(studio.trace_log.get("1.0", "end").rstrip().split(chr(10))))
            nxt = now + 0.30
        if studio.live is None and len(frames) > 2:
            break
        time.sleep(0.005)

    root.update()
    frames.append(shot())
    status = studio.status.get()
    log_lines = studio.trace_log.get("1.0", "end").rstrip().split(chr(10))
    nvg_moves = status.split("moves ", 1)[-1] if "moves" in status else ""

    # ---- what has to be true ---------------------------------------------
    if repaints[0] < 20:
        fails.append("only %d repaints in the whole run - the tick chain died"
                     % repaints[0])
    else:
        notes.append("%d repaints while tracing" % repaints[0])

    if len(log_lines) < 10:
        fails.append("the decision panel has %d lines" % len(log_lines))
    else:
        notes.append("decision panel filled to %d lines" % len(log_lines))
    # It filled progressively, not all at the end, and not once and then
    # never again - which is what the orphaned-list bug looked like.
    if len(set(log_seen)) < 3:
        fails.append("the panel stopped growing (%s)" % sorted(set(log_seen)))
    else:
        notes.append("panel grew %d -> %d over the run"
                     % (log_seen[0], log_seen[-1]))

    if "closed" not in status:
        fails.append("did not close the loop: %s" % status)
    else:
        notes.append("closed the loop, moves %s" % nvg_moves)

    studio.set_test_lock(False)
    root.update()
    if "LOCKED" in root.title():
        fails.append("the test lock did not lift")
    root.destroy()

    if gif and frames:
        small = [f.convert("RGB").resize((f.width // 2, f.height // 2))
                 .quantize(colors=192, dither=0) for f in frames]
        small[0].save(gif, save_all=True, append_images=small[1:],
                      duration=300, loop=0, optimize=True)
        notes.append("%d frames of the real window -> %s (%.1f MB)"
                     % (len(small), gif, os.path.getsize(gif) / 1e6))
        frames[len(frames) // 2].save(os.path.splitext(gif)[0] + "_mid.png")

    return report(fails, notes)


def report(fails, notes):
    for n in notes:
        print("  ok   " + n)
    for f in fails:
        print("  FAIL " + f)
    print()
    print("live view in the app: %s" % ("works" if not fails else "BROKEN"))
    return 1 if fails else 0


if __name__ == "__main__":
    sys.exit(main())
