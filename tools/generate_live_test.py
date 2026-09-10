"""Generate path: does it animate, and does Escape put the old path back?

    python generate_live_test.py [map]

Runs under a REAL mainloop(), driven by root.after, because that is what the
app runs and the difference is not cosmetic: the navigator thread reports
progress with root.after(0, ...), and root.after from a worker raises
"main thread is not in main loop" unless the main thread is genuinely parked
in mainloop. Driving this with a while/update() loop killed the run on its
first status line and reported the FEATURE as broken. Test the environment
the code lives in.

The Studio is parked off-screen: no pointer moved, no window in the way.

Checked, all of which have been silently absent at some point:
  - the flight is visible WHILE it flies, not only once it lands
  - the path grows under the watcher
  - Escape stops it, and the route that was there comes back exactly
  - Generate un-greys afterwards
  - a full run still completes, and how long it takes
"""
import sys
import time

import tkinter as tk

import path_studio as PS

MAP = sys.argv[1] if len(sys.argv) > 1 else "19_monastery"

fails, notes = [], []
repaints = [0]
_real = PS.Studio.repaint


def counted(self):
    repaints[0] += 1
    return _real(self)


PS.Studio.repaint = counted

root = tk.Tk()
root.geometry("1324x835+4000+4000")
PS.messagebox.askyesno = lambda *a, **k: False
studio = PS.Studio(root)

state = {"route0": None, "seen": 0, "t0": 0.0}


def fail(m):
    fails.append(m)


def ok(m):
    notes.append(m)


def wait(cond, then, timeout, on_timeout, t0=None):
    """Poll cond() on the Tk timer. Never blocks the loop."""
    t0 = t0 or time.time()

    def again():
        if cond():
            then()
        elif time.time() - t0 > timeout:
            on_timeout()
        else:
            root.after(100, again)

    root.after(100, again)


# ---------------------------------------------------------------- stages

def s0_load():
    studio.load_named(MAP)
    wait(lambda: studio.bake is not None and not studio.busy, s1_setup, 60,
         lambda: bail("%s never finished loading" % MAP))


def s1_setup():
    if not studio.route:
        bail("%s has no saved route to revert to - generate one first" % MAP)
        return
    state["route0"] = list(studio.route)
    ok("loaded %s with a saved route of %d points" % (MAP, len(state["route0"])))
    studio.edit_path = True
    studio.start = state["route0"][0]
    # No heading any more - a click sets the start and the points decide the
    # direction. Generate needs a start and at least one point.
    studio.targets = [state["route0"][i]
                      for i in range(0, len(state["route0"]),
                                     max(1, len(state["route0"]) // 8))][1:8]
    studio.vars["trace_ms"].set(0)
    studio.live_ms = 0.0
    repaints[0] = 0
    state["t0"] = time.time()
    studio.generate()
    wait(lambda: studio.gen is not None, s2_flying, 240,
         lambda: (fail("the flight never became visible (self.gen stayed None)"
                       " - status: %s" % studio.status.get()), s4_escape()))


def s2_flying():
    p, fans, steps = studio.gen
    ok("visible after %.0f s: %d points, %d fans, step %d"
       % (time.time() - state["t0"], len(p), len(fans), steps))
    if not fans:
        fail("no radar fans recorded - nothing to animate")
    state["seen"] = len(p)
    wait(lambda: studio.gen and len(studio.gen[0]) > state["seen"] + 40,
         s3_grew, 60,
         lambda: (fail("the flight did not advance while being watched"),
                  s4_escape()))


def s3_grew():
    ok("path grew %d -> %d while flying" % (state["seen"], len(studio.gen[0])))
    if repaints[0] < 15:
        fail("only %d repaints during the flight" % repaints[0])
    else:
        ok("%d repaints during the flight" % repaints[0])
    s4_escape()


def s4_escape():
    root.event_generate("<Escape>")
    wait(lambda: not studio.busy, s5_check, 90,
         lambda: (fail("Escape did not stop the flight"), s6_full()))


def s5_check():
    ok("Escape stopped it: %s" % studio.status.get())
    if studio.route != state["route0"]:
        fail("the old route did NOT come back (%d points vs %d)"
             % (len(studio.route or []), len(state["route0"])))
    else:
        ok("the previous %d-point route is back, unchanged"
           % len(state["route0"]))
    if studio.gen is not None:
        fail("the flight overlay is still drawn after cancelling")
    if "disabled" in studio.go.state():
        fail("Generate stayed greyed out after cancelling")
    s6_full()


def s6_full():
    state["t0"] = time.time()
    repaints[0] = 0
    studio.gen_cancel = False
    studio.generate()
    wait(lambda: not studio.busy, s7_done, 420,
         lambda: (fail("a full generate did not finish in 420 s"), finish()))


def s7_done():
    el = time.time() - state["t0"]
    ok("a full generate took %.0f s, %d repaints, route now %d points"
       % (el, repaints[0], len(studio.route or [])))
    ok("status: %s" % studio.status.get()[:70])
    lines = studio.trace_log.get("1.0", "end").rstrip().split(chr(10))
    ok("Navigator box: %d lines" % len(lines))
    finish()


def bail(msg):
    fail(msg)
    finish()


def finish():
    for n in notes:
        print("  ok   " + n)
    for f in fails:
        print("  FAIL " + f)
    print()
    print("Generate live + Escape: %s" % ("works" if not fails else "BROKEN"))
    sys.stdout.flush()
    root.quit()


root.after(200, s0_load)
root.mainloop()
# quit() ends the loop but leaves the interpreter alive; destroy() then throws
# "application has been destroyed" if anything already tore it down. Guarded,
# because a test that dies in its own shutdown reports no verdict at all -
# which is exactly what happened the first time this ran.
try:
    root.destroy()
except Exception:
    pass
sys.exit(1 if fails else 0)
