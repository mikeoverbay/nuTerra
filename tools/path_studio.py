"""
Path Studio - pick a map, click a start, drag a heading, generate a flight.

    python path_studio.py

Everything downstream of the seed is the existing pipeline, called as-is:
flight_plan's cost grid and router, radar_commit's navigator, export_cam_path's
smoothing and .campath writer. This module contributes the window, the seed, and
one thing the pipeline did not have - a route that starts where you say.

--------------------------------------------------------------------------
How the seed becomes a route
--------------------------------------------------------------------------
The click and drag give a point P and a heading H. From those, a circle is laid
down that is TANGENT to H at P - so the flight leaves your start in exactly the
direction you dragged and comes back round to it.

That circle is only a first guess. Its waypoints are snapped into reachable free
space and then routed between with the same Dijkstra the automatic orbit uses,
so the ring bends around whatever is in the way and stops being a circle almost
immediately. It is a seed, not a shape.

Which way it curves is yours to choose - the centre sits 90 degrees to the left
or right of your drag.

--------------------------------------------------------------------------
Only maps you have opened in nuTerra can appear here
--------------------------------------------------------------------------
The list is built from the bakes in %TEMP%\\nuTerra\\flight\\, and MapFlightBake
writes those on map load. A map nuTerra has never opened has no bake and cannot
be planned.
"""

import math
import os
import sys
import threading
import traceback

import numpy as np
from scipy import ndimage

import shutil
import tkinter as tk
from tkinter import ttk, messagebox
from PIL import Image, ImageTk, ImageDraw

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import radar_commit as nav
import flight_plan as fp
import export_cam_path as ex
import cam_path as cp

FOLDER = nav.FOLDER


def existing_plan(map_name):
    """The saved route, the clicks behind it, and its lights.

    Returns ([(x, z)], seed, [light dicts]) or (None, None, []).

    Reads the .campath, not the CSV beside the bake. The .campath is the artefact
    that actually ships, so this shows what nuTerra would fly rather than what the
    last run in this folder happened to leave behind - and since version 2 it is
    the only place the seed exists at all.
    """
    path = os.path.join(cp.campath_dir(), map_name + ".campath")
    if not os.path.exists(path):
        return None, None, []
    try:
        meta, pts = cp.read_path(path)
    except Exception:
        # A half written or older-format file is not worth refusing to open the
        # map over. Draw nothing and let a regenerate replace it.
        return None, None, []

    # Back into the shape the editor works in. The file keeps colour as three
    # floats because that is what a renderer wants; a colour picker speaks hex.
    lights = []
    for lt in meta.get("lights", ()):
        lights.append({
            "x": lt["x"],
            "z": lt["z"],
            "height": lt["y"],
            "color": "#%02x%02x%02x" % (
                max(0, min(255, int(round(lt["r"] * 255.0)))),
                max(0, min(255, int(round(lt["g"] * 255.0)))),
                max(0, min(255, int(round(lt["b"] * 255.0))))),
            "level": lt["level"],
            "range": lt["range"],
        })
    return [(p[0], p[2]) for p in pts], meta["seed"], lights

# --------------------------------------------------------------------------
# Dark theme
# --------------------------------------------------------------------------
# The canvas has always been near black (#11141c) because a terrain mask reads
# better against dark. The panel beside it was system grey, so the window had a
# bright wall down one side and your eye kept re-adapting between them.

BG = "#171a23"       # window and panel
PANEL = "#1f2430"    # inputs, list, buttons
EDGE = "#2c3242"     # borders and separators
FG = "#d7dce6"       # body text
MUTED = "#8a93a6"    # hints, secondary labels
ACCENT = "#4ab3d8"   # selection, focus


def apply_dark(root):
    """Restyle ttk and the classic tk widgets for a dark window.

    "clam" is the theme to build on: the Windows native themes draw from OS
    bitmaps and ignore most colour options, so a dark palette on those silently
    does nothing to half the widgets.

    Classic tk widgets - Listbox, and the Combobox's dropdown, which is a tk
    Listbox in disguise - do not follow ttk styles at all and are coloured
    directly. That asymmetry is the whole reason this is fiddly.
    """
    root.configure(bg=BG)
    st = ttk.Style(root)
    try:
        st.theme_use("clam")
    except tk.TclError:
        pass

    st.configure(".", background=BG, foreground=FG, fieldbackground=PANEL,
                 bordercolor=EDGE, lightcolor=EDGE, darkcolor=EDGE,
                 troughcolor="#11141c", focuscolor=ACCENT, insertcolor=FG)
    st.configure("TFrame", background=BG)
    st.configure("TLabel", background=BG, foreground=FG)
    st.configure("Muted.TLabel", background=BG, foreground=MUTED)
    st.configure("Note.TLabel", background=BG, foreground=MUTED,
                 font=("Consolas", 8))
    st.configure("Head.TLabel", background=BG, foreground=ACCENT)

    st.configure("TButton", background=PANEL, foreground=FG,
                 bordercolor=EDGE, focusthickness=1, padding=4)
    st.map("TButton",
           background=[("pressed", "#39415a"), ("active", "#2b3244"),
                       ("disabled", "#1a1d26")],
           foreground=[("disabled", "#586074")])

    st.configure("TScale", background=BG, troughcolor="#11141c",
                 bordercolor=EDGE, lightcolor=ACCENT, darkcolor=ACCENT)
    st.configure("TSeparator", background=EDGE)
    st.configure("TRadiobutton", background=BG, foreground=FG)
    st.map("TRadiobutton", background=[("active", BG)],
           indicatorcolor=[("selected", ACCENT)])

    st.configure("TCombobox", fieldbackground=PANEL, background=PANEL,
                 foreground=FG, arrowcolor=FG, bordercolor=EDGE)
    st.map("TCombobox",
           fieldbackground=[("readonly", PANEL)],
           foreground=[("readonly", FG)],
           background=[("readonly", PANEL)])

    # The dropdown is a tk Listbox and only listens to the option database.
    root.option_add("*TCombobox*Listbox.background", PANEL)
    root.option_add("*TCombobox*Listbox.foreground", FG)
    root.option_add("*TCombobox*Listbox.selectBackground", ACCENT)
    root.option_add("*TCombobox*Listbox.selectForeground", "#0b0d12")


CANVAS = 780         # starting size only - the map scales with the window
MIN_VIEW = 240

# Wheel zoom. 1.0 fits the whole map in the square; MAX_ZOOM 16 leaves a 64
# texel window, about 87 m across, which is closer than any routing decision
# needs. A step of 1.2 is roughly a doubling every four notches.
MAX_ZOOM = 16.0
ZOOM_STEP = 1.2


# --------------------------------------------------------------------------
# The seed
# --------------------------------------------------------------------------

