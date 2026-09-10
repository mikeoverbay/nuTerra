"""Click every row in the map list, badly, and check the right map loads.

    python picker_click_test.py

Real events through the real class bindings - event_generate is the same path
a physical click takes once the OS is done with it - on a Studio parked
off-screen. No pointer is moved and no window is fronted.

"Badly" is the point. A clean press was never the failure: the failure was a
press followed by the hand moving two pixels, which in Tk's browse mode walks
the selection down the list and fires a LOAD for every row it crosses. So every
click here twitches, and one of them drags clean off the bottom edge.
"""
import os
import sys
import time

import tkinter as tk

import path_studio as PS

TWITCH = (0, 6, 14, 30, -12)        # pixels the "hand" moves after pressing


def main():
    root = tk.Tk()
    root.geometry("1324x835+4000+4000")
    # No dialog: a map with no height map must not stop the sweep.
    PS.messagebox.askyesno = lambda *a, **k: False
    studio = PS.Studio(root)
    root.update()
    lb = studio.maps

    loads = []
    real_load = studio.load_named
    studio.load_named = lambda n: (loads.append(n), real_load(n))[1]

    bad = []
    checked = 0
    for notches in (0, 3, 9, 20, 40):
        for _ in range(notches):
            lb.event_generate("<MouseWheel>", delta=-120)
        root.update()
        top = lb.nearest(0)

        for k, dy in enumerate(TWITCH):
            i = top + k
            bb = lb.bbox(i)
            if not bb:
                continue
            want = lb.get(i)
            loads[:] = []
            x, y = bb[0] + 20, bb[1] + bb[3] // 2
            lb.event_generate("<Button-1>", x=x, y=y)
            if dy:
                lb.event_generate("<B1-Motion>", x=x, y=y + dy)
                if dy > 20:
                    lb.event_generate("<B1-Leave>", x=x, y=y + 200)
                    for _ in range(6):
                        root.update()
                        time.sleep(0.01)
            lb.event_generate("<ButtonRelease-1>", x=x, y=y + dy)
            root.update()
            for _ in range(80):
                root.update()
                time.sleep(0.02)
                if not studio.busy:
                    break

            checked += 1
            sel = lb.curselection()
            shown = lb.get(sel[0]) if sel else "<none>"
            if studio.selected_name != want or shown != want or len(loads) > 1:
                bad.append((want, shown, studio.selected_name, list(loads), dy))

        lb.yview_moveto(0)
        root.update()

    root.destroy()

    print("%d clicks, each with the pointer moving after the press" % checked)
    if not bad:
        print("every one loaded the row it landed on, and loaded it once")
        return 0
    for want, shown, got, fired, dy in bad:
        print("  pressed %-22r drift %-4d list shows %-22r loaded %-22r "
              "loads %s" % (want, dy, shown, got, fired))
    print("%d WRONG" % len(bad))
    return 1


if __name__ == "__main__":
    sys.exit(main())
