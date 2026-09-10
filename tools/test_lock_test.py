"""Does the test lock actually stop input, and does it give the app back?

Every check is an event delivered with event_generate - the same path a real
click or keypress takes once the OS is done with it - so this measures the
binding layer, which is the layer the lock works at. No pointer is moved and
no window is brought to the front.

    python test_lock_test.py        exit 0 if the lock holds and lifts
"""
import os
import sys
import time

import tkinter as tk

import path_studio as PS
import radar_commit as nav

MAP = sys.argv[1] if len(sys.argv) > 1 else "19_monastery"


def main():
    root = tk.Tk()
    root.geometry("1324x835+4000+4000")
    studio = PS.Studio(root)
    root.update()

    studio.load_named(MAP)
    for _ in range(400):
        root.update()
        if studio.bake is not None and not studio.busy:
            break
        time.sleep(0.05)
    loaded = studio.selected_name
    print("loaded %s" % loaded)

    def probe(label):
        """Fire the events a person would, and say what got through."""
        got = []

        # a click on a different row of the map list
        before = studio.selected_name
        studio.maps.selection_clear(0, "end")
        studio.maps.selection_set(0)
        studio.maps.event_generate("<<ListboxSelect>>")
        root.update()
        if studio.selected_name != before:
            got.append("map list loaded %s" % studio.selected_name)

        # a key bound on the root. Watched by its EFFECT, not by swapping the
        # method out: root.bind captured the bound method when it ran, so
        # reassigning the attribute afterwards changes nothing Tk calls - the
        # first version of this test silently probed nothing.
        studio.live = None
        studio.edit_path = True
        studio.targets = [(10.0, 20.0), (30.0, 40.0)]
        root.event_generate("<BackSpace>")
        root.update()
        if len(studio.targets) < 2:
            got.append("backspace dropped a target")
        studio.targets = []

        # the space key, via the class binding the picker fix installed
        paused = studio.live_paused
        studio.live = object()          # enough for on_space to act
        studio.maps.event_generate("<space>")
        root.update()
        if studio.live_paused != paused:
            got.append("space toggled the pause")
        studio.live = None
        studio.live_paused = False

        # a click on the map canvas. It only places anything with the path
        # unlocked, so unlock it - otherwise this probe tests the Edit path
        # lock rather than the test lock.
        studio.edit_path = True
        before_start = studio.start
        studio.canvas.event_generate("<Button-1>", x=400, y=400)
        root.update()
        if studio.start != before_start:
            got.append("canvas placed a start")

        print("%-22s %s" % (label, ", ".join(got) if got else "nothing got through"))
        return got

    print()
    unlocked_before = probe("unlocked:")
    studio.load_named(loaded)
    for _ in range(200):
        root.update()
        if not studio.busy:
            break
        time.sleep(0.02)

    studio.set_test_lock(True, "self test.")
    locked = probe("LOCKED:")
    title_locked = root.title()

    studio.set_test_lock(False)
    unlocked_after = probe("unlocked again:")
    title_after = root.title()

    root.destroy()

    print()
    ok = True
    if not unlocked_before:
        print("FAIL: nothing got through even unlocked - the probe is broken")
        ok = False
    if locked:
        print("FAIL: %d event(s) got through the lock" % len(locked))
        ok = False
    if len(unlocked_after) != len(unlocked_before):
        print("FAIL: unlocking did not give everything back (%d of %d)"
              % (len(unlocked_after), len(unlocked_before)))
        ok = False
    if "LOCKED" not in title_locked or "LOCKED" in title_after:
        print("FAIL: the title does not say which state it is in (%r -> %r)"
              % (title_locked, title_after))
        ok = False
    print("test lock: %s" % ("holds, and lifts" if ok else "BROKEN"))
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