def departure_leg(bake, blocked, g, start_xz, heading, want_m):
    """A straight leg from the click along the heading, as far as it stays clear.

    CONSTRUCTED, not routed, and that is the whole point. Dijkstra minimises
    cost, and the cost field rewards elbow room, so near the start it pulls away
    from whatever direction was asked for and heads wherever the map is open.
    Measured: seeding the ring tangent to 90 degrees produced a route leaving at
    351, and no amount of spline tuning or re-indexing changed it, because the
    router was never trying to honour the heading in the first place.

    Walking the leg ourselves makes the drag exact by construction. The router
    then picks it up from the far end, where it is free to do as it likes.
    """
    sx, sz = start_xz
    dxh, dzh = math.sin(heading), math.cos(heading)
    fy, fx = bake.h // g, bake.w // g
    step = bake.mx * fx * 0.5

    def cell(wx, wz):
        c, r = bake.texel_of(wx, wz)
        return (int(np.clip(round(r / fy), 0, g - 1)),
                int(np.clip(round(c / fx), 0, g - 1)))

    pts = [(sx, sz)]
    t = 0.0
    while t < want_m:
        t += step
        wx, wz = sx + dxh * t, sz + dzh * t
        if blocked[cell(wx, wz)]:
            t -= step
            break
        pts.append((wx, wz))
    return pts, t


def ring_after(bake, reach, g, start_xz, heading, leg_end, radius, count, side):
    """Ring waypoints from the end of the departure leg back round to the start.

    The circle is still tangent to the heading at the click, so the leg lies
    along it and the loop carries on in the same direction rather than doubling
    back on itself.
    """
    sx, sz = start_xz
    nx_, nz_ = (math.cos(heading), -math.sin(heading)) if side > 0 else                (-math.cos(heading), math.sin(heading))
    cx, cz = sx + nx_ * radius, sz + nz_ * radius
    a0 = math.atan2(sz - cz, sx - cx)
    sweep = -1.0 if side > 0 else 1.0

    fy, fx = bake.h // g, bake.w // g

    def cell(wx, wz):
        c, r = bake.texel_of(wx, wz)
        return (int(np.clip(round(r / fy), 0, g - 1)),
                int(np.clip(round(c / fx), 0, g - 1)))

    out = []
    for i in range(1, count):
        a = a0 + sweep * (2.0 * math.pi * i / count)
        out.append(fp.nearest_free(reach, cell(cx + math.cos(a) * radius,
                                               cz + math.sin(a) * radius)))
    out.append(fp.nearest_free(reach, cell(sx, sz)))
    return out


def plan_from_seed(map_name, start_xz, heading, radius, side, waypoints, targets, log):
    """Seed -> nominal course -> flown route -> .campath. Reuses the pipeline."""
    log("loading bake")
    bake = fp.Bake(FOLDER, map_name)

    log("building the cost grid")
    coarse, blocked, cost, cell_m = fp.build_cost(bake)
    g = fp.ROUTE_GRID
    reach = fp.largest_free_region(blocked)

    fy, fx = bake.h // g, bake.w // g

    def cell(wx, wz):
        c, r = bake.texel_of(wx, wz)
        return (int(np.clip(round(r / fy), 0, g - 1)),
                int(np.clip(round(c / fx), 0, g - 1)))

    if blocked[cell(*start_xz)]:
        raise RuntimeError("the start point is inside an obstacle - "
                           "click somewhere clear")

    log("walking the departure leg")
    want = max(30.0, min(radius * 0.4, 90.0))
    leg, got = departure_leg(bake, blocked, g, start_xz, heading, want)
    if got < 12.0:
        raise RuntimeError("that heading is blocked %.0f m out - drag a "
                           "different direction, or move the start" % got)

    # Targets replace the ring rather than adding to it. The ring only ever
    # existed to invent a route shape when there was nothing to go on; once
    # there are points to visit, THEY are the shape, and overlaying a circle on
    # top would drag the route away from the places it was told to go.
    if targets:
        log("routing through %d target%s" % (len(targets),
                                             "" if len(targets) == 1 else "s"))
        chain = [cell(*leg[-1])]
        for (tx, tz) in targets:
            chain.append(fp.nearest_free(reach, cell(tx, tz)))
        chain.append(fp.nearest_free(reach, cell(*start_xz)))
    else:
        log("seeding the ring")
        chain = [cell(*leg[-1])] + ring_after(bake, reach, g, start_xz, heading,
                                              leg[-1], radius, waypoints, side)

    log("routing between waypoints")
    cells = []
    for i in range(len(chain) - 1):
        part = fp.astar(cost, chain[i], chain[i + 1])
        if part is None:
            raise RuntimeError("no route from waypoint %d to %d" % (i, i + 1))
        cells.extend(part[:-1])

    xs = [p[0] for p in leg]
    zs = [p[1] for p in leg]
    xs += [bake.world_of((c + 0.5) * fx, (r + 0.5) * fy)[0] for r, c in cells]
    zs += [bake.world_of((c + 0.5) * fx, (r + 0.5) * fy)[1] for r, c in cells]

    log("smoothing the nominal course")
    x, z, dx, dz, total = fp.smooth_closed(xs, zs, fp.SAMPLE_STEP,
                                           float(len(xs)) * 8.0)


    # The pipeline downstream reads the nominal course off disk, so write it
    # exactly where it expects to find it rather than re-plumbing three scripts.
    import csv
    plan_csv = os.path.join(FOLDER, map_name + "_plan.csv")
    with open(plan_csv, "w", newline="") as f:
        w = csv.writer(f)
        w.writerow(["i", "s_m", "x", "y", "z", "heading_rad"])
        for j in range(len(x)):
            w.writerow([j, round(j * fp.SAMPLE_STEP, 2),
                        round(float(x[j]), 3), 0.0, round(float(z[j]), 3),
                        round(float(math.atan2(dx[j], dz[j])), 5)])

    log("flying it - this is the slow part")
    argv = sys.argv
    sys.argv = ["export_cam_path.py", map_name]

    # Forward the exporter's own diagnostics into the Studio log. They are the
    # only report of what the smoothing actually achieved - tightest turn,
    # corners relaxed, corners the map would not give up - and they were going
    # to a console that nobody running the GUI ever sees.
    class _Tee:
        def __init__(self, sink):
            self.sink = sink
            self.busy = False
            self.buf = ""

        def write(self, chunk):
            # sink is None under pythonw.exe, which has no stdout at all.
            if self.sink is not None:
                self.sink.write(chunk)
            # Re-entrancy guard. Each finished line is forwarded to log(), and
            # if that callback ever writes to stdout - a print left in while
            # debugging - the write lands back here and recurses until the
            # stack blows. The Studio's own _log posts to Tk and is safe; a
            # caller's need not be, and a hung generate is a bad way to find out.
            if self.busy:
                return
            self.buf += chunk
            while "\n" in self.buf:
                line, self.buf = self.buf.split("\n", 1)
                if line.strip():
                    self.busy = True
                    try:
                        log("  " + line.strip())
                    finally:
                        self.busy = False

        def flush(self):
            if self.sink is not None:
                self.sink.flush()

    real_stdout = sys.stdout
    sys.stdout = _Tee(real_stdout)
    try:
        # Into the scratch folder beside the other diagnostics, NOT cam_paths.
        # Generating used to publish, so one stray click on a map that already
        # had a good route replaced it with nothing to undo from.
        # The clicks go into the file with the route they produced. A flown
        # path cannot be reversed back into the start and targets that made
        # it, so without this the intent behind a route exists nowhere.
        ex.main(out_dir=FOLDER,
                seed=cp.pack_seed(start=start_xz, heading=heading,
                                  radius=radius, waypoints=waypoints,
                                  side=side, targets=targets))
    finally:
        sys.stdout = real_stdout
        sys.argv = argv

    return os.path.join(FOLDER, map_name + "_campath.csv")


# --------------------------------------------------------------------------
# Window
# --------------------------------------------------------------------------

