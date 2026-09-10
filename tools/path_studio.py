"""
Path Studio - pick a map, click a start, click the points, generate a flight.

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
import struct
import sys
import threading
import time
import traceback

import numpy as np
from scipy import ndimage

import shutil
import tkinter as tk
from tkinter import ttk, messagebox
from PIL import Image, ImageTk, ImageDraw

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import radar_commit as nav
import smooth_path
import radar_tangent as tang
import path_cards as pcd
import flight_plan as fp
import export_cam_path as ex
import cam_path as cp
import fog_curve as fc
import terrain_bake as tb

FOLDER = nav.FOLDER

# Sweeps drawn for the radar overlay. Every step would be 664 fans of 121 rays
# on a monastery route - eighty thousand lines, which is both unreadable and
# slow. Sixty is enough to see where the returns come from.
RADAR_SWEEPS = 60

# The invented ring's shape, when there is nothing to go on.
#
# plan_from_seed only lays a ring when NO points were placed - targets replace
# it entirely - and left click places points now, so the ring is the empty-hands
# case and nothing more. These were a Waypoints slider and a Left/Right pair of
# radio buttons, greyed out the moment a point existed, which is most of the
# time. A control that is disabled whenever anyone would want it is not a
# control, and Loop radius went the same way for the same reason - its slider
# is Path smoothing now.
#
# The ring and the departure leg are GONE, and with them the drag.
#
# Both existed to invent a route shape out of a click and a dragged heading,
# from the days when there was nothing else to go on. There is now: the
# points. A heading that is only ever used to aim a leg at the first point is
# a worse way of saying where the first point is, and it also served as the
# route's end - one gesture doing two unrelated jobs, badly.
#
# cam_path still writes seed_heading, seed_radius, seed_waypoints and
# seed_side into the campath - a binary format with old files in the wild -
# so they stay in the FILE at their defaults. Nothing here reads them.


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
            "curve": int(lt.get("curve", 0)),
            # The shape - the same fields a street lamp bulb carries. A file
            # from before them reads back as a point light aimed down.
            "kind": int(lt.get("kind", 0)),
            "aim": tuple(lt.get("aim", (0.0, -1.0, 0.0))),
            "cone": float(lt.get("cone", 0.0)),
            "blend": float(lt.get("blend", 0.0)),
            "ang0": float(lt.get("ang0", 0.0)),
            "ang1": float(lt.get("ang1", 0.0)),
            "vol_mix": float(lt.get("vol_mix", 1.0)),
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
BUTTON = "#3f4d68"   # buttons: blue-grey
BUTTON_OFF = "#252a36"  # and the same button disabled - darker, flatter


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

    # Blue-grey buttons, and a disabled state that reads as OFF rather than
    # as a slightly different button: darker face, dim text, flat border.
    # "disabled" is listed first because the first matching state wins.
    st.configure("TButton", background=BUTTON, foreground=FG,
                 bordercolor="#55637f", focusthickness=1, padding=4)
    st.map("TButton",
           background=[("disabled", BUTTON_OFF), ("pressed", "#33405a"),
                       ("active", "#4d5d7d")],
           foreground=[("disabled", "#5a6272")],
           bordercolor=[("disabled", "#2a2f3c")])

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

# The map the Studio opens on when nothing else is asked for. A name, or
# None for the picker. Overridden by a positional argument.
START_MAP = "19_monastery"


def plan_from_seed(map_name, start_xz, targets, log,
                   smooth_passes=2, on_step=None, average_n=0):
    """Start + points -> nominal course -> flown route -> .campath.

    No heading. The course runs from the start through the points in click
    order and back, and the direction it leaves in is simply the direction of
    the first point.
    """
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

    if not targets:
        raise RuntimeError("no points to visit - click the map to add some")

    log("routing through %d point%s" % (len(targets),
                                        "" if len(targets) == 1 else "s"))
    chain = [fp.nearest_free(reach, cell(*start_xz))]
    for (tx, tz) in targets:
        chain.append(fp.nearest_free(reach, cell(tx, tz)))
    chain.append(fp.nearest_free(reach, cell(*start_xz)))

    log("routing between waypoints")
    cells = []
    for i in range(len(chain) - 1):
        part = fp.astar(cost, chain[i], chain[i + 1])
        if part is None:
            raise RuntimeError("no route from waypoint %d to %d" % (i, i + 1))
        cells.extend(part[:-1])

    # The course is the routed cells and nothing else. It used to be the
    # departure leg's world points with the cells appended after them; with
    # the leg gone there is only one source, and the start is already the
    # first cell of the chain.
    xs = [bake.world_of((c + 0.5) * fx, (r + 0.5) * fy)[0] for r, c in cells]
    zs = [bake.world_of((c + 0.5) * fx, (r + 0.5) * fy)[1] for r, c in cells]

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

    # How hard to round the flown route's corners.
    #
    # SMOOTH_ITERS is Chaikin passes over the flown path, each one cutting
    # every corner again, and it is the only knob that changes how abrupt a
    # turn the camera makes - MIN_RADIUS next to it is reported and not
    # enforced, so it describes the problem rather than fixing it. It was a
    # hard 2. The slider that used to set the loop radius drives it now: the
    # ring that radius shaped is only ever laid when no points were placed,
    # and clicking places points, so the control was doing nothing on the
    # routes anyone actually generates.
    ex.SMOOTH_ITERS = int(smooth_passes)
    log("smoothing: %d Chaikin pass%s"
        % (ex.SMOOTH_ITERS, "" if ex.SMOOTH_ITERS == 1 else "es"))

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
                # heading, radius, waypoints and side keep their defaults.
                # The fields stay in the file; nothing puts a value in them.
                seed=cp.pack_seed(start=start_xz, targets=targets),
                on_step=on_step, average_n=average_n)
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


# --------------------------------------------------------------------------
# Lights: the same shape controls as the street lamps
# --------------------------------------------------------------------------
# A map light carries the same fields a street lamp bulb does (cam_path.py,
# "Light record"), and the editor below shows the same controls the Light Bulb
# Placer in nuTerra shows for one - type, aim, the two half angles, blend,
# colour, level, range, fog mix, shaft curve - plus the height over the terrain
# a map light needs, and our fog curve editor.

KIND_NAMES = ("point", "cone", "inverse cone", "dual cowl")
KIND_POINT, KIND_CONE, KIND_INVERSE, KIND_DUAL = 0, 1, 2, 3

LIGHT_DEFAULTS = {"kind": KIND_POINT, "aim": (0.0, -1.0, 0.0), "cone": 0.0,
                  "blend": 0.0, "ang0": 0.0, "ang1": 0.0, "vol_mix": 1.0,
                  "level": 1.0, "range": 12.0, "height": 3.0, "curve": 0,
                  "color": "#ffd9a0"}


def new_light(x, z, **over):
    lt = dict(LIGHT_DEFAULTS)
    lt["x"], lt["z"] = x, z
    lt.update(over)
    return lt


def _write_cur(path, im, hot):
    """A classic .cur: BITMAPINFOHEADER, 32-bit XOR bitmap, 1-bit AND mask.
    Written by hand because Tk loads cursors through the OS, which does not
    take the PNG-compressed entries Pillow writes into an .ico."""
    w, h = im.size
    px = im.load()
    xor = bytearray()
    for y in range(h - 1, -1, -1):              # bottom-up rows
        for x in range(w):
            r, g, b, a = px[x, y]
            xor += bytes((b, g, r, a))
    row = ((w + 31) // 32) * 4
    andm = bytearray()
    for y in range(h - 1, -1, -1):
        bits = bytearray(row)
        for x in range(w):
            if px[x, y][3] < 128:
                bits[x // 8] |= 0x80 >> (x % 8)
        andm += bits
    bih = struct.pack("<IiiHHIIiiII", 40, w, h * 2, 1, 32, 0,
                      len(xor) + len(andm), 0, 0, 0, 0)
    img = bih + bytes(xor) + bytes(andm)
    head = struct.pack("<HHH", 0, 2, 1)
    entry = struct.pack("<BBBBHHII", w, h, 0, 0, hot[0], hot[1], len(img), 22)
    with open(path, "wb") as f:
        f.write(head + entry + img)


def bulb_cursor():
    """A light-bulb mouse cursor for Add Light mode, drawn once into the
    flight folder. Tk on Windows takes a .cur by path; anywhere that fails
    the crosshair stands in."""
    path = os.path.join(FOLDER, "bulb.cur")
    try:
        if not os.path.exists(path):
            os.makedirs(FOLDER, exist_ok=True)
            im = Image.new("RGBA", (32, 32), (0, 0, 0, 0))
            d = ImageDraw.Draw(im)
            ink = (30, 30, 36, 255)
            glass = (255, 228, 140, 255)
            d.ellipse([9, 3, 23, 17], fill=glass, outline=ink)          # glass
            d.rectangle([13, 17, 19, 21], fill=glass, outline=ink)      # neck
            d.rectangle([12, 21, 20, 26], fill=(160, 165, 178, 255), outline=ink)  # cap
            d.line([12, 23, 20, 23], fill=ink)
            d.line([12, 25, 20, 25], fill=ink)
            for a, b in (((2, 10), (6, 10)), ((26, 10), (30, 10)),
                         ((4, 3), (7, 6)), ((28, 3), (25, 6)), ((16, 0), (16, 1))):
                d.line([a, b], fill=(255, 245, 190, 255), width=2)
            _write_cur(path, im, (16, 24))
        return "@" + path.replace("\\", "/")
    except Exception:
        return "crosshair"


class ShapeView:
    """The light's shape, drawn the way the Bulb Placer draws it but flat: a
    side elevation through the aim axis. Range as a dashed circle, a cone as
    its edges with the lit wedge tinted, an inverse cone as the DARK wedge (in
    blue, as the Placer draws it - it is the part that is NOT lit), a dual
    cowl as the lit band between its two cuts with green rims."""

    SIZE = 360

    def __init__(self, editor):
        self.editor = editor
        top = tk.Toplevel(editor.top)
        self.top = top
        top.title("Light shape")
        top.configure(bg=BG)
        top.resizable(False, False)
        top.protocol("WM_DELETE_WINDOW", self.close)
        self.c = tk.Canvas(top, width=self.SIZE, height=self.SIZE, bg="#11141c",
                           highlightthickness=0)
        self.c.pack()
        self.redraw(editor.work)

    def close(self):
        self.editor.shape = None
        self.top.destroy()

    def redraw(self, w):
        c = self.c
        c.delete("all")
        S = self.SIZE
        rng = max(0.1, float(w.get("range", 12.0)))
        scale = (S * 0.40) / rng                  # px per metre
        lx, ly = S * 0.5, S * 0.42
        col = w.get("color", "#ffd9a0")
        kind = int(w.get("kind", KIND_POINT))

        # the ground, `height` metres under the light
        h = float(w.get("height", 3.0))
        gy = ly + h * scale
        if gy < S - 6:
            c.create_line(8, gy, S - 8, gy, fill="#6b5d3a", dash=(4, 3))
            c.create_text(S - 10, gy - 8, text="ground", fill=MUTED,
                          anchor="e", font=("Consolas", 8))

        R = rng * scale
        c.create_oval(lx - R, ly - R, lx + R, ly + R, outline=col, dash=(3, 3))

        # the aim axis in this plane: x right = the horizontal part of the
        # aim, y down = minus its vertical part
        ax, ay, az = w.get("aim", (0.0, -1.0, 0.0))
        horiz = math.hypot(ax, az)
        L = math.hypot(horiz, ay)
        if L < 1e-6:
            horiz, ay, L = 0.0, -1.0, 1.0
        ux, uy = horiz / L, -ay / L

        def at(theta_deg, length):
            t = math.radians(theta_deg)
            ex = ux * math.cos(t) - uy * math.sin(t)
            ey = ux * math.sin(t) + uy * math.cos(t)
            return lx + ex * length, ly + ey * length

        def wedge(a_from, a_to, length):
            pts = [(lx, ly)]
            n = max(2, int(abs(a_to - a_from) / 3.0))
            for k in range(n + 1):
                pts.append(at(a_from + (a_to - a_from) * k / n, length))
            return pts

        if kind == KIND_POINT:
            c.create_oval(lx - R, ly - R, lx + R, ly + R, fill=col,
                          stipple="gray25", outline="")
            note = "point - lights every direction"
        else:
            a0 = float(w.get("ang0", 0.0))
            a1 = float(w.get("ang1", 0.0))
            c.create_line(lx, ly, *at(0, R * 1.02), fill="#8fd0ff", arrow="last")
            if kind == KIND_CONE:
                c.create_polygon(wedge(-a1, a1, R), fill=col, stipple="gray25", outline="")
                for s in (1, -1):
                    c.create_line(lx, ly, *at(s * a1, R), fill=col, width=2)
                    c.create_line(lx, ly, *at(s * a0, R), fill=col, dash=(2, 3))
                note = "cone - lit inside %.0f deg, hot inside %.0f deg" % (a1, a0)
            elif kind == KIND_INVERSE:
                c.create_oval(lx - R, ly - R, lx + R, ly + R, fill=col,
                              stipple="gray25", outline="")
                c.create_polygon(wedge(-a1, a1, R * 1.01), fill="#11141c", outline="")
                c.create_polygon(wedge(-a0, a0, R * 1.01), fill="#2a3d78",
                                 stipple="gray50", outline="")
                for s in (1, -1):
                    c.create_line(lx, ly, *at(s * a0, R), fill="#5d8bff", width=2)
                    c.create_line(lx, ly, *at(s * a1, R), fill="#5d8bff", dash=(2, 3))
                note = "inverse cone - DARK inside %.0f deg, soft to %.0f deg" % (a0, a1)
            else:
                for s in (1, -1):
                    c.create_polygon(wedge(s * a0, s * a1, R), fill=col,
                                     stipple="gray25", outline="")
                    c.create_line(lx, ly, *at(s * a0, R), fill="#6ff0a0", width=2)
                    c.create_line(lx, ly, *at(s * a1, R), fill="#6ff0a0", width=2)
                note = "dual cowl - lit BETWEEN %.0f and %.0f deg (band %.0f)" % (
                    a0, a1, a1 - a0)

        c.create_oval(lx - 5, ly - 5, lx + 5, ly + 5, fill="#ffffff", outline="")
        c.create_text(10, S - 26, text=note, fill=FG, anchor="w", font=("Consolas", 9))
        c.create_text(10, S - 12, text="range %.1f m   level %.2f   fog mix %.2f" % (
            rng, float(w.get("level", 1.0)), float(w.get("vol_mix", 1.0))),
            fill=MUTED, anchor="w", font=("Consolas", 9))


class LightEditor:
    """One light's controls, in their own window.

    The same set the Light Bulb Placer shows for a street lamp, with the
    height over the terrain a map light needs and our fog curve editor. Works
    on a COPY: OK writes it back to the light, Cancel or the window's X drops
    it - asking first if anything was changed - and so does leaving Add Light
    mode, which is why the Studio can close this from outside.
    """

    def __init__(self, studio, index, light, on_ok, on_close):
        self.studio = studio
        self.index = index
        self.work = dict(LIGHT_DEFAULTS)
        self.work.update(light)
        self.work["aim"] = tuple(self.work.get("aim", (0.0, -1.0, 0.0)))
        self.on_ok = on_ok
        self.on_close = on_close
        self.changed = False
        self.shape = None
        self._quiet = False        # a programmatic slider set must not count
        self.vars = {}

        top = tk.Toplevel(studio.root)
        self.top = top
        top.title("Light %d" % (index + 1))
        top.configure(bg=BG)
        top.transient(studio.root)
        top.resizable(False, False)
        top.protocol("WM_DELETE_WINDOW", self.cancel)
        # Backspace and Delete reach the Studio from here too - the editor
        # holds the keyboard while it is up, and the light it is on is the
        # selected one. Not while an aim number is being typed, though.
        top.bind("<BackSpace>", self._key_delete)
        top.bind("<Delete>", self._key_delete)

        f = ttk.Frame(top, padding=10)
        f.grid(row=0, column=0, sticky="nsew")
        self.f = f
        r = 0

        # Type, as radio buttons: four kinds is few enough to show them all.
        ttk.Label(f, text="type", style="Muted.TLabel").grid(row=r, column=0, sticky="w")
        r += 1
        self.kind = tk.IntVar(value=int(self.work["kind"]))
        kf = ttk.Frame(f)
        kf.grid(row=r, column=0, columnspan=2, sticky="w")
        r += 1
        for k, name in enumerate(KIND_NAMES):
            ttk.Radiobutton(kf, text=name, value=k, variable=self.kind,
                            command=self.on_kind).grid(row=k // 2, column=k % 2,
                                                       sticky="w", padx=(0, 12))

        # The aim, as numbers, the way the Placer shows its aim point - but as
        # an OFFSET from the light in metres, so moving the light on the map
        # does not re-aim it.
        self.aim_lbl = ttk.Label(f, text="aim - metres from the light: x, y, z (down is 0, -1, 0)",
                                 style="Muted.TLabel")
        self.aim_lbl.grid(row=r, column=0, columnspan=2, sticky="w", pady=(6, 0))
        r += 1
        af = ttk.Frame(f)
        af.grid(row=r, column=0, columnspan=2, sticky="w")
        r += 1
        self.aim_vars = []
        self.aim_entries = []
        for j in range(3):
            v = tk.StringVar(value="%.2f" % self.work["aim"][j])
            e = ttk.Entry(af, textvariable=v, width=8)
            e.grid(row=0, column=j, padx=(0, 4))
            e.bind("<FocusOut>", self.on_aim)
            e.bind("<Return>", self.on_aim)
            self.aim_vars.append(v)
            self.aim_entries.append(e)

        # The angles - rebuilt whenever the kind changes, because what the two
        # numbers mean depends on it and the labels have to say which.
        self.ang_frame = ttk.Frame(f)
        self.ang_frame.grid(row=r, column=0, columnspan=2, sticky="we", pady=(6, 0))
        r += 1

        # Colour: the swatch IS the button.
        self.color_btn = tk.Button(f, text="Colour", command=self.pick_color,
                                   bg=self.work["color"], activebackground=self.work["color"],
                                   relief="groove", bd=2)
        self.color_btn.grid(row=r, column=0, columnspan=2, sticky="we", pady=(8, 2))
        r += 1

        r = self.slider(f, r, "Level", "level", 0.0, 1.0, "%.2f")
        r = self.slider(f, r, "Range (m)", "range", 0.1, 50.0, "%.1f")
        # Metres ABOVE THE TERRAIN. Path Studio is a 2D map and has no idea
        # what the ground does under a click, so the height is an offset and
        # nuTerra resolves the ground when it places the light.
        r = self.slider(f, r, "Height over ground (m)", "height", 0.0, 30.0, "%.1f")
        r = self.slider(f, r, "Fog mix", "vol_mix", 0.0, 1.0, "%.2f")

        # Which fog falloff curve the lamp's SHAFT uses, and the editor for
        # the curves themselves.
        ttk.Label(f, text="Shaft curve").grid(row=r, column=0, sticky="w")
        self.curve = tk.IntVar(value=int(self.work["curve"]))
        cf = ttk.Frame(f)
        cf.grid(row=r, column=1, sticky="w", padx=(6, 0))
        for k in range(fc.N_CURVES):
            ttk.Radiobutton(cf, text=str(k), value=k, variable=self.curve,
                            command=self.on_curve).pack(side="left")
        r += 1
        ttk.Button(f, text="Curve editor...", command=studio.open_curve_editor).grid(
            row=r, column=0, columnspan=2, sticky="we", pady=(2, 2))
        r += 1
        ttk.Button(f, text="Show shape", command=self.toggle_shape).grid(
            row=r, column=0, columnspan=2, sticky="we", pady=(0, 8))
        r += 1

        bf = ttk.Frame(f)
        bf.grid(row=r, column=0, columnspan=2, sticky="we")
        for col in range(3):
            bf.columnconfigure(col, weight=1)
        ttk.Button(bf, text="OK", command=self.ok).grid(row=0, column=0, sticky="we", padx=(0, 3))
        # Save applies AND writes the lights to the .campath, and the window
        # stays open - the light is on disk, keep tweaking it. OK applies and
        # closes; Save path on the main panel is the same write.
        ttk.Button(bf, text="Save", command=self.save).grid(row=0, column=1, sticky="we", padx=3)
        ttk.Button(bf, text="Cancel", command=self.cancel).grid(row=0, column=2, sticky="we", padx=(3, 0))

        self.rebuild_angles()

        # Beside the main window rather than on top of the map.
        top.update_idletasks()
        rx = studio.root.winfo_rootx() + studio.root.winfo_width() - top.winfo_width() - 30
        ry = studio.root.winfo_rooty() + 60
        top.geometry("+%d+%d" % (max(0, rx), max(0, ry)))

    # ---- widgets ------------------------------------------------------

    def slider(self, parent, row, label, key, lo, hi, fmt, length=220):
        ttk.Label(parent, text=label).grid(row=row, column=0, sticky="w")
        v = tk.DoubleVar(value=float(self.work[key]))
        lbl = ttk.Label(parent, text=fmt % float(self.work[key]))
        lbl.grid(row=row, column=1, sticky="w", padx=(6, 0))
        ttk.Scale(parent, from_=lo, to=hi, variable=v, orient="horizontal",
                  length=length, command=lambda *_: self.on_slider(key)
                  ).grid(row=row + 1, column=0, sticky="we")
        self.vars[key] = (v, lbl, fmt)
        return row + 2

    def set_slider(self, key, val):
        v, lbl, fmt = self.vars[key]
        self._quiet = True
        try:
            v.set(val)
        finally:
            self._quiet = False
        lbl.configure(text=fmt % val)

    def gap(self):
        return 1.0 if self.work["kind"] == KIND_DUAL else 0.5

    def on_slider(self, key):
        if self._quiet or key not in self.vars:
            return
        v, lbl, fmt = self.vars[key]
        val = float(v.get())
        # The two half angles keep their order, the way the Placer keeps it.
        if key == "ang0":
            val = min(val, float(self.work["ang1"]) - self.gap())
        elif key == "ang1":
            val = max(val, float(self.work["ang0"]) + self.gap())
            if self.work["kind"] in (KIND_CONE, KIND_INVERSE):
                # Keep the legacy full-angle field truthful: it is what a
                # build from before the two sliders would read.
                self.work["cone"] = max(2.0, min(178.0, val * 2.0))
        if abs(val - float(v.get())) > 1e-9:
            self.set_slider(key, val)
        else:
            lbl.configure(text=fmt % val)
        self.work[key] = val
        if key in ("ang0", "ang1") and self.band_lbl is not None:
            self.band_lbl.configure(text="band %.0f deg wide" % (
                float(self.work["ang1"]) - float(self.work["ang0"])))
        self.touched()

    def rebuild_angles(self):
        for ch in self.ang_frame.winfo_children():
            ch.destroy()
        for key in ("ang0", "ang1", "blend"):
            self.vars.pop(key, None)
        self.band_lbl = None
        kind = int(self.work["kind"])
        aimed = kind != KIND_POINT
        for e in self.aim_entries:
            e.state(["!disabled" if aimed else "disabled"])
        self.aim_lbl.configure(text=(
            "aim - the AXIS both lobes share: metres from the light, x y z"
            if kind == KIND_DUAL else
            "aim - metres from the light: x, y, z (down is 0, -1, 0)"
            if aimed else "aim - a point light has none"))
        af = self.ang_frame
        r = 0
        if not aimed:
            ttk.Label(af, text="lights every direction", style="Muted.TLabel").grid(
                row=0, column=0, sticky="w")
            return
        if kind == KIND_DUAL:
            # A lamp switched to dual cowl with no band yet would be black.
            if not (float(self.work["ang1"]) > float(self.work["ang0"])):
                self.work["ang0"], self.work["ang1"] = 20.0, 160.0
            ttk.Label(af, text="the band: lit between these two",
                      style="Muted.TLabel").grid(row=r, column=0, sticky="w")
            r += 1
            r = self.slider(af, r, "Cap cut (deg)", "ang0", 0.0, 179.0, "%.0f")
            r = self.slider(af, r, "Base cut (deg)", "ang1", 1.0, 180.0, "%.0f")
            self.band_lbl = ttk.Label(af, text="band %.0f deg wide" % (
                float(self.work["ang1"]) - float(self.work["ang0"])), style="Muted.TLabel")
            self.band_lbl.grid(row=r, column=0, sticky="w")
            r += 1
            # Blend is the softness of BOTH cuts here, and the only control
            # for it - two cuts would need four angles to say it otherwise.
            r = self.slider(af, r, "Edge blend", "blend", 0.0, 1.0, "%.2f")
        else:
            if not (float(self.work["ang1"]) > float(self.work["ang0"])):
                self.work["ang0"], self.work["ang1"] = 20.0, 30.0
                self.work["cone"] = 60.0
            r = self.slider(af, r, "Inner (deg)", "ang0", 0.0, 89.0, "%.0f")
            r = self.slider(af, r, "Outer (deg)", "ang1", 0.5, 89.5, "%.0f")
            # No blend slider on purpose: inner-to-outer IS the soft edge for
            # these two, as the Placer says.
            ttk.Label(af, text="soft edge = inner to outer", style="Muted.TLabel").grid(
                row=r, column=0, sticky="w")

    # ---- handlers -----------------------------------------------------

    def touched(self):
        self.changed = True
        if self.shape is not None:
            self.shape.redraw(self.work)

    def on_kind(self):
        k = int(self.kind.get())
        if k == self.work["kind"]:
            return
        self.work["kind"] = k
        self.rebuild_angles()
        self.touched()

    def on_aim(self, _e=None):
        try:
            aim = tuple(float(v.get()) for v in self.aim_vars)
        except ValueError:
            for j, v in enumerate(self.aim_vars):
                v.set("%.2f" % self.work["aim"][j])
            return
        if aim != tuple(self.work["aim"]):
            self.work["aim"] = aim
            self.touched()

    def on_curve(self):
        self.work["curve"] = int(self.curve.get())
        self.touched()

    def pick_color(self):
        from tkinter import colorchooser
        _rgb, hx = colorchooser.askcolor(color=self.work["color"], title="Light colour",
                                         parent=self.top)
        if not hx:
            return
        self.work["color"] = hx
        self.color_btn.configure(bg=hx, activebackground=hx)
        self.touched()

    def toggle_shape(self):
        if self.shape is not None:
            self.shape.close()
        else:
            self.shape = ShapeView(self)

    def ok(self):
        self.on_aim()
        work = dict(self.work)
        self.destroy()
        self.on_ok(self.index, work)

    def _key_delete(self, _e=None):
        if isinstance(self.top.focus_get(), (ttk.Entry, tk.Entry)):
            return
        self.studio.on_undo_target()

    def save(self):
        """Apply the working copy to the light and write every light to the
        .campath, without closing. After this there is nothing to discard."""
        self.on_aim()
        self.studio.apply_and_save_light(self.index, dict(self.work))
        self.changed = False

    def cancel(self):
        if self.changed and not messagebox.askyesno(
                "Discard the light edits?",
                "This light has changes that were not applied.\n\nClose and lose them?",
                icon="warning", default="no", parent=self.top):
            return
        self.destroy()
        self.on_close()

    def destroy(self):
        if self.shape is not None:
            self.shape.close()
        self.top.destroy()


class AmGrid:
    """Just enough of a Bake for the canvas to draw and locate a click.

    A map with no height bake still has a global_AM in its pkg and a world
    footprint in its chunk names, which is everything the view needs: a size to
    crop against and the two transforms between world and texel. It has no
    floor, no top and no obstacle, so nothing that plans a route will touch it
    - self.bake stays None for exactly that reason, and every planning guard in
    here already tests that.
    """

    def __init__(self, w, h, wx_min, wx_max, wz_min, wz_max):
        self.w = w
        self.h = h
        self.wx_min, self.wx_max = wx_min, wx_max
        self.wz_min, self.wz_max = wz_min, wz_max
        self.mx = (wx_max - wx_min) / w
        self.mz = (wz_max - wz_min) / h

    def world_of(self, col, row):
        return (self.wx_min + (col + 0.5) * self.mx,
                self.wz_max - (row + 0.5) * self.mz)

    def texel_of(self, x, z):
        return ((x - self.wx_min) / self.mx - 0.5,
                (self.wz_max - z) / self.mz - 0.5)


class Studio:
    def __init__(self, root):
        self.root = root
        root.title("nuTerra Path Studio")
        # Before any widget is built - a style set afterwards leaves whatever
        # was created first wearing the old one.
        apply_dark(root)
        self.bake = None
        # What the CANVAS draws against. The bake when there is one, an AmGrid
        # when the map has only a global_AM. Kept apart from self.bake so that
        # every "is there a height map" guard stays a test of self.bake.
        self.view_grid = None
        self.map_name = None
        self.base = None
        self.photo = None
        self.start = None
        # Metres from the start to where the heading drag was released. The
        # marker is redrawn at that distance so it stays where it was dropped.

        self.route = None
        self.targets = []
        self.busy = False
        # A map picked while something else was running. The click is still the
        # signal - it just cannot be served at that instant - so it is held
        # here and honoured the moment the machine is free.
        self.pending_pick = None
        self.mask_full = None    # the mask at bake resolution, resized to fit
        # The radar overlay's cache. Casting 121 rays at 60 places along the
        # route is a second of Python, which is fine once and far too slow on
        # every pan - so it is computed when something it depends on changes
        # and redrawn from world coordinates after that.
        self.radar_fans = None
        self.radar_key = None
        # The rolling average of the route, kept SEPARATELY. It is a second
        # opinion about the same path, not a replacement for it - the flown
        # one stays exactly as the navigator built it and this sits beside it
        # to be compared against.
        self.smooth_buf = None
        self.smooth_key = None
        self.smooth_refused = 0
        # The sample size the CURRENT route was exported with, or None when
        # that is not known (a route loaded from disk).
        self.route_avg_n = None
        self._clear_radar = None
        self._clear_cb = None
        self._clear_key = None
        # The live trace: the navigator itself while it is running, so repaint
        # can draw its path and its last rays as they happen.
        self.live = None
        self.live_stop = False
        # The flight in progress under Generate: (path, fans, steps), handed
        # over by the navigator itself. None whenever nothing is flying.
        self.gen = None
        self.gen_cancel = False
        self.gen_was = None
        # The card search, mid-search. Events only ever get APPENDED by the
        # worker; the repaint timer reads a prefix. Same rule as everything
        # else that crosses a thread here.
        self.cards = None
        self.cards_running = False
        self.cards_shown = True
        # EVERY card, kept apart from the event tail.
        #
        # Cards were drawn out of the last 90 events, so a card filed early
        # had scrolled out of the window long before the search finished and
        # the ones that passed first time were never on screen at all. The
        # rays and rings SHOULD fade - they are a moving scan. A card is a
        # result and stays.
        self.card_paths = []
        self.card_ev = []
        self.card_stop = False
        # The map's saved path, kept from the moment it loads.
        self.loaded_route = []
        self.live_paused = False
        # The trace-step delay, as a PLAIN NUMBER. See _live_step.
        self.live_ms = 40.0
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

        # The map the list points at, whether or not it has a bake - the Bake
        # button works on this. And the global_AM picture for the loaded map,
        # on the bake grid, loaded the first time the underlay is switched on.
        self.selected_name = None
        # True while refill_maps is rebuilding the Listbox. Tk fires
        # <<ListboxSelect>> for a selection set from code exactly as it does
        # for a click, so without this the list cannot be refiltered without
        # loading a map nobody asked for - see load_selected.
        self._refilling = False
        self.am_img = None

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

        # The path is LOCKED until Edit path is pressed: a route loaded from
        # disk survives a stray click, and every control that would change it
        # is greyed until the lock is off. Lights are not part of the lock.
        self.edit_path = False
        # The one open light editor window, or None.
        self.editor = None
        self.light_color = "#ffd9a0"    # colour the NEXT light is placed with
        self.bulb_cursor = bulb_cursor()

        left = ttk.Frame(root, padding=8)
        left.grid(row=0, column=0, sticky="ns")

        # Live search over every space - the arenas in the list and the odd
        # ones in the dropdown - typed above a SHORT list. The list used to be
        # 18 rows under a "Maps with a bake" label; five rows and a filter find
        # a map faster than scrolling ever did.
        self.search = tk.StringVar()
        self.search_box = ttk.Entry(left, textvariable=self.search, width=26)
        self.search_box.grid(row=0, column=0, sticky="we", pady=(0, 2))
        self.search.trace_add("write", lambda *_: self.refill_maps())
        self.visible_names = []

        self.maps = tk.Listbox(left, width=26, height=8, exportselection=False,
                               bg=PANEL, fg=FG, selectbackground=ACCENT,
                               selectforeground="#0b0d12", highlightthickness=0,
                               borderwidth=0, activestyle="none")
        self.maps.grid(row=1, column=0, pady=(2, 2))
        self.maps.bind("<<ListboxSelect>>",
                       lambda e: (self._trace("raw <<ListboxSelect>>"),
                                  self.load_selected()))

        # A PRESS PICKS A MAP. THE MOUSE MOVING AFTERWARDS DOES NOT.
        #
        # selectmode is "browse", and Tk binds:
        #     <B1-Motion>  tk::ListboxMotion %W [%W index @%x,%y]
        #     <B1-Leave>   tk::ListboxAutoScan %W
        # Motion moves the selection to whatever row is under the pointer for
        # as long as the button is down, and every move fires
        # <<ListboxSelect>> - so it is a LOAD each time. Measured: one click
        # with the pointer drifting two pixels loaded three maps, and the last
        # one won. That is "why would it ever change after selecting a map".
        # AutoScan is the same thing off the bottom edge, repeating every 50 ms
        # and scrolling as it goes; on a list eight rows tall the edge is very
        # close to wherever you clicked.
        #
        # This is a list you pick ONE name from. There is nothing to drag, no
        # range to extend, and no reason a click that wobbles should mean
        # anything but the row it landed on. Widget bindings run BEFORE class
        # bindings, so "break" here does stop them - unlike a binding on the
        # root, which arrives too late (see on_space).
        self.maps.bind("<B1-Motion>", lambda e: "break")
        self.maps.bind("<B1-Leave>", lambda e: "break")

        # ONE LINE PER NOTCH.
        #
        # Tk's own binding is `yview scroll [expr {-(%D/120)*4}] units` - four
        # lines a notch, half of this eight-row list, which overshoots every
        # time and puts a different set of rows under the cursor than the one
        # aimed at.
        self.maps.bind("<MouseWheel>",
                       lambda e: (self.maps.yview_scroll(
                           -1 if e.delta > 0 else 1, "units"), "break")[1])

        # TWO pickers, and that is fine.
        #
        # Both are event sources and both do exactly one thing: hand a map NAME
        # to load_named. Nothing else comes out of either. Having two was never
        # the fault - the fault was each one deriving that name from a
        # selection INDEX into its own parallel list, and load_named writing
        # back into whichever widget had not caused the load. Neither happens
        # now: each reads its own text, and nothing writes back to either.
        self.row_names = []
        self.other_names = []
        self.baked = set()
        self.count_lbl = ttk.Label(left, text="", style="Muted.TLabel")
        self.count_lbl.grid(row=2, column=0, sticky="w", pady=(0, 2))
        self.other_combo = ttk.Combobox(left, width=24, state="readonly")
        self.other_combo.grid(row=3, column=0, sticky="we", pady=(0, 8))
        self.other_combo.bind("<<ComboboxSelected>>",
                              lambda e: (self._trace("raw <<ComboboxSelected>>"),
                                         self._pick_other(e)))

        # A bake for a map nuTerra has never opened. TERRAIN ONLY - it reads
        # the heights out of the pkg, so there are no models or trees in it
        # and the obstacle mask is empty. Enough to see the map and place
        # lights; open the map in nuTerra for the real bake, which replaces it.
        self.bake_btn = ttk.Button(left, text="Bake terrain (Python)",
                                   command=self.bake_selected)
        self.bake_btn.grid(row=4, column=0, sticky="we", pady=(0, 4))
        self.bake_btn.state(["disabled"])

        # The map's global_AM - the game's own top-down picture of the ground -
        # under the mask, so the map is recognisable while placing things.
        # Open ground shows it; obstacle cells keep the mask colours.
        f_am = ttk.Frame(left)
        f_am.grid(row=5, column=0, columnspan=2, sticky="we", pady=(0, 6))
        # TWO stacked rows inside the one grid cell, NOT one long row.
        #
        # This frame spans the left column, and a grid column is as wide as its
        # widest child. Packing the blend labels and the radar checkbox beside
        # global_AM made this row ~360 px against a 271 px panel, which widened
        # the column and shoved the map Listbox out of alignment - the map list
        # stopped being clickable where it looked. Stack instead of spread.
        am_row = ttk.Frame(f_am)
        am_row.pack(side="top", fill="x")
        radar_row = ttk.Frame(f_am)
        radar_row.pack(side="top", fill="x", pady=(3, 0))

        # OFF at startup. The photo is for recognising the map while
        # placing things; the moment the question is "what is the navigator
        # doing", it is the thing in the way - a 50% wash of daylight over the
        # rays that are being watched. Tick it when you need to know where you
        # are, not to work.
        self.show_am = tk.BooleanVar(value=False)
        ttk.Checkbutton(am_row, text="global_AM", variable=self.show_am,
                        command=self.on_am_toggle).pack(side="left")
        self.am_blend = tk.DoubleVar(value=0.5)
        ttk.Label(am_row, text="map").pack(side="left", padx=(6, 2))
        ttk.Scale(am_row, from_=0.0, to=1.0, variable=self.am_blend,
                  orient="horizontal", length=90,
                  command=lambda *_: self.on_am_toggle()).pack(side="left")
        ttk.Label(am_row, text="mask").pack(side="left", padx=(2, 0))

        # THE FINISHED IMAGING, AND ONLY WHEN ASKED FOR.
        #
        # This is cast along a path that already exists - every fan, all at
        # once, after the fact. It is not the navigator working; it is a
        # photograph of a flight that is over. Shown by default it appeared
        # the moment a map loaded, so what was on screen after a load was a
        # picture of the LAST path rather than anything happening now, and it
        # buried the live sweep under thousands of standing rays.
        #
        # OFF at startup, off at the start of every generation, and it says so
        # rather than silently showing nothing when there is no path to cast
        # along. The real-time scan is drawn by the flight itself and needs no
        # checkbox.
        self.show_radar = tk.BooleanVar(value=False)
        ttk.Checkbutton(radar_row, text="Show Radar Imaging",
                        variable=self.show_radar,
                        command=self.on_radar_toggle).pack(side="left")

        # THE DASHED LINKS BETWEEN THE POINTS, AND A WAY TO TURN THEM OFF.
        #
        # They are the order the points were clicked in, not the route. The
        # flown path does not follow them and is not meant to - it is pulled
        # taut by the exporter's shortcut, which straightens 204 points down
        # to 7 and misses the points by up to 20 m. Two shapes on one map that
        # disagree by that much read as a bug in the one you are looking at,
        # so this turns the links off and leaves the path to speak for itself.
        #
        # In the same stacked frame as the radar checkbox, NOT beside it: a
        # wider child here stretches the left column and moves the map list.
        links_row = ttk.Frame(f_am)
        links_row.pack(side="top", fill="x", pady=(3, 0))
        self.show_links = tk.BooleanVar(value=True)
        ttk.Checkbutton(links_row, text="Point links",
                        variable=self.show_links,
                        command=self.repaint).pack(side="left")

        # Edit path is the lock on everything below it and on the map clicks
        # that place the start and the points. Off, all of it is greyed.
        self.edit_btn = ttk.Button(left, text="Edit path", command=self.toggle_edit_path)
        self.edit_btn.grid(row=6, column=0, sticky="we", pady=(4, 6))

        r = 7
        self.vars = {}
        for key, label, lo, hi, init in (
                ("smooth", "Path smoothing", 0, 6, 2),
                ("agl", "Height over ground (m)", 1, 30, int(nav.AGL)),
                ("standoff", "Standoff (m)", 0.5, 6, min(6.0, max(0.5, round(nav.BODY_R * 2) / 2.0))),
                # How long the live trace waits between steps. Zero runs it as
                # fast as it can, which is a couple of seconds for a whole
                # route - too quick to watch a single radar sweep. In the same
                # block as the others so the left column keeps its width.
                ("trace_ms", "Trace step (ms)", 0, 300, 40),
                # How many path points go into each average. The path is
                # 2.0 m per point, so this is 4 m at 2 and 48 m at 24 - the
                # number that matters is the METRES, and it changes if the
                # navigator's STEP ever does.
                ("smooth_n", "Smooth sample size", 2, 24, 4)):
            ttk.Label(left, text=label).grid(row=r, column=0, sticky="w")
            # Standoff moves in half metres; the others are whole numbers.
            v = tk.DoubleVar(value=init) if key == "standoff" else tk.IntVar(value=init)
            self.vars[key] = v
            sc = ttk.Scale(left, from_=lo, to=hi, variable=v, orient="horizontal",
                           length=200,
                           command=lambda *_, kk=key: self.refresh_labels(kk))
            sc.grid(row=r + 1, column=0, sticky="we")
            self.vars[key + "_w"] = sc
            lbl = ttk.Label(left, text=str(init))
            lbl.grid(row=r + 1, column=1, sticky="w", padx=(6, 0))
            self.vars[key + "_lbl"] = lbl
            r += 2

        self.ring_lbl = ttk.Label(left, text="", style="Muted.TLabel",
                                  wraplength=210, justify="left")
        r += 2

        self.clear_btn = ttk.Button(left, text="Clear targets", command=self.clear_targets)
        self.clear_btn.grid(row=r, column=0, sticky="we", pady=(8, 0))
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

        # Watch the point-to-point navigator work. Same width as its
        # neighbours - a wider child here stretches the column and moves the
        # map list, which is how the picker broke before.
        self.live_btn = ttk.Button(left, text="Trace live (tangent)",
                                   command=self.trace_live)
        self.live_btn.grid(row=r, column=0, sticky="we", pady=(8, 4))
        self.live_btn.state(["disabled"])
        r += 1

        self.cards_btn = ttk.Button(left, text="Search cards (live)",
                                    command=self.cards_button)
        self.cards_btn.grid(row=r, column=0, sticky="we", pady=(0, 4))
        self.cards_btn.state(["disabled"])
        r += 1

        # ---- lights: their own panel, RIGHT of the map -------------------
        # Path controls stay left, lights go right. The per-light controls are
        # not here any more: a light opens its editor in its own window, with
        # the same set the Light Bulb Placer shows for a street lamp.
        right = ttk.Frame(root, padding=8)
        right.grid(row=0, column=2, sticky="ns")
        rr = 0
        ttk.Label(right, text="Lights", style="Head.TLabel").grid(
            row=rr, column=0, sticky="w")
        rr += 1
        # A mode rather than a modifier: placing several lights in a row is the
        # normal case. While it is on the cursor is a light bulb, and every
        # click drops a light and opens its editor.
        self.light_btn = ttk.Button(right, text="Add Light",
                                    command=self.toggle_add_light)
        self.light_btn.grid(row=rr, column=0, sticky="we", pady=(4, 2))
        rr += 1
        self.edit_light_btn = ttk.Button(right, text="Edit light...",
                                         command=self.edit_selected_light)
        self.edit_light_btn.grid(row=rr, column=0, sticky="we", pady=(0, 2))
        rr += 1
        ttk.Button(right, text="Curve editor...",
                   command=self.open_curve_editor).grid(
            row=rr, column=0, sticky="we", pady=(0, 4))
        rr += 1

        # WHAT THE NAVIGATOR SAID, IN THE GAP THAT GROWS.
        #
        # It has always narrated itself - "ring R=1 m accepted, bearing off by
        # 0.4 deg", "STUCK at (120, -40), 38 m short" - and every line of it
        # used to go to log=lambda m: None. The map shows WHERE it went; this
        # is the only thing that says WHY.
        #
        # It sits in the row that was the spacer, so it takes the slack
        # instead: the lamp buttons stay pinned above it, Notes stays pinned
        # below, and the box between them is however tall the window allows.
        # Nothing to recompute on resize - grid does it.
        #
        # Fed from the repaint timers on the UI thread. The navigator runs on
        # a worker and must never touch Tk, so it appends to a plain list and
        # the timer drains it.
        ttk.Label(right, text="Navigator", style="Head.TLabel").grid(
            row=rr, column=0, sticky="w", pady=(10, 2))
        rr += 1
        self.log_lines = []
        self._log_at = 0
        # width in CHARACTERS, and short enough not to be the widest thing in
        # this column - the Notes block below sets that, and a box wider than
        # it would push the whole panel out.
        self.trace_log = tk.Text(right, width=40, height=8, bg=PANEL, fg=FG,
                                 insertbackground=FG, relief="flat",
                                 highlightthickness=0, wrap="none",
                                 font=("Consolas", 8))
        self.trace_log.grid(row=rr, column=0, sticky="nsew", pady=(0, 6))
        right.rowconfigure(rr, weight=1)      # <- the slack lives here now
        rr += 1
        self.trace_log.insert(
            "end", "what the navigator decides appears here" + chr(10)
                   + "while it flies - Generate or Trace live" + chr(10))
        self.trace_log.configure(state="disabled")
        self.trace_log.tag_configure("wp", foreground="#7fd4ff")
        self.trace_log.tag_configure("hit", foreground="#9dffbe")
        self.trace_log.tag_configure("bad", foreground="#ff8f7a")

        # COPIABLE. It is a debug log; it is worth nothing if it cannot be
        # pasted somewhere. A disabled Text can still be selected with the
        # mouse and answers <<Copy>>, but Tk gives it no Control-a, so
        # select-all is bound here along with a right-click menu.
        self.trace_log.bind("<Control-a>", self._log_select_all)
        self.trace_log.bind("<Control-A>", self._log_select_all)
        self.trace_log.bind("<Button-3>", self._log_menu)
        self.log_menu = tk.Menu(self.trace_log, tearoff=0)
        self.log_menu.add_command(label="Copy selection",
                                  command=lambda: self._log_copy(False))
        self.log_menu.add_command(label="Copy all",
                                  command=lambda: self._log_copy(True))
        self.log_menu.add_separator()
        self.log_menu.add_command(label="Clear", command=self.clear_log)
        ttk.Separator(right, orient="horizontal").grid(
            row=rr, column=0, sticky="we", pady=(12, 6))
        rr += 1
        ttk.Label(right, text="Notes", style="Head.TLabel").grid(
            row=rr, column=0, sticky="sw")
        rr += 1
        # The controls, written down. Every one of these is a mouse gesture or
        # a bare key with nothing on screen to discover it from - the buttons
        # document themselves, this half does not.
        ttk.Label(right, justify="left", style="Note.TLabel", text=(
            "Edit path      unlock the path controls\n"
            "Left click     sets the start (first)\n"
            "Left click     add a point\n"
            "Right click    add a point\n"
            "Backspace /    remove the selected LIGHT,\n"
            "Delete         or the last placed one in\n"
            "               Add Light; else the last\n"
            "               point, then the start\n"
            "\n"
            "Add Light      click the map to place\n"
            "               one - its editor opens.\n"
            "               Add Light again closes\n"
            "               it and drops the edits\n"
            "Shift + click  select a light (opens\n"
            "               its editor) or a point\n"
            "Drag           move what is selected\n"
            "Esc            drop it / stop placing\n"
            "\n"
            "Middle drag    pan\n"
            "Wheel          zoom at the cursor")
        ).grid(row=rr, column=0, sticky="sw")

        ttk.Separator(left, orient="horizontal").grid(
            row=r, column=0, columnspan=2, sticky="we", pady=(10, 8))
        r += 1

        self.status = tk.StringVar(value="pick a map")
        self.status_lbl = ttk.Label(left, textvariable=self.status,
                                    wraplength=210, justify="left")
        self.status_lbl.grid(row=r, column=0, columnspan=2, sticky="w")
        r += 1

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
        # SPACE BELONGS TO THE LIVE TRACE AND TO NOTHING ELSE.
        #
        # Tk gives a key to the focused widget's CLASS binding before the
        # toplevel's, and every control on this panel already does something
        # with space:
        #
        #   TButton, TCheckbutton   ttk::button::activate  - presses it. Space
        #                           after clicking Trace live re-invoked the
        #                           button, which STOPPED the trace instead of
        #                           pausing it.
        #   Listbox                 tk::ListboxBeginSelect %W [%W index active]
        #                           - selects the ACTIVE row and fires
        #                           <<ListboxSelect>>. The active row is not the
        #                           row anyone clicked (activestyle is "none",
        #                           so it is not even drawn), so space loaded a
        #                           map nobody picked. That is the map-picking
        #                           fault all over again, arriving through the
        #                           keyboard this time.
        #
        # Returning "break" from the root binding cannot undo any of that - the
        # class script has already run by then. The script itself has to be
        # replaced, which is what bind_class without add= does. Entry and Text
        # keep theirs: a space typed into the map search has to stay a space.
        for cls in ("TButton", "TCheckbutton", "TRadiobutton", "Listbox"):
            root.bind_class(cls, "<space>", self.on_space)
        root.bind("<space>", self.on_space)

        # Everything starts locked and greyed; a map load and Edit path
        # open things up from here.
        self.update_enabled()
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

    # ---------------------------------------------------------------- trace
    #
    # Who fired what, in what state, and who called it. The pickers kept being
    # "hit again" by code rather than by the operator, and the source could not
    # be reasoned out of the file: Tk delivers a selection made from code and a
    # selection made by a click through the SAME virtual event. So record the
    # call chain through this file at every entry point that touches either
    # widget, and the state of both widgets as it was seen.
    #
    # Off unless PS_PICK_LOG names a file.

    def _trace(self, tag, extra=""):
        path = os.environ.get("PS_PICK_LOG")
        if not path:
            return
        try:
            frames = [f for f in traceback.extract_stack()[:-1]
                      if f.filename.replace(chr(92), "/").endswith("path_studio.py")]
            chain = " < ".join("%s:%d" % (f.name, f.lineno)
                               for f in reversed(frames[-7:]))
            try:
                sel = self.maps.curselection()
                i = sel[0] if sel else -1
                row = self.maps.get(i) if i >= 0 else ""
            except Exception:
                i, row = -2, "?"
            try:
                combo = self.other_combo.get()
            except Exception:
                combo = "?"
            self._trace_n = getattr(self, "_trace_n", 0) + 1
            line = ("#%03d %-16s sel=%-3d row=%-22r combo=%-22r busy=%-5s "
                    "refill=%-5s loaded=%-20r %s%s%s"
                    % (self._trace_n, tag, i, row, combo,
                       getattr(self, "busy", "?"), getattr(self, "_refilling", "?"),
                       getattr(self, "selected_name", None),
                       ("[" + extra + "] ") if extra else "",
                       "<- ", chain))
            with open(path, "a", encoding="utf-8") as f:
                f.write(line + chr(10))
        except Exception:
            pass

    def find_maps(self):
        self._trace("find_maps")
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
        self.other_names = others
        self.refill_maps()

        # Names only. Nothing to parse back out of a value later.
        self.other_combo.configure(values=others)
        self.other_combo.state(["!disabled"] if others else ["disabled"])

        n_all = len(listed) + len(others)
        n_ready = len([n for n in listed + others if n in baked])
        # SHORT. This label shares the left grid column with the map list, and
        # a grid column is as wide as its widest child - a long line here
        # widens the column and shifts the Listbox, which has no sticky, off to
        # the right. Clicks then land beside the rows instead of on them.
        self.count_lbl.configure(text="%d maps, %d baked" % (n_all, n_ready))
        if not n_all:
            self.status.set("No map list and no bakes. Open a map in nuTerra - "
                            "it writes both.")
        else:
            self.status.set("%d maps, %d ready to plan. The rest have no height "
                            "map yet - pick one and it offers to make it."
                            % (n_all, n_ready))

    def _pick_other(self, _e=None):
        """The dropdown changed. Pass the name on, same as a click in the list."""
        self._trace("COMBO-EVENT")
        name = self._row_name(self.other_combo.get())
        if not name:
            return
        if self.busy:
            self.pending_pick = name
            self.status.set("%s: picked - loading when the bake finishes" % name)
            return
        if name == self.selected_name and self.bake is not None:
            return
        self.load_named(name)

    def mark_row_baked(self, name):
        self._trace("mark_row_baked")
        """Restore one row to the has-a-height-map colour, in place.

        Only touches the row's COLOUR. No delete, no insert, no selection, no
        scroll - see _bake_done for what rebuilding the list costs.
        """
        try:
            for i in range(self.maps.size()):
                if self._row_name(self.maps.get(i)) == name:
                    self.maps.itemconfig(i, foreground=FG)
                    break
        except Exception:
            pass
        n_all = len(self.row_names) + len(self.other_names)
        n_ready = len([n for n in self.row_names + self.other_names
                       if n in self.baked])
        # SAME TEXT find_maps WRITES. A longer string here is a wider
        # label, and the label shares its grid column with the map list, which
        # has no sticky and so re-centres when the column changes width. It
        # happens to have slack today - measured 0 px of movement - but that
        # is luck, and the list moving under the cursor after a bake is
        # exactly the fault this picker keeps having.
        self.count_lbl.configure(text="%d maps, %d baked" % (n_all, n_ready))

    def refill_maps(self):
        self._trace("refill_maps")
        """Fill the list from the search box.

        EVERY map, always - the battle arenas first, then the spaces with no
        team bases. The two used to live in separate widgets and the empty
        search showed only the arenas, which meant a map you could see in the
        dropdown was not in the list and vice versa. One list cannot disagree
        with itself.
        """
        q = self.search.get().strip().lower()
        names = self.row_names + self.other_names
        if q:
            names = [n for n in names if q in n.lower()]
        self.visible_names = names
        # Rebuild with the select handler muted. delete() clears the selection
        # and selection_set() re-makes it, and BOTH fire <<ListboxSelect>> -
        # indistinguishable from a click. Every keystroke in the search box
        # therefore fired a load, against a list that had just been refiltered
        # and rescrolled underneath it, so the map that arrived was whatever
        # now sat at that index rather than the one that looked selected.
        # Filtering rebuilds the rows and NOTHING else. It does not restore a
        # selection from selected_name and it does not scroll to it: that is
        # downstream state reaching back into the control that produced it, and
        # re-selecting a row fires the load handler as if it had been clicked.
        # After a refill nothing is selected, which is the truth - the list has
        # just changed under whatever was chosen before.
        #
        # Still muted, because delete() clears the selection and that fires the
        # event too.
        self._refilling = True
        try:
            self.maps.delete(0, "end")
            for i, n in enumerate(names):
                # The row text is the map name and NOTHING else.
                #
                # It used to carry a "    (no bake)" suffix, which meant every
                # read of the list had to strip a magic string, and the strip
                # only worked while its literal matched the one that built the
                # row - two copies, in two methods, forever. A row that says
                # what it is cannot be misread. Bake state is shown by colour
                # instead, which no parser can get wrong.
                self.maps.insert("end", n)
                if n not in self.baked:
                    self.maps.itemconfig(i, foreground=MUTED)
        finally:
            self._refilling = False

    def _row_name(self, text):
        """The map name a row or dropdown entry stands for - which is its text.

        Taken from the WIDGET'S OWN TEXT, never from an index into a parallel
        list. Both load paths used to read a selection index and look it up in
        a list built alongside the widget, and the two only agree while nothing
        has touched either since the last rebuild; when they disagree the index
        still resolves, silently, to the wrong map.

        There is nothing to decode here either - see refill_maps on why the
        rows no longer carry a "(no bake)" suffix.
        """
        return (text or "").strip()

    def load_selected(self):
        self._trace("LISTBOX-EVENT")
        # Only a real click loads. A selection the code just made is not a
        # request for anything.
        if self._refilling:
            return
        sel = self.maps.curselection()
        if not sel:
            return
        name = self._row_name(self.maps.get(sel[0]))
        if not name:
            return

        # BUSY IS NOT A REASON TO DISCARD THE CLICK.
        #
        # It used to return here and the click was gone - while Tk had still
        # moved the highlight, because that is the widget's own doing. So:
        # answer Yes to a bake, click another map while it runs, and the bake
        # finishes and loads ITS map over the top. The list showed the map you
        # picked and the app had loaded a different one. That is "it picks the
        # wrong map after I do".
        #
        # Hold it instead. Whatever finishes will honour the last thing picked.
        if self.busy:
            self.pending_pick = name
            self.status.set("%s: picked - loading when the bake finishes" % name)
            return

        # Already showing it - a re-selection of the same row is not a reload.
        if name == self.selected_name and self.bake is not None:
            return
        self.load_named(name)


    def show_without_bake(self, name):
        self._trace("show_without_bake")
        """A map with no height bake: draw its global_AM and offer to make one.

        The picture comes straight out of the pkg and the world footprint out
        of the chunk names, so this needs nothing that a bake would have
        provided. self.bake stays None, which is what keeps every planning
        control switched off - there is no terrain to plan against, and a route
        drawn over a picture would be a route through geometry nobody has
        measured.
        """
        self.map_name = name
        self.bake = None
        self.am_img = None
        self.start = self.route = None
        self.zoom = 1.0
        self.cx = self.cy = 0.0
        self.close_editor(ask=False)
        self.lights = []
        self.selection = None
        self.moving = False
        self.add_light = False
        self.edit_path = False
        self.edit_btn.configure(text="Edit path")
        self.targets = []
        self.pending = None
        self.route_saved = False
        self.lights_dirty = False
        self.refresh_light_ui()

        self.status.set("%s: no height map - showing the game's own picture" % name)
        self.root.update_idletasks()

        try:
            im = tb.load_global_am(name)
            if im is None:
                raise ValueError("this map has no global_AM in its pkg")
            wx0, wx1, wz0, wz1 = tb.footprint_of(name)
        except Exception as e:
            self.view_grid = None
            self.mask_full = None
            self.canvas.delete("all")
            self.status.set("%s: no height map, and no picture either (%s)" % (name, e))
            self.root.after_idle(self.ask_to_bake, name,
                                 "Its picture could not be read either.")
            return

        # Square, like the bake grid, and mirrored on X for display the same
        # way render_mask mirrors - so a click lands where it looks here too.
        side = max(im.size)
        im = im.resize((side, side), Image.LANCZOS)
        self.view_grid = AmGrid(side, side, wx0, wx1, wz0, wz1)
        self.mask_full = Image.fromarray(
            np.asarray(im.convert("RGB"))[:, ::-1, :].copy(), "RGB")
        self.update_enabled()
        self.repaint()
        # AFTER the click is finished being processed, never inside it.
        #
        # askyesno runs its own event loop, so opening it from within the
        # <<ListboxSelect>> handler stops Tk halfway through delivering the
        # click and pumps the rest of it - button release, the Listbox's own
        # class bindings - underneath the modal. Those land on the list when
        # the dialog closes, and the selection walks to the next row. Saying
        # No to a bake moved the highlight down one, every time.
        #
        # after_idle lets the click finish first. The dialog then opens with
        # nothing left in flight to apply behind it.
        self.root.after_idle(self.ask_to_bake, name)

    def ask_to_bake(self, name, extra=""):
        """Tell them there is no height map, and offer to make one."""
        self._trace("ask_to_bake", "for=" + repr(name))

        # ONLY about the map they are still on.
        #
        # This is queued with after_idle so it cannot open inside the click
        # handler, which means an unknown amount of time passes first - and
        # load_named calls update_idletasks() to paint its "loading" line,
        # which FLUSHES that queue from the middle of the next load. Traced
        # live: picked 29_el_hallouf (no bake), picked 34_redshire before the
        # dialog appeared, and the dialog then asked about 29_el_hallouf
        # while 34_redshire was on screen. Answer Yes to that and you bake a
        # map you are not looking at.
        if name != self.selected_name:
            self._trace("ask_to_bake SKIPPED", "stale=" + repr(name))
            return
        lines = ["%s has no height map, so it cannot be planned yet." % name, ""]
        if extra:
            lines += [extra, ""]
        lines += [
            "Make a terrain-only one now?",
            "",
            "It takes a few seconds and reads the map's own pkg. It has no "
            "models and no trees in it - opening the map once in nuTerra "
            "writes the full one.",
        ]
        if messagebox.askyesno("No height map", chr(10).join(lines)):
            self.bake_selected()


    def load_named(self, name):
        self._trace("load_named", "want=" + repr(name))
        if self.busy or not name:
            return
        self.selected_name = name

        # NOTHING here touches the map list or the dropdown.
        #
        # The list is a SIGNAL SOURCE. A click on it starts everything
        # downstream - the bake, the mask, the route, the lights - and none of
        # that is ever allowed back up to change the selection that caused it.
        # Every version of this bug came from breaking that: a selection set
        # from code fires <<ListboxSelect>> exactly as a click does, so writing
        # back to the widget re-enters the very handler that called you, with
        # whatever the list looks like by then.
        self.bake_btn.state(["!disabled"])
        if name not in getattr(self, "baked", ()):
            # No height map. Show the map anyway and say what is missing.
            self.show_without_bake(name)
            return
        self.status.set("loading " + name)
        self.root.update_idletasks()
        try:
            self.bake = fp.Bake(FOLDER, name)
        except Exception as e:
            self.status.set("could not load: %s" % e)
            return
        # With a height map the canvas draws against the bake itself.
        self.view_grid = self.bake
        self.map_name = name
        self.start = self.route = None
        self.am_img = None          # a different map, a different picture

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
        self.close_editor(ask=False)
        self.lights = []
        self.selection = None
        self.moving = False
        self.add_light = False
        # A freshly loaded map is locked: what it has is what it has until
        # Edit path says otherwise.
        self.edit_path = False
        self.edit_btn.configure(text="Edit path")

        self.route, seed, self.lights = existing_plan(name)
        # THE OLD PATH, KEPT AT LOAD.
        #
        # The map's own saved path, straight off disk. It is the one thing
        # that can always be gone back to, so it is held from the moment the
        # map opens - not snapshotted later by whoever happens to remember.
        self.loaded_route = list(self.route) if self.route else []
        # Off disk: no idea what it was averaged with, so the preview shows.
        self.route_avg_n = None
        # Just read from the file, so by definition they match it.
        self.lights_dirty = False
        self.refresh_light_ui()
        self.route_saved = self.route is not None
        self.targets = []
        # Whatever was generated belonged to the previous map.
        self.pending = None

        if seed:
            self.start = seed["start"]
            self.targets = list(seed["targets"])
            # The file still records the departure heading, the ring's
            # radius, its waypoint count and its turn direction. None of them
            # exists in this app any more. Read and ignored rather than
            # dropped from the format, so a campath written by an older Path
            # Studio still loads.
            #
            # Setting them was a KeyError the moment their sliders went, and it
            # threw HERE, before render_mask, so selecting any map with a saved
            # seed drew nothing at all. Anything restoring UI state from a file
            # has to be checked against the UI that still exists.

        self.render_mask()
        self.update_enabled()
        if self.route_saved:
            self.status.set("%s loaded, showing the saved path (%d points). "
                            "Edit path unlocks it." % (name, len(self.route)))
        else:
            self.status.set("%s loaded. Edit path, then left-drag sets start and "
                            "heading, click adds a point, Backspace undoes one." % name)

    # -------------------------------------------------------------- drawing

    # ------------------------------------------------------- terrain bake

    def bake_selected(self):
        self._trace("bake_selected")
        """Terrain-only bake from the pkg, for the map the list points at.

        Runs in a thread - reading 196 chunk zips and rasterising takes a few
        seconds - and reports through the status line. A real nuTerra bake is
        never replaced without asking: it has the models and trees this cannot.
        """
        name = self.selected_name
        if not name or self.busy:
            return
        if name in self.baked and not tb.bake_is_python(FOLDER, name):
            if not messagebox.askyesno(
                    "Replace the real bake?",
                    "%s already has a bake written by nuTerra, with the models and "
                    "trees in it.\n\nReplace it with a TERRAIN-ONLY bake?" % name):
                return
        self.busy = True
        self.bake_btn.state(["disabled"])
        self.status.set("baking %s from the pkg..." % name)

        def work():
            try:
                tb.bake(name, log=lambda m: self.root.after(0, self.status.set, m))
                self.root.after(0, self._bake_done, name, None)
            except Exception as e:
                self.root.after(0, self._bake_done, name, str(e))
        threading.Thread(target=work, daemon=True).start()

    def _bake_done(self, name, err):
        self._trace("BAKE-DONE", "baked=" + repr(name))
        self.busy = False
        self.bake_btn.state(["!disabled"])
        if err:
            self.status.set("bake failed: %s" % err)
            return
        # DO NOT call find_maps() here.
        #
        # find_maps rebuilds the list - delete every row, insert them again -
        # and a bake runs on a THREAD, so that landed whenever it landed:
        # seconds after the click that started it, while the operator was
        # scrolling or about to click something else. The rows moved under the
        # cursor and the next click picked a different map. That is the whole
        # of "I pick monastery and it switches to el_hallouf".
        #
        # Nothing about the list needs rebuilding anyway. One map gained a
        # height map; the only thing that changed is its colour, and the row
        # is already there. Update the fact, repaint that one row if it happens
        # to be on screen, and leave the scroll and the selection alone.
        self.baked = set(self.baked) | {name}
        self.mark_row_baked(name)

        # Whatever was picked LAST wins. If they moved on while this baked,
        # load that one - the bake still happened and its row is already
        # marked, but the map on screen is the one they actually asked for.
        want = self.pending_pick or name
        self.pending_pick = None
        self.load_named(want)
        if want != name:
            self.status.set("%s baked; showing %s, which you picked while it ran"
                            % (name, want))
        else:
            self.status.set("%s: terrain-only bake written - no models or trees "
                            "in it. Open the map in nuTerra for the real one."
                            % name)

    # ---------------------------------------------------------- global_AM

    ''' RADAR OVERLAY '''

    def on_radar_toggle(self):
        """Turn the finished imaging on or off, and refuse it when there is none.

        Casting needs a PATH to cast along. Ticked with no path the overlay
        simply drew nothing, which looks exactly like a broken checkbox - so
        it now unticks itself and says why.
        """
        self.radar_fans = None
        self.radar_key = None
        if self.show_radar.get():
            if self.bake is None or not self.route:
                self.show_radar.set(False)
                self.status.set("no radar imaging yet - it is cast along a "
                                "path, and there is no path here to cast along")
                return
            self.status.set("radar imaging: casting the fan along %d points..."
                            % len(self.route))
        self.repaint()

    def ensure_smooth_buf(self):
        """Rolling average of the route, wrapping at the seam.

        REVOLVING, because the route is a closed loop: point 0's window has to
        reach back into the tail or the seam gets a kink exactly where the two
        ends meet, which is the one place a path is guaranteed to be looked at.

        Cached on the route and the window size, so dragging the slider is a
        few thousand adds rather than a few thousand adds per repaint.
        """
        r = self.route
        try:
            n = int(round(float(self.vars["smooth_n"].get())))
        except Exception:
            n = 4
        if not r or len(r) < 3 or n < 2:
            self.smooth_buf = None
            self.smooth_key = None
            return

        # NOTHING TO PREVIEW WHEN THE ROUTE ALREADY IS THE AVERAGE.
        #
        # Averaging happens inside the export now, so after a Generate the
        # pink line IS the averaged path. Averaging it again drew a SECOND
        # pass and called it the preview - measured 1.87 m away from what had
        # just been written, which is a picture that disagrees with the file
        # it claims to describe. The burnt orange is what averaging WOULD do;
        # once it has been done, there is nothing left to show.
        if self.route_avg_n is not None and n == self.route_avg_n:
            self.smooth_buf = None
            self.smooth_key = None
            self.smooth_refused = 0
            return
        agl = round(float(self.vars["agl"].get()), 3)
        so = round(float(self.vars["standoff"].get()), 3)
        key = (len(r), r[0], r[-1], n, agl, so)
        if self.smooth_buf is not None and self.smooth_key == key:
            return

        # THE SAME FUNCTION THE EXPORTER USES, with the same collision test.
        #
        # A picture that is not what gets written is worse than no picture:
        # the averaging refuses points that would clip, and if the drawn line
        # took the ideal average while the file took the refused one, the two
        # would disagree exactly at the corners that matter.
        self.smooth_buf, self.smooth_refused = smooth_path.rolling_average(
            r, n, clear=self.clear_fn(), closed=True)
        self.smooth_key = key

    def clear_fn(self):
        """A collision test against the current mask, cached.

        Building the world and a Radar is most of a second, and the averaging
        wants it on every slider nudge - so it is kept until a slider that
        changes what counts as an obstacle moves.
        """
        agl = round(float(self.vars["agl"].get()), 3)
        so = round(float(self.vars["standoff"].get()), 3)
        key = (self.map_name, agl, so)
        if getattr(self, "_clear_key", None) == key and self._clear_radar:
            return self._clear_cb
        saved = (nav.AGL, nav.MARGIN, nav.BLOCK_H, nav.BODY_R)
        try:
            nav.AGL = agl
            nav.MARGIN = min(0.5, nav.AGL * 0.5)
            nav.BLOCK_H = nav.AGL - nav.MARGIN
            nav.BODY_R = so
            raw, plan_m, _d, _p = nav.build_world(self.bake, None)
            rad = nav.Radar(self.bake, plan_m, raw, self.bake.mx)
        finally:
            nav.AGL, nav.MARGIN, nav.BLOCK_H, nav.BODY_R = saved

        def cb(x0, z0, x1, z1):
            d = math.hypot(x1 - x0, z1 - z0)
            if d < 1e-6:
                return True
            return rad.clear(x0, z0, (x1 - x0) / d, (z1 - z0) / d, d)

        self._clear_radar = rad
        self._clear_cb = cb
        self._clear_key = key
        return cb

    def ensure_radar_fans(self):
        """Cast the navigator's fan along the route and cache it.

        Rebuilt only when something it depends on moves: the route itself, or
        either slider that changes what counts as an obstacle. Everything is
        kept in WORLD coordinates so pan and zoom are free - the drawing side
        only transforms.
        """
        if not self.show_radar.get() or self.bake is None or not self.route:
            self.radar_fans = None
            return
        key = (len(self.route), self.route[0], self.route[-1],
               round(float(self.vars["agl"].get()), 3),
               round(float(self.vars["standoff"].get()), 3))
        if self.radar_fans is not None and self.radar_key == key:
            return

        # Drive the navigator's own globals from the sliders, exactly the way
        # generate does, so the picture is of THIS setting and not of whatever
        # the module happens to default to - then put them back. An overlay
        # that leaves the navigator reconfigured would change the next route
        # the operator generated without telling them.
        saved = (nav.AGL, nav.MARGIN, nav.BLOCK_H, nav.BODY_R)
        try:
            nav.AGL = float(self.vars["agl"].get())
            nav.MARGIN = min(0.5, nav.AGL * 0.5)
            nav.BLOCK_H = nav.AGL - nav.MARGIN
            nav.BODY_R = float(self.vars["standoff"].get())

            raw, plan_m, _dist, _pad = nav.build_world(self.bake, None)
            radar = nav.Radar(self.bake, plan_m, raw, self.bake.mx)

            pts = self.route
            n = len(pts)
            step = max(1, n // RADAR_SWEEPS)
            fans = []
            for i in range(0, n, step):
                x, z = pts[i]
                x1, z1 = pts[(i + step) % n]
                # The radar's bearing convention is its own: march takes a
                # direction as (cos a, sin a) in world (x, z), so a is
                # atan2(dz, dx). It is NOT the campath's heading, which is
                # atan2(dx, dz) and would put the fan 90 degrees off.
                if x1 == x and z1 == z:
                    continue
                heading = math.atan2(z1 - z, x1 - x)
                rays = []
                for a, r in radar.fan(x, z, heading):
                    rays.append((x + math.cos(a) * r, z + math.sin(a) * r,
                                 r < nav.RADAR_RANGE - 0.5))
                fans.append((x, z, rays))
            self.radar_fans = fans
            self.radar_key = key
        except Exception as exc:
            self.radar_fans = None
            self.status.set("radar scan failed: %s" % exc)
        finally:
            nav.AGL, nav.MARGIN, nav.BLOCK_H, nav.BODY_R = saved

    def on_am_toggle(self):
        if self.bake is not None:
            self.render_mask()

    def load_am(self):
        """The map's global_AM on the bake grid, cached per map. False if none."""
        if self.am_img is not None:
            return self.am_img is not False
        try:
            im = tb.load_global_am(self.map_name)
        except Exception as e:
            self.status.set("global_AM: %s" % e)
            im = None
        if im is None:
            self.am_img = False
            return False
        b = self.bake
        # Resized to the bake grid and flipped on X only.
        #
        # It used to flip BOTH axes. The vertical flip was picked by scoring
        # the four options on 19_monastery, and the note admitted monastery is
        # symmetric enough that the score could not see the horizontal one -
        # which is a fair warning that the score could not see the vertical one
        # either. Re-scored on asymmetric maps it is worthless: gradient ratios
        # land within 0.9-1.1 of each other on lakeville, himmelsdorf and
        # monastery and disagree about the winner, and correlating AM relief
        # against terrain relief comes out at 0.00-0.08, which is noise.
        #
        # The mapping settles it instead. global_AM reaches world through a
        # NEGATIVE affine on both axes - t_mixer.vert does Global_UV *= -1.0,
        # and ChunkFunctions.vb says so in as many words. But the bake grid
        # already runs row 0 at wz_max with rows going z-DECREASING, while a
        # PIL image runs row 0 at v = 0. That row convention is itself a
        # vertical flip, so applying the affine's on top of it made two, and
        # two cancel. X has no such double count, so it keeps its flip.
        self.am_img = np.asarray(im.resize((b.w, b.h), Image.LANCZOS))[:, ::-1, :].copy()
        return True

    def render_mask(self):
        """The collision mask, shaded the same way the navigator's picture is."""
        b = self.bake
        o = b.obstacle
        img = np.zeros((b.h, b.w, 3), dtype=np.uint8)
        g = b.floor
        # Percentile stretch, and a wider tonal range than before. The absolute
        # min and max belong to the border chunks, so the playable ground sat in
        # the darkest sixth of the range and a terrain-only bake read as black.
        lo, hi = np.percentile(g, (2.0, 98.0))
        gn = np.clip((g - lo) / max(1e-6, hi - lo), 0.0, 1.0)
        img[..., 0] = (14 + 70 * gn).astype(np.uint8)
        img[..., 1] = (18 + 76 * gn).astype(np.uint8)
        img[..., 2] = (26 + 84 * gn).astype(np.uint8)

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
        # The global_AM under the OPEN ground. Obstacles and the low band keep
        # their mask colours, so what the planner sees stays legible on top of
        # what the map looks like.
        # A straight mix over EVERY cell. The slider reads LEFT = the map
        # photo, RIGHT = the mask the navigator actually sees - so pushing it
        # right brings up the thing being debugged rather than hiding it,
        # which is the way round it is reached for. The stored value is still
        # "amount of mask", so it is inverted once, here, into the AM's share.
        if self.show_am.get() and self.load_am():
            k = 1.0 - float(self.am_blend.get())
            img = (self.am_img.astype(np.float32) * k
                   + img.astype(np.float32) * (1.0 - k)).astype(np.uint8)

        self.mask_full = Image.fromarray(img[:, ::-1], "RGB")
        self.repaint()

    def repaint(self):
        if self.mask_full is None:
            return
        # Resize FROM a box rather than cropping first: the box takes
        # floats, so the visible window does not have to snap to whole texels
        # and the transforms above stay exact at every zoom.
        crop = self.crop_side()
        # LANCZOS is worth it for a still picture and not for a frame that
        # will be replaced in 60 ms - it is most of the repaint, and repaint
        # time is time the navigator is not running.
        im = self.mask_full.resize(
            (self.view, self.view),
            Image.BILINEAR if self.live is not None else Image.LANCZOS,
            box=(self.cx, self.cy, self.cx + crop, self.cy + crop))
        d = ImageDraw.Draw(im)

        # Under everything else: it is context, not the answer. RGBA on an RGB
        # image so hundreds of overlapping rays add up into a wash instead of
        # painting the map out.
        # The live trace, under everything else. Rays first so the path
        # reads on top of them.
        if self.live is not None:
            dl = ImageDraw.Draw(im, "RGBA")
            # The LAST FEW SWEEPS, not the whole run.
            #
            # ~21 rays a step, so this is about a dozen steps of history: long
            # enough to read as a scan sweeping ahead, short enough that it
            # moves. The full run is 5000+ rays and drawing them all at the
            # brightness needed to see one leaves a solid wash that never
            # changes - which looked exactly like nothing happening. What has
            # been decided is kept: that is the path line, below.
            tries = self.live.tries
            for t in tries[-260:]:
                col = tang.COLOUR_LIVE.get((t.layer, t.verdict))
                if not col:
                    continue
                a0 = self.to_view(t.x, t.z)
                a1 = self.to_view(t.x + math.cos(t.a) * t.r,
                                  t.z + math.sin(t.a) * t.r)
                dl.line([a0, a1], fill=col, width=1)

        self.ensure_radar_fans()
        if self.show_radar.get() and self.radar_fans:
            # Fades with the same slider as the mask. All the way to the map
            # photo and the rays are gone with it; all the way to the mask and
            # they are at full strength. One control for "how much of what the
            # navigator sees am I looking at", rather than a second one that
            # has to be found and reasoned about separately. With global_AM off
            # there is no blend to ride, so they are simply full.
            fade = float(self.am_blend.get()) if self.show_am.get() else 1.0
            # A THIRD of that while a trace is running. These fans are what
            # the SAVED route saw - thousands of rays, standing still - and at
            # full strength they bury the couple of dozen live ones that are
            # the whole reason to be watching. The comparison is still there,
            # it is just no longer shouting over the thing it is a comparison
            # for. Untick Radar scan to be rid of it entirely.
            if self.live is not None:
                fade *= 0.34
            if fade > 0.02:
                dr = ImageDraw.Draw(im, "RGBA")
                a_hit = max(1, int(60 * fade))
                a_open = max(1, int(26 * fade))
                a_dot = max(1, int(200 * fade))
                for (x, z, rays) in self.radar_fans:
                    px, py = self.to_view(x, z)
                    for (ex, ez, hit) in rays:
                        vx, vy = self.to_view(ex, ez)
                        dr.line([px, py, vx, vy],
                                fill=(255, 90, 70, a_hit) if hit
                                else (90, 200, 255, a_open))
                for (x, z, rays) in self.radar_fans:
                    for (ex, ez, hit) in rays:
                        if hit:
                            vx, vy = self.to_view(ex, ez)
                            dr.point([vx, vy], fill=(255, 210, 120, a_dot))

        if self.route:
            pts = [self.to_view(x, z) for (x, z) in self.route]
            # Dimmed while a trace is running. The saved route and the one
            # being flown cover the same waypoints, so at full strength the
            # old path sits directly on the new one and the brighter of the
            # two is the one that is not being watched. It is still there -
            # it is the comparison - just no longer the loudest thing.
            # Dimmed while ANYTHING is building a new one - a trace, a
            # flight, or a card search. The pink line is the saved path, and
            # at full strength next to a path being computed it reads as the
            # live answer when it is the stale one.
            live = (self.live is not None or self.gen is not None
                    or self.cards is not None)
            d.line(pts + [pts[0]],
                   fill=(120, 30, 85) if live else (255, 46, 168),
                   width=2 if live else 3, joint="curve")

        # THE LIVE PATH LAST, over the saved one.
        #
        # The rays above are context and belong under everything. This is not
        # context - it is the answer arriving, and it has to be legible
        # against the magenta route already on screen, which it very largely
        # overlaps because both are flying the same waypoints. Drawn earlier
        # it was simply painted out by the old path, so the one thing being
        # watched was the one thing invisible. Dark casing under a bright
        # core, so it reads over the magenta and over the map alike.
        if self.live is not None:
            pth = self.live.path
            if len(pth) > 1:
                vp = [self.to_view(px, pz) for px, pz in pth]
                # Casing for the whole line first, then each segment in the
                # colour of the layer that chose it - white where it flew
                # straight at the target, green where it settled for the
                # acceptance ring, amber round a blocker, violet where it fell
                # through to the grid search. The shape says where it went;
                # the colour says what it was doing.
                d.line(vp, fill=(20, 12, 6), width=8, joint="curve")
                mv = getattr(self.live, "moves", [])
                for i in range(len(vp) - 1):
                    lay = mv[i] if i < len(mv) else None
                    d.line([vp[i], vp[i + 1]],
                           fill=tang.PATH_COLOUR.get(lay, (255, 150, 40)),
                           width=4)
                # Where it is NOW - the head of the trace, which is the thing
                # the eye follows.
                hx, hy = vp[-1]
                d.ellipse([hx - 6, hy - 6, hx + 6, hy + 6],
                          fill=(255, 255, 255), outline=(255, 150, 40), width=2)

        # THE CARD SEARCH, AS IT HAPPENS.
        #
        # Rings grow, tangents get tried, anchors land and cards get filed -
        # and every one of those is a decision the numbers cannot show. Six
        # of twelve boxes solved is a fact; WHICH ring was too big and which
        # tangent was blocked is a picture.
        #
        # A short tail only. The whole route is thousands of events and drawn
        # all at once they are a solid wash that never changes, which is what
        # "I cannot see it happening" looked like the last three times.
        if self.cards is not None and self.cards_shown:
            dc = ImageDraw.Draw(im, "RGBA")

            # EVERY CARD, IN SHADES OF GREEN.
            #
            # Under the moving scan, because they accumulate - by the end
            # there are dozens and they are the context the scan is happening
            # in. Bright green passed the test, dim green was thrown out, and
            # the shade walks per card so two cards along the same line read
            # as two lines rather than one slightly thicker one.
            for ci, (cpth, creached, _why) in enumerate(self.card_paths):
                if not cpth or len(cpth) < 2:
                    continue
                t = ((ci * 0.37) % 1.0)
                if creached:
                    # GREEN, not mint. The blue channel was up at 160-220,
                    # which reads as cyan against a blue-black mask and is
                    # not what "shades of green" means. Opaque, too - these
                    # are results, not atmosphere.
                    col = (110 + int(55 * t), 255,
                           70 + int(55 * t), 255)
                    wid = 3
                else:
                    col = (40 + int(35 * t), 155 + int(60 * t),
                           45 + int(35 * t), 235)
                    wid = 2
                dc.line([self.to_view(px_, pz_) for px_, pz_ in cpth],
                        fill=col, width=wid)

            evs = self.card_ev[-90:]
            for i, (kind, kw) in enumerate(evs):
                # Older events fade out, so the eye follows the newest.
                age = (i + 1) / float(len(evs))
                a = int(30 + 210 * age * age)

                if kind == "ring":
                    cx, cz, rr = kw["cx"], kw["cz"], kw["r"]
                    p0 = self.to_view(cx - rr, cz - rr)
                    p1 = self.to_view(cx + rr, cz + rr)
                    box = [min(p0[0], p1[0]), min(p0[1], p1[1]),
                           max(p0[0], p1[0]), max(p0[1], p1[1])]
                    if box[2] - box[0] > 2:
                        dc.ellipse(box, outline=(255, 190, 80, a),
                                   width=2 if kw["clear"] else 1)
                    for (tx_, tz_) in kw["clear"]:
                        dc.line([self.to_view(kw["x"], kw["z"]),
                                 self.to_view(tx_, tz_)],
                                fill=(255, 235, 140, a), width=1)

                elif kind == "ray":
                    a0 = self.to_view(kw["x"], kw["z"])
                    if kw["hit"] is None:
                        dc.line([a0, self.to_view(kw["tx"], kw["tz"])],
                                fill=(110, 240, 230, a), width=2)
                    else:
                        hx, hz = kw["hit"]
                        dc.line([a0, self.to_view(hx, hz)],
                                fill=(255, 90, 70, a), width=2)
                        vx, vy = self.to_view(hx, hz)
                        dc.ellipse([vx - 3, vy - 3, vx + 3, vy + 3],
                                   fill=(255, 120, 90, a))

                elif kind == "took":
                    cx, cz, rr = kw["cx"], kw["cz"], kw["r"]
                    p0 = self.to_view(cx - rr, cz - rr)
                    p1 = self.to_view(cx + rr, cz + rr)
                    dc.ellipse([min(p0[0], p1[0]), min(p0[1], p1[1]),
                                max(p0[0], p1[0]), max(p0[1], p1[1])],
                               outline=(120, 255, 170, 255), width=3)

                elif kind == "anchor":
                    vx, vy = self.to_view(kw["px"], kw["pz"])
                    dc.line([self.to_view(kw["x"], kw["z"]), (vx, vy)],
                            fill=(255, 255, 255, a), width=1)
                    dc.ellipse([vx - 4, vy - 4, vx + 4, vy + 4],
                               fill=(255, 255, 255, a))

                # "card" is not drawn here - every card is drawn above,
                # from card_paths, and drawing it twice made the newest one
                # look like a different colour from the rest.

            # What has actually been committed, over the top of the search.
            # Each connected stretch on its own. The breaks BETWEEN them are
            # the legs nothing could fly, and they are supposed to look like
            # holes.
            for seg in (self.cards.get("runs")
                        or ([self.cards.get("path")] if self.cards.get("path")
                            else [])):
                if not seg or len(seg) < 2:
                    continue
                vp = [self.to_view(px_, pz_) for px_, pz_ in seg]
                # A HAIRLINE, and no casing.
                #
                # The winning cards ARE this chain, drawn bright green just
                # above - and a 6 px dark casing with an orange core on top of
                # them buried every card that passed first time, which is
                # exactly the complaint. White reads on green, one pixel is
                # enough to say "this is the chain that was taken", and the
                # green underneath stays the thing you see.
                d.line(vp, fill=(255, 255, 255, 220), width=1, joint="curve")

        # THE FLIGHT UNDER GENERATE.
        #
        # This is the navigator that actually produces the saved path -
        # radar_commit - and it DOES sweep: a whole fan of bearings every
        # step, which is what "the radar scan" has always meant. Until now
        # none of it was drawn, because the live view was wired only to the
        # tangent probe on a different button. Watching one navigator while
        # saving the output of another was the whole confusion.
        if self.gen is not None:
            gpath, gfans = self.gen[0], self.gen[1]
            gevents = self.gen[3] if len(self.gen) > 3 else ()
            dg = ImageDraw.Draw(im, "RGBA")

            def sweep(entry, alpha_line, alpha_dot, wide):
                """One radar sweep: the bearings, where they stopped, and the
                samples taken PAST each stop.

                The same three things radar_commit's own picture draws, in the
                same colours - amber where a bearing came back short, teal
                where it ran clear, and the post-hit probes in red when what
                they found is solid. Reading the live view and the saved
                picture as one thing only works if they agree.
                """
                fx, fz, _hdg, fan, _mode, probes = entry
                ox, oy = self.to_view(fx, fz)
                for (a_, rng_) in fan:
                    hit = rng_ < nav.RADAR_RANGE - 0.5
                    tip = self.to_view(fx + math.cos(a_) * rng_,
                                       fz + math.sin(a_) * rng_)
                    dg.line([ox, oy, tip[0], tip[1]],
                            fill=(255, 205, 105, alpha_line) if hit
                            else (110, 240, 230, max(20, alpha_line // 2)),
                            width=wide)
                    if hit and alpha_dot:
                        dg.ellipse([tip[0] - 2, tip[1] - 2,
                                    tip[0] + 2, tip[1] + 2],
                                   fill=(255, 225, 150, alpha_dot))
                # The three samples taken BEYOND each hit - the test for
                # whether a return is a wall or a hedge. Red when solid.
                for (_rng, spts, solid) in probes or ():
                    solid_hit = solid >= nav.SOLID_H
                    for (sx_, sz_) in spts:
                        vx, vy = self.to_view(sx_, sz_)
                        r_ = 2.5 if solid_hit else 1.8
                        dg.ellipse([vx - r_, vy - r_, vx + r_, vy + r_],
                                   fill=(255, 45, 45, alpha_dot or 150)
                                   if solid_hit
                                   else (255, 165, 105, (alpha_dot or 150) // 2))

            for entry in list(gfans)[-7:]:
                sweep(entry, 70, 0, 1)
            if gfans:
                sweep(gfans[-1], 235, 230, 1)

            # WHAT IT DECIDED, WHERE IT DECIDED IT.
            #
            # radar_commit marks its own turning points and these are the same
            # marks its picture uses: a ring where it entered a trap, a cross
            # where it was boxed in, a yellow ring where the guard fired, a
            # dot where it reversed, a dash where it backed up. Live, they are
            # the difference between watching a line move and watching a
            # navigator think.
            for ev in list(gevents)[-60:]:
                ex_, ez_ = ev[0], ev[1]
                kind = ev[2]
                px, py = self.to_view(ex_, ez_)
                if kind == "enter":
                    dg.ellipse([px - 6, py - 6, px + 6, py + 6],
                               outline=(255, 175, 55, 255), width=2)
                elif kind == "boxed":
                    dg.line([px - 8, py - 8, px + 8, py + 8],
                            fill=(255, 70, 70, 255), width=2)
                    dg.line([px - 8, py + 8, px + 8, py - 8],
                            fill=(255, 70, 70, 255), width=2)
                elif kind == "guard":
                    dg.ellipse([px - 11, py - 11, px + 11, py + 11],
                               outline=(255, 245, 90, 255), width=2)
                elif kind == "reverse":
                    dg.ellipse([px - 4, py - 4, px + 4, py + 4],
                               fill=(255, 90, 150, 255))
                elif kind == "backup":
                    dg.line([px - 5, py, px + 5, py],
                            fill=(255, 200, 60, 255), width=2)
            if len(gpath) > 1:
                vp = [self.to_view(px, pz) for px, pz in list(gpath)]
                d.line(vp, fill=(20, 12, 6), width=7, joint="curve")
                d.line(vp, fill=(255, 170, 60), width=3, joint="curve")
                hx, hy = vp[-1]
                d.ellipse([hx - 6, hy - 6, hx + 6, hy + 6],
                          fill=(255, 255, 255), outline=(255, 170, 60), width=2)

        if self.live is not None and self.live.path:
            # THE RAYS OF THE CURRENT STEP, OVER THE TOP.
            #
            # Measured on 19_monastery: 195 of 253 steps cast exactly two
            # rays - one probe straight at the waypoint and the move it then
            # committed to. So three quarters of the run has ONE drawn ray,
            # pointing the same way as the path, one pixel wide, underneath a
            # four pixel path line drawn on top of it. There was nothing to
            # see because the only moving thing was the only hidden thing.
            #
            # A step is everything since the last committed move, so this is
            # exactly "what it is looking at, at this moment": one long line
            # to the waypoint when the way is clear, and the whole ring or
            # tangent fan when it is not.
            step = []
            for t in reversed(self.live.tries[:-1] if self.live.tries else []):
                if t.verdict == "TAKEN":
                    break
                step.append(t)
            if not step and self.live.tries:
                step = [self.live.tries[-1]]
            dn = ImageDraw.Draw(im, "RGBA")
            for t in step[:400]:
                if t.verdict == "TAKEN":
                    continue
                col = tang.COLOUR_LIVE.get((t.layer, t.verdict))
                if not col:
                    continue
                a0 = self.to_view(t.x, t.z)
                a1 = self.to_view(t.x + math.cos(t.a) * t.r,
                                  t.z + math.sin(t.a) * t.r)
                # Full alpha and two pixels: this is the live one, not the
                # wash of where it has already been.
                dn.line([a0, a1], fill=col[:3] + (255,), width=2)
            for (rx, rz, _wi, how) in self.live.reached:
                vx, vy = self.to_view(rx, rz)
                c = (110, 255, 140) if how != "STUCK" else (255, 70, 70)
                d.line([vx - 8, vy - 8, vx + 8, vy + 8], fill=(20, 12, 6), width=5)
                d.line([vx - 8, vy + 8, vx + 8, vy - 8], fill=(20, 12, 6), width=5)
                d.line([vx - 8, vy - 8, vx + 8, vy + 8], fill=c, width=3)
                d.line([vx - 8, vy + 8, vx + 8, vy - 8], fill=c, width=3)

        # THE AVERAGED PATH, BURNT ORANGE, over the route it came from.
        #
        # Drawn after the pink so the comparison reads the right way round:
        # the flown path underneath, what the averaging would make of it on
        # top. Nothing else uses this buffer - it is not exported, not saved,
        # and the campath is untouched by it.
        self.ensure_smooth_buf()
        if self.smooth_buf and len(self.smooth_buf) > 2:
            vb = [self.to_view(px_, pz_) for px_, pz_ in self.smooth_buf]
            d.line(vb + [vb[0]], fill=(30, 14, 4), width=6, joint="curve")
            d.line(vb + [vb[0]], fill=(204, 85, 0), width=3, joint="curve")

        if self.targets:
            tv = [self.to_view(tx, tz) for (tx, tz) in self.targets]
            seq = ([self.to_view(*self.start)] if self.start else []) + tv
            links = self.show_links.get()
            # Dash ALONG each link, not by dropping alternate links. The first
            # version skipped every other segment, which reads as the line
            # missing a target rather than as a dashed line.
            # The POINTS always draw; only the links between them go away.
            # Hiding the points too would leave nothing to click on.
            if links:
                for i in range(len(seq) - 1):
                    dashed(d, seq[i], seq[i + 1], (90, 200, 230))
                if self.start and len(tv) > 1:
                    # and back to the start, where the route actually ends
                    dashed(d, seq[-1], seq[0], (70, 150, 180))
            for i, (tx, ty) in enumerate(tv):
                d.ellipse([tx - 6, ty - 6, tx + 6, ty + 6],
                          fill=(70, 210, 245), outline=(255, 255, 255))
                d.text((tx + 9, ty - 6), str(i + 1), fill=(190, 240, 255))

        if self.start:
            px, py = self.to_view(*self.start)
            d.ellipse([px - 7, py - 7, px + 7, py + 7],
                      fill=(80, 255, 130), outline=(255, 255, 255))

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
            # An aimed light with any horizontal aim shows it as a tick out
            # to half its range; straight down has nothing to show on a map.
            if int(lt.get("kind", 0)) != KIND_POINT:
                ax, _ay, az = lt.get("aim", (0.0, -1.0, 0.0))
                hl = math.hypot(ax, az)
                if hl > 0.05 and rr > 1.5:
                    tx, ty = self.to_view(lt["x"] + ax / hl * rm * 0.5,
                                          lt["z"] + az / hl * rm * 0.5)
                    d.line([(lx, ly), (tx, ty)], fill=rgb, width=2)

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

        # THE KEY, only while a trace is running.
        #
        # Four path colours and two ray colours is more than anyone should
        # have to hold in their head, and a legend in the docs is a legend
        # nobody has open. It costs six short lines in a corner and it goes
        # away with the trace.
        if self.live is not None:
            keys = (
                    ("straight at it", tang.PATH_COLOUR["direct"], 4),
                    ("to the ring", tang.PATH_COLOUR["ring"], 4),
                    ("round a blocker", tang.PATH_COLOUR["tangent"], 4),
                    ("grid search", tang.PATH_COLOUR["search"], 4),
                    ("ray - clear", (90, 200, 255), 2),
                    ("ray - blocked", (255, 90, 70), 2))
            # On a panel. Over a map that is half daylight-yellow, white text
            # on nothing is unreadable exactly where the interesting things
            # happen.
            dk = ImageDraw.Draw(im, "RGBA")
            dk.rectangle([6, 6, 150, 12 + 16 * len(keys)],
                         fill=(8, 10, 16, 205), outline=(70, 84, 104, 255))
            ky = 12
            for label, col, w in keys:
                dk.line([14, ky + 5, 38, ky + 5], fill=col, width=w)
                dk.text((46, ky), label, fill=(228, 236, 246, 255))
                ky += 16

        self.photo = ImageTk.PhotoImage(im)
        self.canvas.delete("all")
        self.canvas.create_image(self.ox, self.oy, anchor="nw", image=self.photo)

    # ------------------------------------------------------------ transforms

    def crop_side(self):
        """Side of the visible window, in texels."""
        return float(self.view_grid.w) / self.zoom

    def mirror_col(self, c):
        """Bake column <-> display column. Its own inverse."""
        return (self.view_grid.w - 1) - c

    def to_view(self, wx, wz):
        """World -> pixels INSIDE the map square (what gets drawn into)."""
        c, r = self.view_grid.texel_of(wx, wz)
        c = self.mirror_col(c)
        crop = self.crop_side()
        s = self.view / crop
        return ((c - self.cx) * s, (r - self.cy) * s)

    def to_world(self, px, py):
        """Canvas pixels -> world. Takes the letterbox offset off first."""
        crop = self.crop_side()
        s = crop / self.view
        c = self.mirror_col((px - self.ox) * s + self.cx)
        return self.view_grid.world_of(c, (py - self.oy) * s + self.cy)

    def clamp_window(self):
        """Keep the visible window inside the bake.

        Without this, zooming out at the edge walks the window off the map and
        leaves a band of whatever PIL pads with, which reads as terrain that
        is not there.
        """
        crop = self.crop_side()
        hi = max(0.0, float(self.view_grid.w) - crop)
        self.cx = min(max(self.cx, 0.0), hi)
        self.cy = min(max(self.cy, 0.0), hi)

    def on_pan_press(self, e):
        """Anchor the pan: where the mouse was, and where the window was."""
        if self.view_grid is None:
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
        if self.view_grid is None or self.pan_from is None:
            return
        ax, ay, acx, acy = self.pan_from

        # Pixels to texels. Negative because the map follows the cursor: drag
        # right and the window has to move LEFT to bring the map with it.
        s = self.crop_side() / self.view
        self.cx = acx - (e.x - ax) * s
        self.cy = acy - (e.y - ay) * s
        self.clamp_window()
        self.repaint()

    def frame_on(self, pts, margin=1.35):
        """Zoom and centre on a set of world points.

        The whole map is 1400 m across and a radar ray is 90 m long, so at
        zoom 1 the entire scan is a 25 pixel smudge - the reason the live
        trace looked like nothing was happening even while it ran. Framing
        the waypoints puts a ray back at a readable length.

        View only. It moves no selection and decides nothing; the path is
        exactly the same path whether it is watched close up or not.
        """
        if self.view_grid is None or not pts:
            return
        cs, rs = [], []
        for (wx, wz) in pts:
            c, r = self.view_grid.texel_of(wx, wz)
            cs.append(self.mirror_col(c))
            rs.append(r)
        # A square window: the view is square, so fitting the long side fits
        # both, and a non-square box would only be letterboxed anyway.
        want = max(max(cs) - min(cs), max(rs) - min(rs)) * margin
        want = max(want, 40.0)              # never closer than ~28 m across
        self.zoom = min(max(self.view_grid.w / want, 1.0), MAX_ZOOM)
        crop = self.crop_side()
        self.cx = (min(cs) + max(cs)) * 0.5 - crop * 0.5
        self.cy = (min(rs) + max(rs)) * 0.5 - crop * 0.5
        self.clamp_window()

    # ----------------------------------------------------------- test lock

    def _every_widget(self):
        """This window and everything in it."""
        stack = [self.root]
        while stack:
            w = stack.pop()
            yield w
            try:
                stack.extend(w.winfo_children())
            except Exception:
                pass

    def set_test_lock(self, on, why=""):
        """Take the Studio away from the mouse and keyboard, or give it back.

        A test driving the app and a person using it at the same time wreck
        each other. A stray click lands in the middle of a measured sequence
        and the result reads as an app bug - that has already happened here
        more than once, and hours went into "fixing" a picker that was doing
        exactly what it was told.

        Locked by BINDTAGS. A widget whose tags are one tag nobody has bound
        to has no bindings at all - not its own, not its class's - which is
        the same mechanism that took the space key off the map list. It is
        total and it is exactly reversible: the old tags go back.

        after() callbacks are NOT bindings and keep running, so a trace that
        is already going carries on; only the human input stops.
        """
        if bool(on) == getattr(self, "_locked", False):
            return
        if on:
            self._saved_tags = {}
            for w in self._every_widget():
                try:
                    self._saved_tags[w] = w.bindtags()
                    w.bindtags(("PS_TEST_LOCK",))
                except Exception:
                    pass
            self._locked = True
            self.root.title("nuTerra Path Studio - LOCKED, a test has it")
            try:
                self.status_lbl.configure(foreground="#ff9a6a")
            except Exception:
                pass
            self.status.set("LOCKED - a test has the Studio, clicks and keys "
                            "do nothing." + ((" " + why) if why else ""))
        else:
            for w, tags in getattr(self, "_saved_tags", {}).items():
                try:
                    w.bindtags(tags)
                except Exception:
                    pass
            self._saved_tags = {}
            self._locked = False
            self.root.title("nuTerra Path Studio")
            try:
                self.status_lbl.configure(foreground="")
            except Exception:
                pass
            self.status.set("unlocked - the Studio is yours again")

    def on_wheel(self, e):
        """Zoom about the cursor: the texel under it does not move."""
        if self.view_grid is None:
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
        """Arm or disarm light placement.

        Disarming closes the open editor and drops its edits - after asking,
        if there were any. If the answer is to keep them, the mode stays on.
        """
        if self.add_light:
            if not self.close_editor(ask=True):
                return
            self.add_light = False
        else:
            self.add_light = True
            # Placing and selecting are different intentions; being in one
            # should not leave the other half-active.
            self.selection = None
        self.refresh_light_ui()
        self.status.set("click to place a light - Add Light or Esc to stop"
                        if self.add_light
                        else "%d light%s" % (len(self.lights),
                                             "" if len(self.lights) == 1 else "s"))
        self.update_enabled()
        self.repaint()

    def refresh_light_ui(self):
        self.light_btn.configure(
            text="Placing... (click to stop)" if self.add_light else "Add Light")
        # The cursor says what a click will do.
        self.canvas.configure(cursor=self.bulb_cursor if self.add_light else "")

    def selected_light(self):
        """The selected light dict, or None when the selection is not a light."""
        if self.selection and self.selection[0] == "light":
            return self.lights[self.selection[1]]
        return None

    # ---- the light editor window --------------------------------------

    def open_light_editor(self, i):
        """Open the editor on light i, replacing one already open on another
        light (asking about its edits first). False if the user kept it."""
        if self.editor is not None and self.editor.index == i:
            self.editor.top.lift()
            return True
        if not self.close_editor(ask=True):
            return False
        self.editor = LightEditor(self, i, self.lights[i],
                                  on_ok=self._editor_ok, on_close=self._editor_closed)
        return True

    def edit_selected_light(self):
        if self.selected_light() is not None:
            self.open_light_editor(self.selection[1])

    def close_editor(self, ask=True):
        """Close the editor if one is open. True when it is closed or was not
        open, False when the user chose to keep editing."""
        if self.editor is None:
            return True
        if ask and self.editor.changed:
            if not messagebox.askyesno(
                    "Discard the light edits?",
                    "The light editor has changes that were not applied.\n\n"
                    "Close it and lose them?",
                    icon="warning", default="no", parent=self.root):
                return False
        ed = self.editor
        self.editor = None
        ed.destroy()
        self.update_enabled()
        return True

    def _editor_ok(self, i, work):
        """OK in the editor: the working copy becomes the light, and its
        colour becomes the colour the next light is placed with."""
        self.editor = None
        if 0 <= i < len(self.lights):
            self.lights[i].update(work)
            self.light_color = work.get("color", self.light_color)
            self.lights_dirty = True
        self.update_enabled()
        self.repaint()

    def _editor_closed(self):
        """The editor closed itself - Cancel, or the window's X."""
        self.editor = None
        self.update_enabled()

    def apply_and_save_light(self, i, work):
        """Save in the editor: the working copy becomes the light and the
        lights go to the .campath now, through the same save the panel
        button runs. The editor stays open."""
        if 0 <= i < len(self.lights):
            self.lights[i].update(work)
            self.light_color = work.get("color", self.light_color)
            self.lights_dirty = True
        self.update_enabled()
        self.repaint()
        self.save_path()

    def open_curve_editor(self):
        """The shared falloff curves, in their own window.

        Saved beside the .campath files, because that is the folder both sides
        already agree on: nuTerra resolves it the way it finds the route, and
        its Reload Cam Path re-reads the curves along with the lamps.
        """
        fc.CurveEditor(self.root, cp.campath_dir(),
                       on_saved=lambda p: self.status.set(
                           "saved %s - Reload Cam Path in nuTerra" % os.path.basename(p)))

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
        # Path points are only there to pick while the path is unlocked.
        if not self.edit_path:
            return None
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
        """Cancel a flight if one is in the air; otherwise drop the selection
        and leave placement mode - closing the light editor too, after asking
        if it has edits."""
        if self.cards_running and not self.card_stop:
            self.card_stop = True
            self.status.set("stopping the search...")
            return "break"
        if self.busy and self.gen_was is not None and not self.gen_cancel:
            # Only a flag. The navigator is on another thread and stops itself
            # at its next step, which keeps the unwind inside the code that
            # knows what it was doing.
            self.gen_cancel = True
            self.status.set("cancelling...")
            return "break"
        if not self.close_editor(ask=True):
            return
        self.add_light = False
        self.selection = None
        self.moving = False
        self.refresh_light_ui()
        self.update_enabled()
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
                if self.selection[0] == "light":
                    # The editor comes to the light rather than the other way
                    # round, so editing it does not first overwrite it.
                    self.open_light_editor(self.selection[1])
                self.status.set("%s selected - drag to move, Esc to drop"
                                % self.selection[0])
            else:
                self.status.set("nothing under the cursor")
            self.update_enabled()
            self.repaint()
            return

        if self.add_light:
            # The editor for the light placed before this one may still be
            # open with edits in it; it has to go before the next one opens.
            if not self.close_editor(ask=True):
                return
            wx, wz = self.to_world(e.x, e.y)
            self.lights.append(new_light(wx, wz, color=self.light_color))
            self.lights_dirty = True
            self.selection = ("light", len(self.lights) - 1)
            self.open_light_editor(len(self.lights) - 1)
            self.update_enabled()
            self.status.set("%d light%s - editor open; Add Light or Esc to stop placing"
                            % (len(self.lights),
                               "" if len(self.lights) == 1 else "s"))
            self.repaint()
            return

        # With a start already placed, a left click ADDS A POINT - it does not
        # begin a new course.
        #
        # It used to always reset: one stray click after ten minutes of placing
        # points threw the lot away, silently, with no undo beyond Backspace
        # one at a time. Starting over is rare and Clear says so explicitly;
        # adding another point is the common act, and the common act is what
        # the plain click should do. Right click still adds one too, so old
        # habits keep working.
        # Everything from here down changes the path, and the path is locked
        # until Edit path says otherwise.
        if not self.edit_path:
            self.status.set("the path is locked - press Edit path to change it")
            return

        if self.start is not None:
            self.add_point(e)
            return

        # A CLICK. That is the whole gesture.
        self.start = self.to_world(e.x, e.y)
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
        # Dragging only ever moves a selection now. Nothing else owns the
        # gesture, so nothing has to be arbitrated.
        if self.moving:
            self.move_selection(e)
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
        # Nothing else to finish. A release used to set the heading; the
        # click already did everything there is to do.

    def add_point(self, e):
        """Append a point the route must visit, in click order.

        Both buttons land here once a start exists. Kept separate from on_press
        so the left-click path and the right-click path cannot drift apart.
        """
        self.targets.append(self.to_world(e.x, e.y))
        self.route = None
        self.status.set("%d point%s - click to add, Backspace to undo, "
                        "Clear to start over"
                        % (len(self.targets), "" if len(self.targets) == 1 else "s"))
        self.update_enabled()
        self.repaint()

    def on_target(self, e):
        """Right click adds a point the route must visit.

        Left click does the same once a start is placed - see on_press. Right
        click also works BEFORE there is a start, which left click cannot: the
        first left click has to set the start and drag its heading.
        """
        if self.bake is None or self.busy:
            return
        if not self.edit_path:
            self.status.set("the path is locked - press Edit path to change it")
            return
        self.add_point(e)

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
                 "y": lt.get("height", 3.0),
                 "curve": int(lt.get("curve", 0)),
                 "kind": int(lt.get("kind", 0)),
                 "aim": tuple(lt.get("aim", (0.0, -1.0, 0.0))),
                 "cone": float(lt.get("cone", 0.0)),
                 "blend": float(lt.get("blend", 0.0)),
                 "ang0": float(lt.get("ang0", 0.0)),
                 "ang1": float(lt.get("ang1", 0.0)),
                 "vol_mix": float(lt.get("vol_mix", 1.0))}
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

        # Nothing is greyed out here any more - Path smoothing applies to
        # every route there is.
        self.ring_lbl.configure(text="")

        # The path lock. Everything that can change the path follows
        # edit_path; the map has to be loaded and nothing running.
        loaded = self.bake is not None and not self.busy
        unlocked = loaded and self.edit_path
        self.edit_btn.state(["!disabled" if loaded else "disabled"])
        # Not behind the path lock: tracing changes nothing, it only watches.
        self.live_btn.state(["!disabled" if loaded else "disabled"])
        self.cards_btn.state(["!disabled" if loaded else "disabled"])
        for k in ("smooth", "agl", "standoff"):
            self.vars[k + "_w"].state(["!disabled" if unlocked else "disabled"])
        self.clear_btn.state(["!disabled" if unlocked else "disabled"])
        self.go.state(["!disabled" if unlocked else "disabled"])
        self.edit_light_btn.state(
            ["!disabled" if (self.selected_light() is not None and not self.busy)
             else "disabled"])

    def on_undo_target(self, _e=None):
        """Backspace or Delete drops the most recently placed thing.

        WHICH thing depends on the mode. While placing lights it is the last
        light - the key that undoes a placement has to undo the placement you
        are actually making, or it quietly eats a waypoint out of the route
        while you are looking at the lights.
        """
        if self.busy:
            return

        # A SELECTED light goes first, whatever mode we are in - it is the
        # thing on screen with a ring round it and, usually, its editor open.
        # With none selected, Add Light mode drops the last one placed. Neither
        # is behind the path lock: lights are not part of the path.
        li = None
        if self.selection and self.selection[0] == "light":
            li = self.selection[1]
        elif self.add_light and self.lights:
            li = len(self.lights) - 1
        if li is not None and 0 <= li < len(self.lights):
            # Its editor has nothing left to edit; an editor on a light above
            # it in the list keeps its light, one index down.
            if self.editor is not None:
                if self.editor.index == li:
                    self.close_editor(ask=False)
                elif self.editor.index > li:
                    self.editor.index -= 1
            self.lights.pop(li)
            self.selection = None
            self.moving = False
            self.lights_dirty = True
            n = len(self.lights)
            self.status.set("removed the light - %d left" % n if n
                            else "removed the light - none left")
            self.update_enabled()
            self.repaint()
            return
        if self.add_light:
            self.status.set("no lights to remove")
            return

        if not self.edit_path:
            self.status.set("the path is locked - press Edit path to change it")
            return

        if not self.targets:
            # Backed all the way out: the one thing left to undo is the
            # start itself. Backspace used to stop here and leave it, so the
            # only way to lose a start was the Clear button.
            if self.start is None:
                return
            self.start = None
            self.route = None
            if self.selection and self.selection[0] == "start":
                self.selection = None
                self.moving = False
            self.status.set("removed the start - click to place one again")
            self.update_enabled()
            self.repaint()
            return
        self.targets.pop()
        self.route = None
        # A selected target that was the one just popped now indexes past the
        # end of the list; drop the selection rather than let repaint reach it.
        if (self.selection and self.selection[0] == "target"
                and self.selection[1] >= len(self.targets)):
            self.selection = None
            self.moving = False
        n = len(self.targets)
        self.status.set("removed the last target - %d left"
                        % n if n else "removed the last target - none left")
        self.update_enabled()
        self.repaint()

    def toggle_edit_path(self):
        """The lock on the path. Off, every path control and every map click
        that would place or move the start or a point is greyed or ignored;
        the lights are not part of it."""
        if self.busy or self.bake is None:
            return
        self.edit_path = not self.edit_path
        self.edit_btn.configure(
            text="Editing path (click to lock)" if self.edit_path else "Edit path")
        if not self.edit_path and self.selection and self.selection[0] in ("target", "start"):
            self.selection = None
            self.moving = False
        self.status.set("path unlocked - drag a start, click points, Generate"
                        if self.edit_path else "path locked")
        self.update_enabled()
        self.repaint()

    def on_space(self, _e=None):
        """Space pauses and resumes the live trace.

        Ignored while a text box has focus, or a space could never be typed
        into the map search.
        """
        w = self.root.focus_get()
        # A ttk.Combobox IS a ttk.Entry, and this one is readonly - nothing can
        # be typed into it, so it is not a text box for this purpose and space
        # should still reach the trace. Without the second test the pause key
        # was dead for as long as the dropdown held focus, which is from the
        # moment a map is picked with it.
        if (isinstance(w, (tk.Entry, ttk.Entry, tk.Text))
                and not isinstance(w, ttk.Combobox)):
            return
        # "break" on EVERY path out. This is bound as a class binding as well
        # as on the toplevel, and without it the toplevel copy runs second and
        # toggles the pause straight back off - one press, no effect.
        if self.live is not None:
            self.live_paused = not self.live_paused
            self.status.set("trace PAUSED - space to resume" if self.live_paused
                            else "tracing...")
        return "break"


    def _live_step(self, _nav):
        """Called by the navigator after every move, on its own thread.

        Sleeping here is what makes the scan watchable - the repaint timer
        samples whatever has been appended, so without a wait the whole route
        appears between two frames. It touches no Tk objects, only two plain
        flags, which is why it is safe off the UI thread.
        """
        while self.live_paused and not self.live_stop:
            time.sleep(0.05)
        if self.live_stop:
            raise tang.Stopped()
        # NO TK FROM THIS THREAD.
        #
        # This read self.vars["trace_ms"].get(). Tcl is single-threaded, so a
        # call from anywhere but the UI thread is queued and does not return
        # until the main loop gets round to servicing it - and the main loop
        # is busy repainting at 40-70 ms a frame. Each step therefore waited
        # on the repaint that was supposed to be showing it: 25 ms of intended
        # delay came out as 1.1 SECONDS a step, which is why the scan looked
        # frozen rather than slow. The navigator itself does all 242 steps in
        # under a second. The slider is sampled on the UI thread in tick() and
        # left here as a float.
        ms = self.live_ms
        if ms > 0:
            time.sleep(ms / 1000.0)

    def cards_button(self):
        """Run a search, stop one, or hide the last one's cards.

        Three states, one button, and the label always says which:
            nothing drawn      -> "Search cards (live)"  runs it
            running            -> "Stop search"          stops it
            cards on the map   -> "Hide cards"           hides them
        Hidden, it goes back to offering a fresh search - a search takes well
        under a second, so re-running is cheaper than a fourth state.
        """
        if self.cards_running:
            self.card_stop = True
            return
        if self.cards is not None and self.cards_shown and self.card_paths:
            self.cards_shown = False
            self.cards_btn.configure(text="Search cards (live)")
            self.status.set("cards hidden - %d of them" % len(self.card_paths))
            self.repaint()
            return
        self.search_cards()

    def search_cards(self):
        """Run the box-of-boxes search, drawing every step it takes.

        The numbers say six of twelve boxes solved; only the picture says
        why the other six were not. Every ring expansion, every tangent, every
        anchor and every card filed is drawn as it happens, paced by the same
        Trace step slider.
        """
        if self.bake is None or self.busy:
            return
        import importlib
        for mod in (nav, pcd):
            try:
                importlib.reload(mod)
            except Exception as e:
                self.status.set("reload failed: %s" % e)
                return

        pts = []
        if self.start:
            pts.append(self.start)
        pts += list(self.targets)
        if len(pts) < 2 and self.route:
            step = max(1, len(self.route) // 12)
            pts = [self.route[i] for i in range(0, len(self.route), step)]
        if len(pts) < 2:
            self.status.set("place a start and some points first, or load a route")
            return

        nav.AGL = float(self.vars["agl"].get())
        nav.MARGIN = min(0.5, nav.AGL * 0.5)
        nav.BLOCK_H = nav.AGL - nav.MARGIN
        nav.BODY_R = float(self.vars["standoff"].get())

        if self.live is not None:
            self.live_stop = True
            self.live = None
        self.show_radar.set(False)
        self.radar_fans = None
        self.radar_key = None

        del self.log_lines[:]
        self._log_at = 0
        self.trace_log.configure(state="normal")
        self.trace_log.delete("1.0", "end")
        self.trace_log.configure(state="disabled")

        self.live_ms = float(self.vars["trace_ms"].get())
        # Clear at the START, like generate() - so the LAST search stays on
        # the map until a new one replaces it.
        self.gen = None
        self.card_ev = []
        self.card_paths = []
        self.cards_shown = True
        self.card_stop = False
        self.cards_running = True
        # What the pink line was showing, so a stopped search can put it
        # back rather than leaving the map claiming a path nobody chose.
        self.cards_was = self.route
        self.cards = {"box": 0, "of": len(pts) - 1, "path": [], "done": False}
        self.cards_btn.configure(text="Stop search")
        if self.zoom <= 1.0001:
            self.frame_on(pts)
        self.status.set("searching %d boxes - Esc or the button stops it"
                        % (len(pts) - 1))

        def trace(kind, kw):
            """On the worker thread. Appends, sleeps, and touches no Tk."""
            if self.card_stop:
                raise pcd.Stopped()
            self.card_ev.append((kind, kw))
            if kind == "card" and len(self.card_paths) < 4000:
                self.card_paths.append((kw["points"], kw["reached"],
                                        kw.get("why", "")))
            ms = self.live_ms
            if ms > 0 and kind in ("ring", "ray", "anchor", "card"):
                time.sleep(ms / 1000.0)

        def work():
            try:
                pcd.TRACE = trace
                world = pcd.World(self.bake)
                self.log_lines.append("%d objects on the mask" % world.n)
                runs, report, closed = pcd.run(world, pts,
                                               on_box=self._card_box)
                # RUNS, not one path. A leg nobody could fly is a gap, and
                # joining across it would draw a straight line through the
                # buildings that beat it.
                self.cards["runs"] = runs
                self.cards["path"] = [p for r in runs for p in r]
                self.cards["closed"] = closed
            except pcd.Stopped:
                self.cards["closed"] = False
            except Exception:
                self.log_lines.append("search failed: "
                                      + traceback.format_exc()
                                      .strip().splitlines()[-1])
                self.cards["closed"] = False
            finally:
                pcd.TRACE = None
                self.cards["done"] = True

        threading.Thread(target=work, daemon=True).start()
        self.root.after(60, self._cards_tick)

    def _card_box(self, wi, target, out, best):
        """One box finished, on the worker thread. Words only."""
        done = len([c for c in out if c.reached])
        if best is None:
            # Counted by reason, not one line per card. Eight cards rejected
            # for the same thing is one fact, not eight.
            why = {}
            for c in out:
                why[c.why] = why.get(c.why, 0) + 1
            self.log_lines.append(
                "box %d: %d cards, none flyable" % (wi, len(out)))
            for reason, k in sorted(why.items(), key=lambda kv: -kv[1]):
                self.log_lines.append("  %d x %s" % (k, reason))
        else:
            self.log_lines.append(
                "box %d: %d cards, %d completed -> took %d points, %.0f m (%s)"
                % (wi, len(out), done, len(best.points) - 1, best.length(),
                   best.why))
        if self.cards is not None:
            self.cards["box"] = wi

    def _cards_tick(self):
        if not self.cards_running or self.cards is None:
            return
        try:
            self.live_ms = float(self.vars["trace_ms"].get())
        except Exception:
            pass
        self.drain_log()
        self.repaint()
        c = self.cards
        if not c.get("done"):
            self.status.set("box %d of %d - %d events - Esc stops it"
                            % (c["box"], c["of"], len(self.card_ev)))
            self.root.after(60, self._cards_tick)
            return
        # DRAIN AGAIN, NOW THAT done IS TRUE.
        #
        # The drain above happened BEFORE this check. If the worker finished
        # in the gap between the two - which it does, at trace step 0 - every
        # line it appended in that gap had no later tick to collect it and was
        # simply lost. Measured over three identical runs: 6, 13, 6 lines.
        # The last drain has to come after the last append is guaranteed, and
        # done being true is that guarantee.
        self.drain_log()
        self.cards_btn.configure(text="Hide cards" if self.card_paths
                                 else "Search cards (live)")
        ok = c.get("closed")
        found = c.get("path") or []
        n = len(found)

        # THE PINK LINE BECOMES WHAT WAS JUST FOUND.
        #
        # It is self.route, and the search never touched it - so the map went
        # on showing the SAVED path while a new one was being built beside
        # it, and the answer vanished when the search overlay came down. A
        # search that finishes and changes nothing on screen has not
        # finished as far as anyone watching is concerned.
        #
        # Not saved, and deliberately: Save publishes what GENERATE wrote,
        # and these points have not been through the exporter. Displayed,
        # kept, and restorable.
        # WHAT IT FOUND IS SHOWN, CLOSED OR NOT.
        #
        # This used to adopt the path only when the search closed, and put
        # the old one back otherwise - so a search that solved six boxes of
        # twelve changed nothing on screen and looked like it had done
        # nothing at all. The six boxes it DID solve are the most useful
        # thing on the map: they are where it got to before the wall, and
        # the last point is the wall. A partial answer is an answer.
        #
        # The previous path only comes back when there is genuinely nothing
        # to show.
        if n > 1:
            self.route = found
            self.route_saved = False
            # THE POINTS ARE NOT TOUCHED.
            #
            # A version of this replaced self.start and self.targets with the
            # anchors the search found, so that Generate could fly them and
            # light up Save. It committed a result the moment it appeared,
            # over the points that had been clicked by hand, with nothing
            # asked and nothing to undo from. Finishing is not consent.
            #
            # The search SHOWS its answer. Taking it is a separate decision
            # and needs a separate action.
        else:
            self.route = self.cards_was
        self.cards_was = None

        runs = c.get("runs") or []
        gaps = max(0, len(runs) - 1)
        self.status.set("%s  %d points%s over %d boxes, %d events%s"
                        % ("closed" if ok
                           else "%d leg%s nothing could fly"
                                % (gaps, "" if gaps == 1 else "s"),
                           n,
                           "" if gaps == 0 else " in %d runs" % len(runs),
                           c["of"], len(self.card_ev),
                           " - shown, not saved; your points are untouched"
                           if n > 1
                           else " - nothing found, the previous path is back"))
        self.trace_log.configure(state="normal")
        self.trace_log.insert("end", chr(10) + ("CLOSED" if ok else "STOPPED")
                              + ": %d points" % n + chr(10),
                              "hit" if ok else "bad")
        self.trace_log.see("end")
        self.trace_log.configure(state="disabled")
        # The search stays on the map only while it did NOT close - the
        # rings and refused cards are the diagnosis then. A search that
        # closed has produced a path, and the path is the better picture.
        self.cards_running = False
        if ok:
            self.cards = None
            self.card_paths = []
            self.cards_btn.configure(text="Search cards (live)")
        self.update_enabled()
        self.repaint()

    def trace_live(self):
        """Run the tangent navigator over the placed points, drawing as it goes.

        Driven from the Tk idle loop in small batches rather than run to
        completion and drawn afterwards. A navigator that only shows its answer
        cannot be watched failing, and watching it fail is the whole point -
        the picture at the end says where it stopped, not what it tried.

        The modules are reloaded first, so a change to the algorithm is picked
        up without restarting the Studio. Anything built BEFORE a reload keeps
        its old classes, so bake and radar are rebuilt here rather than reused.
        """
        if self.bake is None or self.busy:
            return
        if self.live is not None:
            self.live_stop = True
            return

        import importlib
        for mod in (fp, nav, tang):
            try:
                importlib.reload(mod)
            except Exception as e:
                self.status.set("reload failed: %s" % e)
                return

        pts = []
        if self.start:
            pts.append(self.start)
        pts += list(self.targets)
        if len(pts) < 2 and self.route:
            step = max(1, len(self.route) // 12)
            pts = [self.route[i] for i in range(0, len(self.route), step)]
        if len(pts) < 2:
            self.status.set("place a start and some points first, or load a route")
            return

        nav.AGL = float(self.vars["agl"].get())
        nav.MARGIN = min(0.5, nav.AGL * 0.5)
        nav.BLOCK_H = nav.AGL - nav.MARGIN
        nav.BODY_R = float(self.vars["standoff"].get())

        raw, plan_m, _dist, _pad = nav.build_world(self.bake, None)
        radar = nav.Radar(self.bake, plan_m, raw, self.bake.mx)

        # Frame it, unless the view is already somewhere on purpose.
        #
        # zoom 1.0 is the whole map, which is what a fresh load leaves and
        # nobody chooses - so it is safe to read as "no opinion". Any zoom at
        # all is an opinion and is left exactly alone: yanking a view someone
        # set is the same class of rudeness as moving their selection.
        if self.zoom <= 1.0001:
            self.frame_on(pts)

        # KEEP THE LOG. Appending to a list is all the worker may do; tick
        # moves it into the widget.
        del self.log_lines[:]           # in place - see drain_log
        self._log_at = 0
        # note(), not log_lines.append - so the ring-by-ring and
        # bearing-by-bearing chatter is dropped and the waypoints and
        # arrivals are not buried under it.
        self.live = tang.TangentNav(self.bake, radar, log=self.note)
        self.trace_log.configure(state="normal")
        self.trace_log.delete("1.0", "end")
        self.trace_log.configure(state="disabled")
        self.live_stop = False
        self.live_paused = False
        self.live_ms = float(self.vars["trace_ms"].get())
        self.live_btn.configure(text="Stop trace")
        self.status.set("tracing %d points - space pauses" % len(pts))

        # A generator so the Tk loop stays responsive: the navigator runs in
        # bursts between repaints instead of blocking the UI for the whole run.
        state = {"done": False, "ok": False}

        def work():
            try:
                state["ok"] = self.live.run(pts, on_step=self._live_step,
                                            step_every=1)
            except tang.Stopped:
                state["ok"] = False
            except Exception:
                state["ok"] = False
            state["done"] = True

        # run() is a straight loop, so step it by running it on a thread and
        # repainting from what it has appended so far. self.live.path only ever
        # grows, so reading it from the UI thread is safe enough for a picture.
        import threading
        threading.Thread(target=work, daemon=True).start()

        def tick():
            if self.live is None:
                return
            # Sampled here, on the UI thread, so the slider still works mid
            # trace without the worker ever touching Tk.
            try:
                self.live_ms = float(self.vars["trace_ms"].get())
            except Exception:
                pass
            self.drain_log()
            self.repaint()
            if not self.live_paused:
                self.status.set("tracing: %d points, %d rays tried - space pauses"
                                % (len(self.live.path), len(self.live.tries)))
            if state["done"] or self.live_stop:
                self._trace_done(state["ok"])
                return
            self.root.after(60, tick)

        self.root.after(60, tick)

    def _log_select_all(self, _e=None):
        self.trace_log.tag_add("sel", "1.0", "end-1c")
        return "break"

    def _log_copy(self, everything):
        try:
            if everything:
                text = self.trace_log.get("1.0", "end-1c")
            else:
                text = self.trace_log.get("sel.first", "sel.last")
        except Exception:
            text = self.trace_log.get("1.0", "end-1c")
        self.root.clipboard_clear()
        self.root.clipboard_append(text)
        n = len(text.split(chr(10)))
        self.status.set("copied %d line%s" % (n, "" if n == 1 else "s"))

    def _log_menu(self, e):
        try:
            self.log_menu.tk_popup(e.x_root, e.y_root)
        finally:
            self.log_menu.grab_release()
        return "break"

    def clear_log(self):
        del self.log_lines[:]
        self._log_at = 0
        self.trace_log.configure(state="normal")
        self.trace_log.delete("1.0", "end")
        self.trace_log.configure(state="disabled")

    def drain_log(self):
        """Move whatever the navigator has said into the panel.

        Takes the list away from the worker in one go rather than popping,
        so a line appended mid-drain is simply picked up next time instead of
        being lost or double-printed.
        """
        # NEVER REBIND THE LIST.
        #
        # This was `lines, self.log_lines = self.log_lines, []`, and the
        # navigator holds `self.log_lines.append` - a method bound to the
        # ORIGINAL list, captured when the run started. One drain and every
        # later line went into an orphaned list nobody reads: 4 lines survived
        # a run that produced sixty. Read by index off a list that is only
        # ever cleared IN PLACE.
        n = len(self.log_lines)
        lines = self.log_lines[self._log_at:n]
        self._log_at = n
        if not lines:
            return
        self.trace_log.configure(state="normal")
        for ln in lines:
            tag = ""
            if ln.startswith("waypoint"):
                tag = "wp"
            elif "reached" in ln:
                tag = "hit"
            elif ("STUCK" in ln or "stalled" in ln or "NO ROUTE" in ln
                  or "failed" in ln or "MAX_STEPS" in ln):
                tag = "bad"
            self.trace_log.insert("end", ln + chr(10), tag)
        # Only follow the tail if the tail is what is being looked at - a
        # scroll back to read something must not be yanked forward again.
        if self.trace_log.yview()[1] > 0.995:
            self.trace_log.see("end")
        self.trace_log.configure(state="disabled")

    def _trace_done(self, ok):
        nvg = self.live
        self.live_btn.configure(text="Trace live (tangent)")
        if nvg is None:
            return
        flown = sum(math.hypot(nvg.path[i + 1][0] - nvg.path[i][0],
                               nvg.path[i + 1][1] - nvg.path[i][1])
                    for i in range(len(nvg.path) - 1))
        by = {}
        for t in nvg.tries:
            if t.verdict == "TAKEN":
                by[t.layer] = by.get(t.layer, 0) + 1
        # Beside the BAKES, not in cam_paths.
        # cam_paths is tracked and holds the .campath files that ship
        # with the work that made them; a trace writes two CSVs every
        # run, so pointing them there put untracked scratch in a
        # committed directory on every click. The bake folder is where
        # the plan CSVs these are compared against already live.
        stem = os.path.join(nav.FOLDER, (self.map_name or "map") + "_tangent")
        try:
            np_ = nvg.export_path_csv(stem + "_path.csv")
            nt_ = nvg.export_tries_csv(stem + "_tries.csv")
            wrote = "  wrote %s_path.csv (%d) and _tries.csv (%d)" % (
                os.path.basename(stem), np_, nt_)
        except Exception as e:
            wrote = "  export failed: %s" % e
        self.status.set("%s  %.0f m, moves %s%s"
                        % ("closed" if ok else "STOPPED", flown, by, wrote))
        self.drain_log()
        self.trace_log.configure(state="normal")
        self.trace_log.insert("end", chr(10) + "%s: %.0f m over %d points. %s"
                              % ("CLOSED" if ok else "STOPPED", flown,
                                 len(nvg.path), by) + chr(10),
                              "hit" if ok else "bad")
        self.trace_log.insert("end", wrote.strip() + chr(10))
        self.trace_log.see("end")
        self.trace_log.configure(state="disabled")
        self.live = None
        self.repaint()

    def clear_targets(self):
        """Clear every point - the targets AND the start.

        The status line always promised "click to place a start again", but
        the start survived the button; only the targets went. Clear means
        start over, and starting over includes the click that began it.
        """
        if self.busy or not self.edit_path:
            return
        self.targets = []
        self.start = None
        self.route = None
        # Whatever was selected is gone now, whichever kind it was.
        if self.selection and self.selection[0] in ("target", "start"):
            self.selection = None
            self.moving = False
        self.status.set("points cleared - click to place a start again")
        self.update_enabled()
        self.repaint()

    # Sliders whose value changes what the MASK looks like. Everything else
    # only changes a number.
    MASK_SLIDERS = ("agl", "standoff")
    # Sliders that change the PICTURE but not the mask: a repaint, no
    # re-render. Without this the averaged path only moved when something
    # else happened to repaint, which looks like a dead slider.
    DRAW_SLIDERS = ("smooth", "smooth_n")

    def refresh_labels(self, which=None):
        """Put every slider's value in its label, and re-render only if asked.

        Driven from the sliders that EXIST, not from a hand-written list. It
        used to read `for k in ("smooth", "agl")`, so a slider added later
        had a label that never moved off its starting value - which is
        exactly what happened to Trace step. A list of names that has to be
        remembered will eventually not be.

        `which` names the slider that moved. Re-rendering the mask is a
        LANCZOS pass over 2048 squared, and dragging a timing slider has no
        business paying for it - only agl and standoff change what the mask
        shows. None means "something else changed, do it all".
        """
        for k, v in list(self.vars.items()):
            if k.endswith("_lbl") or k.endswith("_w"):
                continue
            lbl = self.vars.get(k + "_lbl")
            if lbl is None:
                continue
            if k == "standoff":
                # Snap to 0.5 m steps and show it that way.
                so = round(float(v.get()) * 2.0) / 2.0
                if abs(so - float(v.get())) > 1e-9:
                    v.set(so)
                lbl.configure(text="%.1f" % so)
            else:
                lbl.configure(text=str(v.get()))

        if which is not None and which not in self.MASK_SLIDERS:
            if which in self.DRAW_SLIDERS and self.mask_full is not None:
                self.repaint()
            return
        if self.mask_full is not None and not self.busy:
            self.render_mask()

    # ------------------------------------------------------------- generate

    def generate(self):
        if self.busy or self.bake is None or not self.edit_path:
            return
        if self.start is None:
            self.status.set("click a start first")
            return
        if not self.targets:
            self.status.set("click some points for it to visit")
            return

        # What to put back if this is abandoned. Generate replaces the route,
        # and until now there was no way to change your mind - the old one was
        # gone the moment the new one landed.
        # A PLAYBACK AND A LIVE FLIGHT DO NOT SHARE THE MAP.
        #
        # Trace live re-flies the SAVED waypoints - a playback - and its rays
        # and its path stayed on screen when Generate was pressed afterwards,
        # so two different navigators' work was drawn on top of each other
        # with nothing to say which was which. Dropping self.live stops both
        # the drawing and the repaint timer; the worker sees live_stop at its
        # next step and unwinds on its own.
        if self.live is not None:
            self.live_stop = True
            self.live = None
            self.live_paused = False

        # OFF for every generation. The imaging belongs to the path that was
        # there before, and leaving thousands of its standing rays on screen
        # over a flight in progress is the reason the live sweep could not be
        # seen. Tick it again afterwards to photograph the new one.
        self.show_radar.set(False)
        self.radar_fans = None
        self.radar_key = None

        self.gen_was = (self.route, getattr(self, "pending", None),
                        getattr(self, "route_saved", False))
        # CLEAR EVERYTHING, HERE, AT THE START. NOWHERE ELSE.
        #
        # A generation used to dump its own data on the way out - _done,
        # _failed, _generate_cancelled and _gen_tick all set self.gen = None -
        # so the flight vanished at exactly the moment it was worth looking
        # at. Clearing belongs at the start of the NEXT run, where it cannot
        # destroy the thing being examined.
        self.gen = None
        self.gen_cancel = False
        self.cards = None
        self.cards_running = False
        self.cards_shown = True
        self.card_paths = []
        self.card_ev = []
        # Same slider as the trace. Read here, on the UI thread, and left as a
        # plain float - the worker must not touch Tk.
        try:
            self.live_ms = float(self.vars["trace_ms"].get())
        except Exception:
            self.live_ms = 0.0
        del self.log_lines[:]
        self._log_at = 0
        self.trace_log.configure(state="normal")
        self.trace_log.delete("1.0", "end")
        self.trace_log.configure(state="disabled")

        self.busy = True
        self.go.state(["disabled"])
        threading.Thread(target=self._run, daemon=True).start()
        self.root.after(60, self._gen_tick)

    def _gen_step(self, path, fans, steps, events=()):
        """The navigator, mid-flight, on its own thread.

        Stores references and NOTHING else - no Tk, no copying. path and fans
        only ever grow, so the repaint timer reads a prefix and never a torn
        value; copying 6000 steps of path per step would cost more than the
        flight.
        """
        self.gen = (path, fans, steps, events)
        if self.gen_cancel:
            raise nav.Cancelled()
        ms = self.live_ms
        if ms > 0:
            time.sleep(ms / 1000.0)

    def _gen_tick(self):
        """Repaint while a flight is in the air."""
        if not self.busy or self.gen_was is None:
            # Do NOT clear self.gen. The flight stays drawn until the next
            # generation clears it - see generate().
            self.repaint()
            return
        self.drain_log()
        if self.gen is not None:
            _p, fans, steps = self.gen[0], self.gen[1], self.gen[2]
            self.repaint()
            if not self.gen_cancel:
                self.status.set("flying: step %d, %d points - Esc cancels"
                                % (steps, len(_p)))
        self.root.after(60, self._gen_tick)

    def _generate_cancelled(self):
        """Escape during a flight. Put back exactly what was there."""
        # The flight is left drawn - cancelling is not a reason to throw
        # away what it had done by the time you stopped it. Only the PATH
        # goes back, which is what Escape means.
        self.busy = False
        if self.gen_was is not None:
            self.route, self.pending, self.route_saved = self.gen_was
        elif self.loaded_route:
            self.route = list(self.loaded_route)
        self.gen_was = None
        self.go.state(["!disabled"])
        self.update_enabled()
        self.repaint()
        self.status.set("generation cancelled - "
                        + ("the previous path is back" if self.route
                           else "there was no previous path"))

    # Lines this deep are probe detail - one per ray, per ring, per bearing
    # tried. They belong in a CSV, not on screen: at four or five a step they
    # push the thing you are reading off the top before you have read it.
    # The navigators already indent that way, so the convention is the filter.
    LOG_DETAIL_INDENT = 4

    def note(self, msg):
        """Put one line in the Navigator box, if it is worth a line.

        Appending to a list is all this does, so it is safe from the worker
        threads; the repaint timers drain it into the widget.
        """
        for line in str(msg).rstrip().split(chr(10)):
            if not line.strip():
                continue
            if len(line) - len(line.lstrip(" ")) >= self.LOG_DETAIL_INDENT:
                continue
            self.log_lines.append(line.rstrip())

    def _log(self, msg):
        """Progress from the generate pipeline. Worker thread.

        This only ever set the status bar - one line, overwritten by the next
        one - so a whole generation's account, including everything the
        exporter prints, was produced and thrown away. The box was empty
        after a run that had plenty to say.
        """
        self.note(msg)
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
                self.map_name, self.start, list(self.targets), self._log,
                smooth_passes=int(self.vars["smooth"].get()),
                on_step=self._gen_step,
                # The Smooth sample size slider, applied INSIDE the export so
                # heading, tilt, bank and speed are all derived from the
                # averaged positions. What is drawn burnt orange is what
                # lands in the file.
                average_n=int(round(float(self.vars["smooth_n"].get()))))

            import csv as _csv
            rows = list(_csv.DictReader(open(csv_path)))
            route = [(float(r["x"]), float(r["z"])) for r in rows]
            # The scratch copy Generate just wrote. Save publishes it.
            out = os.path.join(FOLDER, self.map_name + ".campath")
            avg_n = int(round(float(self.vars["smooth_n"].get())))
            self.root.after(0, lambda: self._done(route, len(rows), out, avg_n))
        except nav.Cancelled:
            # Not a failure. Asked for, and answered.
            self.root.after(0, self._generate_cancelled)
        except BaseException:
            # BaseException, not Exception. Anything that escapes this thread
            # leaves the UI stuck disabled with no message, which is the worst
            # possible failure mode - a window that looks broken and says
            # nothing. SystemExit from a called library did exactly that.
            tb = traceback.format_exc().strip().splitlines()[-1]
            self.root.after(0, lambda: self._failed(tb))

    def _done(self, route, n, out, avg_n=None):
        # ONE PATH ON SCREEN WHEN IT WORKED.
        #
        # The overlay is kept after a FAILURE, because where it got to is the
        # diagnosis. After a success it is just the raw flown line lying on
        # top of the smoothed route that replaced it - two paths, and the
        # brighter one is the one that is not the answer. Keeping data is for
        # when there is nothing better to look at; here there is.
        self.gen = None
        self.cards = None
        self.live = None
        self.drain_log()          # _gen_tick has stopped; nothing else will
        self.gen_was = None
        self.route = route
        self.route_avg_n = avg_n
        self.route_saved = False
        self.pending = out
        self.busy = False
        self.update_enabled()
        self.repaint()
        self.status.set("wrote %d points to %s" % (n, out))

    def _failed(self, msg):
        """A generation that fell over. KEEP WHAT IT FLEW.

        This dropped self.gen and never repainted, so the flight you had just
        sat and watched vanished the instant it failed and the map went back
        to showing the old path - the one moment the picture is worth the
        most, and it was thrown away. Where it got to before it fell over IS
        the diagnosis: the last point is the place that beat it.

        Cancelling is different and stays different - Escape means "put it
        back", and it does.
        """
        # DUMP NOTHING. Everything it gathered stays where it is - the
        # flight, the fans, the log - and the next generation clears it. The
        # old path stays too: a failure has produced nothing to replace it
        # with.
        flown = len(self.gen[0]) if (self.gen and self.gen[0]) else 0
        self.drain_log()          # whatever it managed to say before it fell
        self.gen_was = None
        self.busy = False
        self.update_enabled()
        self.repaint()
        self.status.set("failed: %s%s"
                        % (msg, ("  -  the %d points it flew are still on the "
                                 "map" % flown) if flown > 1 else
                           "  -  it never got as far as flying"))


def main():
    root = tk.Tk()
    studio = Studio(root)

    # A test can ask for the Studio to itself:
    #     set PS_TEST_LOCK=1   or   python path_studio.py --test-lock
    # Nothing turns this on by itself. It exists so that when a probe is
    # driving, the window says so and cannot be typed or clicked into.
    if os.environ.get("PS_TEST_LOCK") == "1" or "--test-lock" in sys.argv:
        studio.set_test_lock(True, "started with --test-lock.")

    # OPEN ON A MAP.
    #
    #     python path_studio.py            -> START_MAP
    #     python path_studio.py 04_himmelsdorf
    #     python path_studio.py --no-map   -> the picker, as before
    #
    # PathStudio.exe forwards its own arguments to this script, so the same
    # name works from the launcher.
    #
    # Only if the map already HAS a height map. Loading one that does not
    # puts the no-height-map dialog on screen before the window has been
    # looked at, and a modal is a poor way to say good morning.
    wanted = None
    if "--no-map" not in sys.argv:
        plain = [a for a in sys.argv[1:] if not a.startswith("-")]
        wanted = plain[0] if plain else START_MAP

    if wanted:
        def open_it():
            if wanted in getattr(studio, "baked", ()):
                studio.load_named(wanted)
            else:
                studio.status.set("%s has no height map - pick a map, or bake "
                                  "it" % wanted)
        # After the window is up, so the load draws into a real canvas rather
        # than one that has not been sized yet.
        root.after(0, open_it)

    root.mainloop()


if __name__ == "__main__":
    main()