def _hex_rgb(h):
    """#rrggbb -> (r, g, b). PIL will not take the string form for a fill."""
    h = h.lstrip("#")
    if len(h) == 3:
        h = "".join(c * 2 for c in h)
    try:
        return (int(h[0:2], 16), int(h[2:4], 16), int(h[4:6], 16))
    except (ValueError, IndexError):
        return (255, 217, 160)


def dashed(d, a, b, fill, on=9.0, off=7.0, width=1):
    """A dashed line between two points, dashed along its own length."""
    ax, ay = a
    bx, by = b
    L = math.hypot(bx - ax, by - ay)
    if L < 1.0:
        return
    ux, uy = (bx - ax) / L, (by - ay) / L
    t = 0.0
    while t < L:
        t2 = min(t + on, L)
        d.line([(ax + ux * t, ay + uy * t), (ax + ux * t2, ay + uy * t2)],
               fill=fill, width=width)
        t = t2 + off


class Studio:
    def __init__(self, root):
        self.root = root
        root.title("nuTerra Path Studio")
        # Before any widget is built - a style set afterwards leaves whatever
        # was created first wearing the old one.
        apply_dark(root)
        self.bake = None
        self.map_name = None
        self.base = None
        self.photo = None
        self.start = None
        self.heading = None
        self.drag = None
        self.route = None
        self.targets = []
        self.busy = False
        self.mask_full = None    # the mask at bake resolution, resized to fit
        self.view = CANVAS       # side of the square the map is drawn in
        self.ox = self.oy = 0    # where that square sits in the canvas

        # The visible window into the bake, in TEXELS: origin plus a side
        # length. Texels rather than pixels because the window survives a
        # resize - the canvas can change size without moving the map.
        self.zoom = 1.0
        self.cx = 0.0
        self.cy = 0.0
        self.pan_from = None     # (mouse x, mouse y, cx, cy) while panning
        self._resize_job = None

        # Light entities. Each is {"x", "z", "color", "level"} in WORLD metres,
        # like every other placement here - view coordinates change with zoom and
        # a light must not move because the map was scrolled.
        self.lights = []

        # What is selected, as (kind, index): ("light", i), ("target", i) or
        # ("start", 0). One selection, because dragging two things at once has
        # no meaning and a list would only invite it.
        self.selection = None
        self.moving = False      # a selection is being dragged right now
        self.add_light = False   # next left click drops a light

        # Lights have been touched since the last save. Tracked separately from
        # the route because they are saved by a different route: the path is
        # published from a freshly generated scratch file, lights can be edited
        # on a route that was loaded from disk and has no scratch file at all.
        self.lights_dirty = False

        left = ttk.Frame(root, padding=8)
        left.grid(row=0, column=0, sticky="ns")
        ttk.Label(left, text="Maps with a bake").grid(row=0, column=0, sticky="w")

        self.maps = tk.Listbox(left, width=26, height=18, exportselection=False,
                               bg=PANEL, fg=FG, selectbackground=ACCENT,
                               selectforeground="#0b0d12", highlightthickness=0,
                               borderwidth=0, activestyle="none")
        self.maps.grid(row=1, column=0, pady=(2, 2))
        self.maps.bind("<<ListboxSelect>>", lambda e: self.load_selected())

        # The odd spaces, after the rotation list rather than mixed into it.
        self.row_names = []
        self.other_names = []
        self.baked = set()
        self.other_lbl = ttk.Label(left, text="Other spaces", style="Muted.TLabel")
        self.other_lbl.grid(row=2, column=0, sticky="w")
        self.other_combo = ttk.Combobox(left, width=24, state="readonly")
        self.other_combo.grid(row=3, column=0, sticky="we", pady=(0, 8))
        self.other_combo.bind("<<ComboboxSelected>>", self._pick_other)

        r = 4
        self.vars = {}
        for key, label, lo, hi, init in (
                ("radius", "Loop radius (m)", 60, 600, 260),
                ("waypoints", "Waypoints", 6, 28, 14),
                ("agl", "Height over ground (m)", 1, 30, int(nav.AGL)),
                ("standoff", "Standoff (m)", 2, 14, int(nav.BODY_R))):
            ttk.Label(left, text=label).grid(row=r, column=0, sticky="w")
            v = tk.IntVar(value=init)
            self.vars[key] = v
            sc = ttk.Scale(left, from_=lo, to=hi, variable=v, orient="horizontal",
                           length=200, command=lambda *_: self.refresh_labels())
            sc.grid(row=r + 1, column=0, sticky="we")
            self.vars[key + "_w"] = sc
            lbl = ttk.Label(left, text=str(init))
            lbl.grid(row=r + 1, column=1, sticky="w", padx=(6, 0))
            self.vars[key + "_lbl"] = lbl
            r += 2

        self.side = tk.IntVar(value=1)
        ttk.Label(left, text="Turn").grid(row=r, column=0, sticky="w")
        f = ttk.Frame(left)
        f.grid(row=r + 1, column=0, sticky="w")
        self.side_btns = [
            ttk.Radiobutton(f, text="Left", variable=self.side, value=1),
            ttk.Radiobutton(f, text="Right", variable=self.side, value=-1)]
        for b in self.side_btns:
            b.pack(side="left")
        self.ring_lbl = ttk.Label(left, text="", style="Muted.TLabel",
                                  wraplength=210, justify="left")
        r += 2

        ttk.Button(left, text="Clear targets", command=self.clear_targets
                   ).grid(row=r, column=0, sticky="we", pady=(8, 0))
        r += 1

        self.ring_lbl.grid(row=r, column=0, columnspan=2, sticky="w")
        r += 1

        self.go = ttk.Button(left, text="Generate path", command=self.generate)
        self.go.grid(row=r, column=0, sticky="we", pady=(10, 4))
        self.go.state(["disabled"])
        r += 1

        self.save_btn = ttk.Button(left, text="Save path", command=self.save_path)
        self.save_btn.grid(row=r, column=0, sticky="we", pady=(0, 4))
        self.save_btn.state(["disabled"])
        r += 1

        # ---- lights -------------------------------------------------------
        # A mode rather than a modifier: placing several lights in a row is the
        # normal case, and holding a key through all of them is not.
        self.light_btn = ttk.Button(left, text="Add Light",
                                    command=self.toggle_add_light)
        self.light_btn.grid(row=r, column=0, sticky="we", pady=(10, 2))
        r += 1

        # The swatch IS the button - a colour control that does not show its
        # colour makes you click it to find out what it is set to.
        self.light_color = "#ffd9a0"
        self.color_btn = tk.Button(left, text="Colour", command=self.pick_color,
                                   bg=self.light_color, activebackground=self.light_color,
                                   relief="groove", bd=2)
        self.color_btn.grid(row=r, column=0, sticky="we", pady=(0, 2))
        r += 1

        ttk.Label(left, text="Level").grid(row=r, column=0, sticky="w")
        self.light_level = tk.DoubleVar(value=1.0)
        self.level_lbl = ttk.Label(left, text="1.00")
        self.level_lbl.grid(row=r, column=1, sticky="w", padx=(6, 0))
        r += 1
        ttk.Scale(left, from_=0.0, to=1.0, variable=self.light_level,
                  orient="horizontal", length=200,
                  command=lambda *_: self.on_level_change()
                  ).grid(row=r, column=0, sticky="we")
        r += 1

        ttk.Label(left, text="Range (m)").grid(row=r, column=0, sticky="w")
        self.light_range = tk.DoubleVar(value=12.0)
        self.range_lbl = ttk.Label(left, text="12.0")
        self.range_lbl.grid(row=r, column=1, sticky="w", padx=(6, 0))
        r += 1
        # 0.1 to 50. The top end is a guess and will stay one until nuTerra is
        # wired for multiple lights and the number can be looked at rather than
        # reasoned about - 50 m is almost certainly too much for a point light
        # on this scale of map.
        ttk.Scale(left, from_=0.1, to=50.0, variable=self.light_range,
                  orient="horizontal", length=200,
                  command=lambda *_: self.on_range_change()
                  ).grid(row=r, column=0, sticky="we")
        r += 1

        ttk.Label(left, text="Height (m)").grid(row=r, column=0, sticky="w")
        self.light_height = tk.DoubleVar(value=3.0)
        self.height_lbl = ttk.Label(left, text="3.0")
        self.height_lbl.grid(row=r, column=1, sticky="w", padx=(6, 0))
        r += 1
        # Metres ABOVE THE TERRAIN, not absolute. Path Studio is a 2D map and
        # has no idea what the ground does under a click, so the height is an
        # offset and nuTerra resolves the ground when it places the light.
        # 0 puts it on the dirt; 3 is about a street lamp.
        ttk.Scale(left, from_=0.0, to=30.0, variable=self.light_height,
                  orient="horizontal", length=200,
                  command=lambda *_: self.on_height_change()
                  ).grid(row=r, column=0, sticky="we")
        r += 1

        ttk.Separator(left, orient="horizontal").grid(
            row=r, column=0, columnspan=2, sticky="we", pady=(10, 8))
        r += 1

        self.status = tk.StringVar(value="pick a map")
        ttk.Label(left, textvariable=self.status, wraplength=210,
                  justify="left").grid(row=r, column=0, columnspan=2, sticky="w")
        r += 1

        # The controls, written down. Every one of these is a mouse gesture or a
        # bare key with nothing on screen to discover it from - the buttons
        # above document themselves, this half does not.
        ttk.Separator(left, orient="horizontal").grid(
            row=r, column=0, columnspan=2, sticky="we", pady=(12, 6))
        r += 1
        ttk.Label(left, text="Notes", style="Head.TLabel").grid(
            row=r, column=0, sticky="w")
        r += 1
        ttk.Label(left, justify="left", style="Note.TLabel", text=(
            "Left drag      start + heading\n"
            "Right click    add a target\n"
            "Backspace      remove the last target,\n"
            "               or the last LIGHT while\n"
            "               Add Light is armed\n"
            "\n"
            "Shift + click  select a light or a point\n"
            "Drag           move what is selected\n"
            "Esc            drop it / stop placing\n"
            "\n"
            "Middle drag    pan\n"
            "Wheel          zoom at the cursor")
        ).grid(row=r, column=0, columnspan=2, sticky="w")

        self.canvas = tk.Canvas(root, width=CANVAS, height=CANVAS,
                                bg="#11141c", highlightthickness=0)
        self.canvas.grid(row=0, column=1, padx=(0, 8), pady=8, sticky="nsew")
        root.columnconfigure(1, weight=1)
        root.rowconfigure(0, weight=1)
        root.minsize(560, 420)
        self.canvas.bind("<Configure>", self.on_resize)
        self.canvas.bind("<Button-1>", self.on_press)
        self.canvas.bind("<B1-Motion>", self.on_drag)
        self.canvas.bind("<ButtonRelease-1>", self.on_release)
        self.canvas.bind("<Button-3>", self.on_target)
        self.canvas.bind("<MouseWheel>", self.on_wheel)

        # MIDDLE button to pan. Left already sets the start and drags the
        # heading, right adds a target - both are placements, and stealing
        # either for navigation would mean every pan risked moving the plan.
        self.canvas.bind("<Button-2>", self.on_pan_press)
        self.canvas.bind("<B2-Motion>", self.on_pan_drag)
        # Bound on the ROOT, not the canvas - a Canvas only sees key events when
        # it holds focus, and clicking a slider takes focus away, so bound there
        # Backspace would work until the moment you touched a control.
        root.bind("<BackSpace>", self.on_undo_target)
        root.bind("<Delete>", self.on_undo_target)
        # Same reason as Backspace above: bound on the ROOT so it still fires
        # after a slider has taken focus away from the canvas.
        root.bind("<Escape>", self.on_escape)

        self.find_maps()

    # -------------------------------------------------------------- resize

    def on_resize(self, e):
        """Re-fit the map to the window.

        Debounced. A drag of the window edge fires Configure dozens of times,
        and re-scaling a 1024 square image on every one of them makes the whole
        window judder. 90 ms after the last event is imperceptible and does the
        work once.
        """
        if self._resize_job is not None:
            self.root.after_cancel(self._resize_job)
        self._resize_job = self.root.after(90, self._apply_resize)

    def _apply_resize(self):
        self._resize_job = None
        w = max(MIN_VIEW, self.canvas.winfo_width())
        h = max(MIN_VIEW, self.canvas.winfo_height())
        # The map is square, so take the largest square that fits and centre it.
        # Letterboxing rather than stretching: a stretched collision mask lies
        # about distances, and every judgement made in this window is about
        # whether something will fit.
        view = max(MIN_VIEW, min(w, h))
        if view == self.view and self.ox == (w - view) // 2:
            return
        self.view = view
        self.ox = (w - view) // 2
        self.oy = (h - view) // 2
        self.repaint()

    # ---------------------------------------------------------------- maps

    def _pick_other(self, _e=None):
        i = self.other_combo.current()
        if 0 <= i < len(self.other_names):
            self.load_named(self.other_names[i])

    def read_split(self):
        """nuTerra's battle/other split, if it has run.

        Written by MapMenuScreen from scripts/arena_defs/<space>.xml - a space
        is a battle arena when it declares teamBasePositions. Read rather than
        re-derived, so there is one answer and no second packed-XML reader here.
        Missing file means no opinion, and everything baked is listed.
        """
        battle, other = set(), set()
        try:
            with open(os.path.join(os.environ.get("TEMP", "."),
                                   "nuTerra", "map_split.txt")) as f:
                for line in f:
                    line = line.strip()
                    if line.startswith("battle="):
                        battle.add(line[7:])
                    elif line.startswith("other="):
                        other.add(line[6:])
        except OSError:
            pass
        return battle, other

    def find_maps(self):
        """List the same maps nuTerra does, not just the ones already baked.

        Listing only baked maps showed a single entry and gave no idea what else
        was possible. The split file names all 73 spaces, so the list mirrors
        nuTerra's grid - battle arenas here, the rest in the dropdown - and the
        ones without a bake are MARKED rather than hidden. A map that cannot be
        planned yet is still worth seeing, along with why.
        """
        baked = set()
        if os.path.isdir(FOLDER):
            for f in os.listdir(FOLDER):
                if f.endswith("_meta.txt"):
                    baked.add(f[:-9])
        self.baked = baked

        battle, other = self.read_split()
        if battle:
            listed = sorted(battle)
            others = sorted(other | (baked - battle - other))
        else:
            # No split written yet - nuTerra has not run. Fall back to bakes.
            listed, others = sorted(baked), []

        self.row_names = listed
        for n in listed:
            self.maps.insert("end", n if n in baked else n + "    (no bake)")

        self.other_names = others
        self.other_combo.configure(
            values=[n if n in baked else n + "    (no bake)" for n in others])
        self.other_combo.state(["!disabled"] if others else ["disabled"])
        self.other_lbl.configure(
            text="Other spaces (%d) - no team bases" % len(others)
            if others else "Other spaces - none")

        n_ready = len([n for n in listed if n in baked])
        if not listed:
            self.status.set("No map list and no bakes. Open a map in nuTerra - "
                            "it writes both.")
        else:
            self.status.set("%d battle arenas, %d baked and ready. Open a map in "
                            "nuTerra to bake it." % (len(listed), n_ready))

    def load_selected(self):
        sel = self.maps.curselection()
        if not sel or self.busy:
            return
        i = sel[0]
        self.load_named(self.row_names[i] if i < len(self.row_names)
                        else self.maps.get(i))

    def load_named(self, name):
        if self.busy or not name:
            return
        if name not in getattr(self, "baked", ()):  # nothing to draw or plan
            self.status.set("%s has no bake yet - open it once in nuTerra, "
                            "which writes one on map load." % name)
            return
        self.status.set("loading " + name)
        self.root.update_idletasks()
        try:
            self.bake = fp.Bake(FOLDER, name)
        except Exception as e:
            self.status.set("could not load: %s" % e)
            return
        self.map_name = name
        self.start = self.heading = self.route = None

        # Show the route this map already has, AND the clicks that made it.
        # Opening a map planned weeks ago and being shown a blank mask invites
        # planning it again from scratch without meaning to; showing the route
        # but not the seed invites the same thing one step later, because there
        # is nothing to adjust - only something to admire.
        self.zoom = 1.0
        self.cx = self.cy = 0.0

        # Cleared on EVERY load, whether the new map has lights or not.
        #
        # Leaving them would be worse than losing them: the list would still
        # hold the last map's lights, at the last map's world coordinates, and
        # the next Save would write them onto this map's file. Selection goes
        # with them - an index into a list that has been replaced.
        self.lights = []
        self.selection = None
        self.moving = False
        self.add_light = False

        self.route, seed, self.lights = existing_plan(name)
        # Just read from the file, so by definition they match it.
        self.lights_dirty = False
        self.refresh_light_ui()
        self.route_saved = self.route is not None
        self.targets = []
        # Whatever was generated belonged to the previous map.
        self.pending = None

        if seed:
            self.start = seed["start"]
            # Only meaningful with a start to depart from.
            self.heading = seed["heading"] if seed["start"] else None
            self.targets = list(seed["targets"])
            if seed["radius"]:
                self.vars["radius"].set(int(round(seed["radius"])))
            if seed["waypoints"]:
                self.vars["waypoints"].set(int(seed["waypoints"]))
            if seed["side"]:
                self.side.set(int(seed["side"]))

        self.render_mask()
        self.update_enabled()
        self.go.state(["!disabled"])
        if self.route_saved:
            self.status.set("%s loaded, showing the saved path (%d points). "
                            "Left-drag a new start to replace it." %
                            (name, len(self.route)))
        else:
            self.status.set("%s loaded. Left-drag sets start and heading, "
                            "right click adds a target, Backspace undoes one." % name)

    # -------------------------------------------------------------- drawing

    def render_mask(self):
        """The collision mask, shaded the same way the navigator's picture is."""
        b = self.bake
        o = b.obstacle
        img = np.zeros((b.h, b.w, 3), dtype=np.uint8)
        g = b.floor
        gn = (g - g.min()) / max(1e-6, (g.max() - g.min()))
        img[..., 0] = (14 + 22 * gn).astype(np.uint8)
        img[..., 1] = (18 + 27 * gn).astype(np.uint8)
        img[..., 2] = (26 + 35 * gn).astype(np.uint8)

        cut = max(0.1, self.vars["agl"].get() - nav.MARGIN)
        low = (o > 0.4) & (o <= cut)
        img[low] = (66, 72, 82)
        hard = o > cut
        t = np.clip((o - cut) / 20.0, 0, 1)
        img[hard, 0] = (150 + 105 * t[hard]).astype(np.uint8)
        img[hard, 1] = (110 + 90 * t[hard]).astype(np.uint8)
        img[hard, 2] = (20 + 40 * t[hard]).astype(np.uint8)

        # Kept at bake resolution. Resizing on paint costs a LANCZOS pass and
        # keeps every zoom level sharp; re-deriving the shading each time would
        # redo the numpy work for nothing.
        # MIRRORED on X for display.
        #
        # The bake is not wrong - its meta says col 0 is wx_min, world_of and
        # texel_of are exact inverses of that, MapFlightBake probes the mapping
        # against the CPU height function at load, and every route planned
        # through it flies real geometry with no clips. Drawn straight, though,
        # it comes out mirrored against how the map reads in nuTerra, and a
        # planning view that disagrees with the view you fly is worse than
        # useless - you would place a start on the wrong side of the map.
        #
        # Flip the picture once, here, and mirror the column in to_view and
        # to_world so clicks land where they look. Nothing else has to know.
        self.mask_full = Image.fromarray(img[:, ::-1], "RGB")
        self.repaint()

    def repaint(self):
        if self.mask_full is None:
            return
        # Resize FROM a box rather than cropping first: the box takes
        # floats, so the visible window does not have to snap to whole texels
        # and the transforms above stay exact at every zoom.
        crop = self.crop_side()
        im = self.mask_full.resize((self.view, self.view), Image.LANCZOS,
                                   box=(self.cx, self.cy,
                                        self.cx + crop, self.cy + crop))
        d = ImageDraw.Draw(im)

        if self.route:
            pts = [self.to_view(x, z) for (x, z) in self.route]
            d.line(pts + [pts[0]], fill=(255, 46, 168), width=3, joint="curve")

        if self.targets:
            tv = [self.to_view(tx, tz) for (tx, tz) in self.targets]
            seq = ([self.to_view(*self.start)] if self.start else []) + tv
            # Dash ALONG each link, not by dropping alternate links. The first
            # version skipped every other segment, which reads as the line
            # missing a target rather than as a dashed line.
            for i in range(len(seq) - 1):
                dashed(d, seq[i], seq[i + 1], (90, 200, 230))
            if self.start and len(tv) > 1:
                # and back to the start, which is where the route actually ends
                dashed(d, seq[-1], seq[0], (70, 150, 180))
            for i, (tx, ty) in enumerate(tv):
                d.ellipse([tx - 6, ty - 6, tx + 6, ty + 6],
                          fill=(70, 210, 245), outline=(255, 255, 255))
                d.text((tx + 9, ty - 6), str(i + 1), fill=(190, 240, 255))

        if self.start:
            px, py = self.to_view(*self.start)
            d.ellipse([px - 7, py - 7, px + 7, py + 7],
                      fill=(80, 255, 130), outline=(255, 255, 255))
            if self.drag:
                dv = (self.drag[0] - self.ox, self.drag[1] - self.oy)
                d.line([(px, py), dv], fill=(80, 255, 130), width=3)
                d.ellipse([dv[0] - 4, dv[1] - 4, dv[0] + 4, dv[1] + 4],
                          fill=(255, 255, 255))
            elif self.heading is not None:
                # The departure heading, drawn the same way the drag shows it.
                # Taken 60 m out in WORLD space and then projected, so it lands
                # where the route actually leaves rather than at some angle that
                # only looks right at one zoom level.
                hx = self.start[0] + 60.0 * math.sin(self.heading)
                hz = self.start[1] + 60.0 * math.cos(self.heading)
                hv = self.to_view(hx, hz)
                d.line([(px, py), hv], fill=(80, 255, 130), width=3)
                d.ellipse([hv[0] - 4, hv[1] - 4, hv[0] + 4, hv[1] + 4],
                          fill=(255, 255, 255))

        # Lights last, so they sit above the route and its waypoints - they
        # are the thing being edited when they are on screen at all.
        for i, lt in enumerate(self.lights):
            lx, ly = self.to_view(lt["x"], lt["z"])
            rgb = _hex_rgb(lt["color"])
            lvl = max(0.0, min(1.0, float(lt["level"])))
            # Level shown as SIZE as well as fill, because a dim light and a
            # dark-coloured light look identical otherwise.
            rad = 4.0 + 5.0 * lvl
            body = tuple(int(c * (0.25 + 0.75 * lvl)) for c in rgb)
            # The range as a ring in WORLD metres, so it scales with zoom
            # and can be judged against the map rather than against the icon.
            rm = float(lt.get("range", 12.0))
            ex, ey = self.to_view(lt["x"] + rm, lt["z"])
            rr = abs(ex - lx)
            if rr > 1.5:
                d.ellipse([lx - rr, ly - rr, lx + rr, ly + rr], outline=rgb)

            d.ellipse([lx - rad, ly - rad, lx + rad, ly + rad],
                      fill=body, outline=(255, 255, 255))
            if self.selection == ("light", i):
                d.ellipse([lx - rad - 4, ly - rad - 4, lx + rad + 4, ly + rad + 4],
                          outline=(255, 255, 255))

        # A selected path point gets the same ring, so "selected" looks like one
        # thing whatever kind of thing it is.
        if self.selection and self.selection[0] in ("target", "start"):
            kind, i = self.selection
            w = self.targets[i] if kind == "target" else self.start
            if w is not None:
                sx, sy = self.to_view(*w)
                d.ellipse([sx - 11, sy - 11, sx + 11, sy + 11],
                          outline=(255, 255, 255))

        self.photo = ImageTk.PhotoImage(im)
        self.canvas.delete("all")
        self.canvas.create_image(self.ox, self.oy, anchor="nw", image=self.photo)

    # ------------------------------------------------------------ transforms

    def crop_side(self):
        """Side of the visible window, in texels."""
        return float(self.bake.w) / self.zoom

    def mirror_col(self, c):
        """Bake column <-> display column. Its own inverse."""
        return (self.bake.w - 1) - c

    def to_view(self, wx, wz):
        """World -> pixels INSIDE the map square (what gets drawn into)."""
        c, r = self.bake.texel_of(wx, wz)
        c = self.mirror_col(c)
        crop = self.crop_side()
        s = self.view / crop
        return ((c - self.cx) * s, (r - self.cy) * s)

    def to_world(self, px, py):
        """Canvas pixels -> world. Takes the letterbox offset off first."""
        crop = self.crop_side()
        s = crop / self.view
        c = self.mirror_col((px - self.ox) * s + self.cx)
        return self.bake.world_of(c, (py - self.oy) * s + self.cy)

    def clamp_window(self):
        """Keep the visible window inside the bake.

        Without this, zooming out at the edge walks the window off the map and
        leaves a band of whatever PIL pads with, which reads as terrain that
        is not there.
        """
        crop = self.crop_side()
        hi = max(0.0, float(self.bake.w) - crop)
        self.cx = min(max(self.cx, 0.0), hi)
        self.cy = min(max(self.cy, 0.0), hi)

    def on_pan_press(self, e):
        """Anchor the pan: where the mouse was, and where the window was."""
        if self.bake is None:
            return
        self.pan_from = (e.x, e.y, self.cx, self.cy)

    def on_pan_drag(self, e):
        """Drag the map under the cursor.

        Anchored to the press rather than accumulated per motion event: adding
        up deltas drifts, because the window gets clamped at the map edge and a
        clamped step is smaller than the mouse actually moved. Solving from the
        original anchor every time means running into an edge and coming back
        leaves the map exactly where it started.
        """
        if self.bake is None or self.pan_from is None:
            return
        ax, ay, acx, acy = self.pan_from

        # Pixels to texels. Negative because the map follows the cursor: drag
        # right and the window has to move LEFT to bring the map with it.
        s = self.crop_side() / self.view
        self.cx = acx - (e.x - ax) * s
        self.cy = acy - (e.y - ay) * s
        self.clamp_window()
        self.repaint()

    def on_wheel(self, e):
        """Zoom about the cursor: the texel under it does not move."""
        if self.bake is None:
            return

        # Position inside the map square, not the canvas - the square is
        # letterboxed, so those differ whenever the window is not square.
        vx = e.x - self.ox
        vy = e.y - self.oy
        if not (0 <= vx <= self.view and 0 <= vy <= self.view):
            return

        crop = self.crop_side()
        # The texel under the cursor, which is the whole point: solve for the
        # new origin that puts this same texel back under the same pixel.
        tx = self.cx + (vx / self.view) * crop
        ty = self.cy + (vy / self.view) * crop

        step = ZOOM_STEP if e.delta > 0 else 1.0 / ZOOM_STEP
        self.zoom = min(max(self.zoom * step, 1.0), MAX_ZOOM)

        new_crop = self.crop_side()
        self.cx = tx - (vx / self.view) * new_crop
        self.cy = ty - (vy / self.view) * new_crop
        self.clamp_window()
        self.repaint()

    # ---------------------------------------------------------------- input

    # ---------------------------------------------------------------- lights

    def toggle_add_light(self):
        """Arm or disarm light placement."""
        self.add_light = not self.add_light
        # Placing and selecting are different intentions; being in one should
        # not leave the other half-active.
        if self.add_light:
            self.selection = None
        self.refresh_light_ui()
        self.status.set("click to place a light - Esc to stop" if self.add_light
                        else "%d light%s" % (len(self.lights),
                                             "" if len(self.lights) == 1 else "s"))
        self.repaint()

    def refresh_light_ui(self):
        self.light_btn.configure(
            text="Placing... (Esc)" if self.add_light else "Add Light")
        self.color_btn.configure(bg=self.light_color,
                                 activebackground=self.light_color)

    def selected_light(self):
        """The selected light dict, or None when the selection is not a light."""
        if self.selection and self.selection[0] == "light":
            return self.lights[self.selection[1]]
        return None

    def pick_color(self):
        from tkinter import colorchooser
        rgb, hx = colorchooser.askcolor(color=self.light_color,
                                        title="Light colour")
        if not hx:
            return
        self.light_color = hx
        # Editing the selected light if there is one, otherwise setting what the
        # NEXT light will be. The control does double duty because a separate
        # "apply to selection" button would be a click with no decision in it.
        lt = self.selected_light()
        if lt is not None:
            lt["color"] = hx
            self.lights_dirty = True
            self.update_enabled()
        self.refresh_light_ui()
        self.repaint()

    def on_level_change(self):
        v = float(self.light_level.get())
        self.level_lbl.configure(text="%.2f" % v)
        lt = self.selected_light()
        if lt is not None:
            lt["level"] = v
            self.lights_dirty = True
            self.update_enabled()
            self.repaint()

    def on_range_change(self):
        v = float(self.light_range.get())
        self.range_lbl.configure(text="%.1f" % v)
        lt = self.selected_light()
        if lt is not None:
            lt["range"] = v
            self.lights_dirty = True
            self.update_enabled()
            self.repaint()

    def on_height_change(self):
        v = float(self.light_height.get())
        self.height_lbl.configure(text="%.1f" % v)
        lt = self.selected_light()
        if lt is not None:
            lt["height"] = v
            self.lights_dirty = True
            self.update_enabled()
            # No repaint: height is not drawn. A 2D map cannot show it, and
            # pretending otherwise with a size change would collide with the
            # range ring, which IS a distance on this map.
            self.status.set("height %.1f m" % v)

    def to_canvas(self, wx, wz):
        """World -> CANVAS pixels, which is what a mouse event is in."""
        vx, vy = self.to_view(wx, wz)
        return (vx + self.ox, vy + self.oy)

    def pick_entity(self, e, radius=11.0):
        """Nearest light or path point to the click, or None.

        Lights are tested first and win ties: they sit on top visually, and a
        light dropped on a waypoint would otherwise be unreachable.
        """
        best = None
        best_d = radius
        for i, lt in enumerate(self.lights):
            cx, cy = self.to_canvas(lt["x"], lt["z"])
            d = math.hypot(cx - e.x, cy - e.y)
            if d <= best_d:
                best, best_d = ("light", i), d
        if best is not None:
            return best
        for i, (tx, tz) in enumerate(self.targets):
            cx, cy = self.to_canvas(tx, tz)
            d = math.hypot(cx - e.x, cy - e.y)
            if d <= best_d:
                best, best_d = ("target", i), d
        if self.start is not None:
            cx, cy = self.to_canvas(*self.start)
            if math.hypot(cx - e.x, cy - e.y) <= best_d:
                best = ("start", 0)
        return best

    def move_selection(self, e):
        """Put the selected entity under the cursor."""
        if not self.selection:
            return
        wx, wz = self.to_world(e.x, e.y)
        kind, i = self.selection
        if kind == "light":
            self.lights[i]["x"], self.lights[i]["z"] = wx, wz
            self.lights_dirty = True
            self.update_enabled()
        elif kind == "target":
            self.targets[i] = (wx, wz)
            # The generated route no longer matches the targets it was built
            # from, so it must not stay on screen claiming otherwise.
            self.route = None
            self.update_enabled()
        else:
            self.start = (wx, wz)
            self.route = None
            self.update_enabled()

    def on_escape(self, _e=None):
        """Drop the selection and leave placement mode."""
        self.add_light = False
        self.selection = None
        self.moving = False
        self.refresh_light_ui()
        self.repaint()

    # ---------------------------------------------------------------- mouse

    def on_press(self, e):
        if self.bake is None or self.busy:
            return

        # SHIFT selects, and never places. Held down, the click cannot set the
        # start, cannot drop a light, and cannot clear the route - it only picks
        # what is already there.
        if e.state & 0x0001:
            self.selection = self.pick_entity(e)
            self.moving = self.selection is not None
            if self.selection:
                lt = self.selected_light()
                if lt is not None:
                    # Bring the controls to the light rather than the other way
                    # round, so editing it does not first overwrite it.
                    self.light_color = lt["color"]
                    self.light_level.set(lt["level"])
                    self.level_lbl.configure(text="%.2f" % lt["level"])
                    # .get with a default: a light placed before the range
                    # slider existed has no such key.
                    rng = float(lt.get("range", 12.0))
                    self.light_range.set(rng)
                    self.range_lbl.configure(text="%.1f" % rng)
                    hgt = float(lt.get("height", 3.0))
                    self.light_height.set(hgt)
                    self.height_lbl.configure(text="%.1f" % hgt)
                    self.refresh_light_ui()
                self.status.set("%s selected - drag to move, Esc to drop"
                                % self.selection[0])
            else:
                self.status.set("nothing under the cursor")
            self.repaint()
            return

        if self.add_light:
            wx, wz = self.to_world(e.x, e.y)
            self.lights.append({"x": wx, "z": wz,
                                "color": self.light_color,
                                "level": float(self.light_level.get()),
                                "range": float(self.light_range.get()),
                                "height": float(self.light_height.get())})
            self.lights_dirty = True
            self.update_enabled()
            self.status.set("%d light%s - Esc to stop placing"
                            % (len(self.lights),
                               "" if len(self.lights) == 1 else "s"))
            self.repaint()
            return

        self.start = self.to_world(e.x, e.y)
        self.drag = (e.x, e.y)
        self.heading = None
        self.route = None
        # The route just went away, so anything that depends on there being one
        # has to be told. Without this a Save left enabled by an earlier
        # Generate stayed enabled with nothing on screen, and would have
        # published a route the window was no longer showing.
        self.update_enabled()
        self.repaint()

    def on_drag(self, e):
        if self.busy:
            return
        # Moving a selection takes precedence over the heading drag - they are
        # both left-button drags and only one of them can own the gesture.
        if self.moving:
            self.move_selection(e)
            self.repaint()
            return
        if self.start is None:
            return
        self.drag = (e.x, e.y)
        self.repaint()

    def on_release(self, e):
        if self.busy:
            return
        # A move ends here but the selection SURVIVES, so the colour and level
        # controls still act on what was just placed.
        if self.moving:
            self.moving = False
            self.repaint()
            return
        if self.start is None:
            return
        wx, wz = self.to_world(e.x, e.y)
        dx, dz = wx - self.start[0], wz - self.start[1]
        if math.hypot(dx, dz) < 4.0:
            self.status.set("drag further - the line sets the heading")
            return
        self.heading = math.atan2(dx, dz)
        self.status.set("start (%.0f, %.0f) heading %.0f deg. Generate when ready."
                        % (self.start[0], self.start[1], math.degrees(self.heading)))
        self.repaint()

    def on_target(self, e):
        """Right click adds a target the route must visit, in click order."""
        if self.bake is None or self.busy:
            return
        self.targets.append(self.to_world(e.x, e.y))
        self.route = None
        self.status.set("%d target%s - right click to add, Clear to start over"
                        % (len(self.targets), "" if len(self.targets) == 1 else "s"))
        self.update_enabled()
        self.repaint()

    def save_path(self):
        """Publish the generated route to cam_paths.

        The one place that writes the file nuTerra flies. Generate leaves its
        result in the scratch folder and this copies it over, after asking -
        the whole point of the split is that replacing a tuned route should be
        a decision rather than a side effect.
        """
        dst_dir = cp.campath_dir()
        os.makedirs(dst_dir, exist_ok=True)
        dst = os.path.join(dst_dir, self.map_name + ".campath")

        src = getattr(self, "pending", None)
        fresh = bool(src and os.path.exists(src))

        if not fresh:
            # Lights only. The route on disk is not touched and not re-asked
            # about: nothing is being replaced except the light block, which is
            # what the user just edited.
            #
            # src and dst are the same file on purpose. copy_with_lights reads
            # the whole thing before it opens the output, so reading and
            # writing one path is safe - and rewriting only the light block is
            # what keeps the route bit for bit identical.
            if not os.path.exists(dst):
                self.status.set("nothing generated to save")
                return
            try:
                cp.copy_with_lights(dst, dst, self.light_rows())
            except Exception as e:
                self.status.set("could not save lights: %s" % e)
                return
            self.lights_dirty = False
            self.update_enabled()
            self.status.set("saved %d light%s to %s"
                            % (len(self.lights),
                               "" if len(self.lights) == 1 else "s", dst))
            return

        if os.path.exists(dst):
            # Say what is about to be lost, not just that something is. "Are
            # you sure" with no subject is a question nobody can answer.
            try:
                hdr, pts = cp.read_path(dst)
                have = "%d points over %.0f m" % (len(pts), hdr["total_len"])
            except Exception:
                have = "an unreadable file"
            if not messagebox.askyesno(
                    "Overwrite the saved path?",
                    "%s already has a saved path - %s.\n\n"
                    "Replace it with the one just generated?\n\n"
                    "This is the file nuTerra flies. There is no undo."
                    % (self.map_name, have),
                    icon="warning", default="no", parent=self.root):
                self.status.set("kept the existing path for " + self.map_name)
                return

        try:
            # Not a plain copy any more: the generated file in scratch knows
            # nothing about lights, which are placed after it was made.
            cp.copy_with_lights(src, dst, self.light_rows())
        except Exception as e:
            self.status.set("could not save: %s" % e)
            return

        self.route_saved = True
        self.update_enabled()
        self.status.set("saved to %s (%d light%s)"
                        % (dst, len(self.lights),
                           "" if len(self.lights) == 1 else "s"))

    def light_rows(self):
        """The lights in the shape cam_path.pack_light takes.

        One place, used by both save paths. The editor's key names and the
        writer's argument names differ - "range" is a builtin and "height" is
        an offset, not a coordinate - and translating that in two places is how
        they drift.
        """
        return [{"x": lt["x"], "z": lt["z"], "color": lt["color"],
                 "level": lt["level"], "rng": lt.get("range", 12.0),
                 "y": lt.get("height", 3.0)}
                for lt in self.lights]

    def update_enabled(self):
        """Grey out the ring controls when targets are driving the route.

        Targets replace the ring entirely, so Turn, Loop radius and Waypoints do
        nothing the moment there is one - and a control that looks live but is
        ignored is worse than no control. Right-clicking a target used to
        silently kill the Left/Right buttons with no sign of it.
        """
        # Only when there is something generated and not yet published. A
        # route loaded from disk is already saved and Save has nothing to do.
        # Two independent reasons to enable Save, because there are two
        # things that can be unsaved. Without the second one, lights could be
        # placed on a route loaded from disk and never written anywhere - the
        # button stayed grey and the only way out was to regenerate the path.
        fresh_route = (getattr(self, "pending", None) is not None
                       and not getattr(self, "route_saved", False))
        dirty_lights = (self.lights_dirty and self.map_name is not None
                        and os.path.exists(os.path.join(
                            cp.campath_dir(), self.map_name + ".campath")))
        can_save = (fresh_route or dirty_lights) and not self.busy
        self.save_btn.state(["!disabled" if can_save else "disabled"])

        ring = not self.targets
        state = "!disabled" if ring else "disabled"
        for b in self.side_btns:
            b.state([state])
        for k in ("radius", "waypoints"):
            self.vars[k + "_w"].state([state])
        self.ring_lbl.configure(
            text="" if ring else
            "Turn, radius and waypoints are unused - your targets set the route.")

    def on_undo_target(self, _e=None):
        """Backspace or Delete drops the most recently placed thing.

        WHICH thing depends on the mode. While placing lights it is the last
        light - the key that undoes a placement has to undo the placement you
        are actually making, or it quietly eats a waypoint out of the route
        while you are looking at the lights.
        """
        if self.busy:
            return

        if self.add_light:
            if not self.lights:
                self.status.set("no lights to remove")
                return
            self.lights.pop()
            # A selected LIGHT may have been the one just removed, or may now
            # index past the end of a shorter list. A selected target is not
            # affected and is left alone.
            if self.selection and self.selection[0] == "light":
                self.selection = None
                self.moving = False
            self.lights_dirty = True
            n = len(self.lights)
            self.status.set("removed the last light - %d left" % n if n
                            else "removed the last light - none left")
            self.update_enabled()
            self.repaint()
            return

        if not self.targets:
            return
        self.targets.pop()
        self.route = None
        n = len(self.targets)
        self.status.set("removed the last target - %d left"
                        % n if n else "removed the last target - none left")
        self.update_enabled()
        self.repaint()

    def clear_targets(self):
        if self.busy:
            return
        self.targets = []
        self.route = None
        self.status.set("targets cleared - Turn, radius and waypoints apply again")
        self.update_enabled()
        self.repaint()

    def refresh_labels(self):
        for k in ("radius", "waypoints", "agl", "standoff"):
            self.vars[k + "_lbl"].configure(text=str(self.vars[k].get()))
        if self.mask_full is not None and not self.busy:
            self.render_mask()

    # ------------------------------------------------------------- generate

    def generate(self):
        if self.busy or self.bake is None:
            return
        if self.start is None or self.heading is None:
            self.status.set("click a start and drag a heading first")
            return
        self.busy = True
        self.go.state(["disabled"])
        threading.Thread(target=self._run, daemon=True).start()

    def _log(self, msg):
        self.root.after(0, lambda: self.status.set(msg))

    def _run(self):
        try:
            # The pipeline reads its envelope off module globals, so set them
            # from the sliders before anything downstream is called.
            nav.AGL = float(self.vars["agl"].get())
            nav.MARGIN = min(0.5, nav.AGL * 0.5)
            nav.BLOCK_H = nav.AGL - nav.MARGIN
            nav.BODY_R = float(self.vars["standoff"].get())
            fp.FLIGHT_AGL = nav.AGL
            fp.FLIGHT_MARGIN = nav.MARGIN
            fp.FLIGHT_BLOCK_H = nav.BLOCK_H
            fp.BODY_RADIUS = nav.BODY_R

            csv_path = plan_from_seed(
                self.map_name, self.start, self.heading,
                float(self.vars["radius"].get()), self.side.get(),
                int(self.vars["waypoints"].get()), list(self.targets), self._log)

            import csv as _csv
            rows = list(_csv.DictReader(open(csv_path)))
            route = [(float(r["x"]), float(r["z"])) for r in rows]
            # The scratch copy Generate just wrote. Save publishes it.
            out = os.path.join(FOLDER, self.map_name + ".campath")
            self.root.after(0, lambda: self._done(route, len(rows), out))
        except BaseException:
            # BaseException, not Exception. Anything that escapes this thread
            # leaves the UI stuck disabled with no message, which is the worst
            # possible failure mode - a window that looks broken and says
            # nothing. SystemExit from a called library did exactly that.
            tb = traceback.format_exc().strip().splitlines()[-1]
            self.root.after(0, lambda: self._failed(tb))

    def _done(self, route, n, out):
        self.route = route
        self.route_saved = False
        self.pending = out
        self.busy = False
        self.go.state(["!disabled"])
        self.update_enabled()
        self.repaint()
        self.status.set("wrote %d points to %s" % (n, out))

    def _failed(self, msg):
        self.busy = False
        self.go.state(["!disabled"])
        self.update_enabled()
        self.status.set("failed: " + msg)


def main():
    root = tk.Tk()
    Studio(root)
    root.mainloop()


if __name__ == "__main__":
    main()
