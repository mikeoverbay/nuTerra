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
import struct
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
import fog_curve as fc
import terrain_bake as tb

FOLDER = nav.FOLDER

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
# RING_RADIUS is still read on every generate, not only by the ring: the
# departure leg walks max(30, min(radius * 0.4, 90)) metres out along the
# heading, so at 260 that is a 90 m leg.
RING_RADIUS = 260.0     # metres
RING_WAYPOINTS = 14     # points around the ring
RING_SIDE = 1           # +1 turns left out of the departure leg, -1 right


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


def plan_from_seed(map_name, start_xz, heading, radius, side, waypoints, targets, log,
                   smooth_passes=2):
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
        # Metres from the start to where the heading drag was released. The
        # marker is redrawn at that distance so it stays where it was dropped.
        self.heading_len = None
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

        # The map the list points at, whether or not it has a bake - the Bake
        # button works on this. And the global_AM picture for the loaded map,
        # on the bake grid, loaded the first time the underlay is switched on.
        self.selected_name = None
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

        self.maps = tk.Listbox(left, width=26, height=5, exportselection=False,
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
        self.show_am = tk.BooleanVar(value=True)
        ttk.Checkbutton(f_am, text="global_AM", variable=self.show_am,
                        command=self.on_am_toggle).pack(side="left")
        self.am_blend = tk.DoubleVar(value=0.5)
        ttk.Scale(f_am, from_=0.0, to=1.0, variable=self.am_blend,
                  orient="horizontal", length=110,
                  command=lambda *_: self.on_am_toggle()).pack(side="left", padx=(6, 0))

        # Edit path is the lock on everything below it and on the map clicks
        # that place the start and the points. Off, all of it is greyed.
        self.edit_btn = ttk.Button(left, text="Edit path", command=self.toggle_edit_path)
        self.edit_btn.grid(row=6, column=0, sticky="we", pady=(4, 6))

        r = 7
        self.vars = {}
        for key, label, lo, hi, init in (
                ("smooth", "Path smoothing", 0, 6, 2),
                ("agl", "Height over ground (m)", 1, 30, int(nav.AGL)),
                ("standoff", "Standoff (m)", 0.5, 6, min(6.0, max(0.5, round(nav.BODY_R * 2) / 2.0)))):
            ttk.Label(left, text=label).grid(row=r, column=0, sticky="w")
            # Standoff moves in half metres; the others are whole numbers.
            v = tk.DoubleVar(value=init) if key == "standoff" else tk.IntVar(value=init)
            self.vars[key] = v
            sc = ttk.Scale(left, from_=lo, to=hi, variable=v, orient="horizontal",
                           length=200, command=lambda *_: self.refresh_labels())
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

        # Notes at the BOTTOM of this panel, anchored there: a spacer row
        # takes the slack, so the lamp controls stay at the top and the notes
        # sit under them however tall the window is.
        right.rowconfigure(rr, weight=1)
        rr += 1
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
            "Left drag      start + heading (first)\n"
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
        ttk.Label(left, textvariable=self.status, wraplength=210,
                  justify="left").grid(row=r, column=0, columnspan=2, sticky="w")
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
        self.other_names = others
        self.refill_maps()

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

    def refill_maps(self):
        """Fill the list from the search box: the arenas when it is empty,
        every space whose name contains the text when it is not."""
        q = self.search.get().strip().lower()
        if q:
            names = [n for n in self.row_names + self.other_names if q in n.lower()]
        else:
            names = list(self.row_names)
        self.visible_names = names
        self.maps.delete(0, "end")
        for n in names:
            self.maps.insert("end", n if n in self.baked else n + "    (no bake)")
        if self.selected_name in names:
            k = names.index(self.selected_name)
            self.maps.selection_set(k)
            self.maps.see(k)

    def load_selected(self):
        sel = self.maps.curselection()
        if not sel or self.busy:
            return
        i = sel[0]
        if i < len(self.visible_names):
            self.load_named(self.visible_names[i])

    def load_named(self, name):
        if self.busy or not name:
            return
        self.selected_name = name
        self.bake_btn.state(["!disabled"])
        if name not in getattr(self, "baked", ()):  # nothing to draw or plan
            self.status.set("%s has no bake yet. Bake terrain (Python) writes a "
                            "terrain-only one now; opening it once in nuTerra "
                            "writes the real one." % name)
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
        self.heading_len = None
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
            # The file still records the ring's radius, waypoint count and turn
            # direction - cam_path writes all three and older seeds carry real
            # values - but none of them has a control any more (RING_RADIUS,
            # RING_WAYPOINTS, RING_SIDE). Read and ignored rather than dropped
            # from the format, so a seed written by an older Path Studio still
            # loads.
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
        self.busy = False
        self.bake_btn.state(["!disabled"])
        if err:
            self.status.set("bake failed: %s" % err)
            return
        self.find_maps()
        self.load_named(name)
        self.status.set("%s: terrain-only bake written - no models or trees in "
                        "it. Open the map in nuTerra for the real one." % name)

    # ---------------------------------------------------------- global_AM

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
        # A straight mix over EVERY cell: 0 is the depth shading alone, 1 is
        # the AM alone. The obstacles fade with it, which is what a slider
        # that promises full AM has to do; park it near the middle to see both.
        if self.show_am.get() and self.load_am():
            k = float(self.am_blend.get())
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
                # In WORLD space and then projected, so it holds its place on
                # the map through a pan or a zoom rather than only looking
                # right at one of them.
                #
                # At the length it was dragged to. A seed loaded from disk
                # carries the angle but not the length, so that falls back to
                # 60 m.
                hlen = self.heading_len if self.heading_len else 60.0
                hx = self.start[0] + hlen * math.sin(self.heading)
                hz = self.start[1] + hlen * math.cos(self.heading)
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
        """Drop the selection and leave placement mode - closing the light
        editor too, after asking if it has edits."""
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

        self.start = self.to_world(e.x, e.y)
        self.drag = (e.x, e.y)
        self.heading = None
        self.heading_len = None
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
        if self.start is None or not self.edit_path:
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
        # Locked, a release must not set a heading either - a plain click on
        # the locked map gets here with a start on file.
        if self.start is None or not self.edit_path or self.drag is None:
            return
        wx, wz = self.to_world(e.x, e.y)
        dx, dz = wx - self.start[0], wz - self.start[1]
        if math.hypot(dx, dz) < 4.0:
            self.status.set("drag further - the line sets the heading")
            return
        self.heading = math.atan2(dx, dz)
        self.heading_len = math.hypot(dx, dz)
        # LOCKED. self.drag is canvas pixels and used to be left set after the
        # release, so the branch that draws it kept winning and the end point
        # was pinned to the SCREEN - pan or zoom and it slid across the map.
        # Clearing it hands the drawing to the world-space branch, which now
        # uses heading_len so the marker stays exactly where it was dropped
        # instead of snapping to a fixed 60 m.
        self.drag = None
        self.status.set("start (%.0f, %.0f) heading %.0f deg. Generate when ready."
                        % (self.start[0], self.start[1], math.degrees(self.heading)))
        self.repaint()

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

        # The ring is only ever used when there are no points to visit, and
        # left click places points now, so in practice it never is. Loop radius
        # is the one ring control left with a slider; the turn direction and the
        # ring waypoint count are RING_SIDE / RING_WAYPOINTS.
        # Path smoothing applies to every route, points or ring, so nothing
        # here is greyed out any more. The ring's radius is RING_RADIUS.
        self.ring_lbl.configure(text="")

        # The path lock. Everything that can change the path follows
        # edit_path; the map has to be loaded and nothing running.
        loaded = self.bake is not None and not self.busy
        unlocked = loaded and self.edit_path
        self.edit_btn.state(["!disabled" if loaded else "disabled"])
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
            # Backed all the way out: the one thing left to undo is the start
            # itself, with its heading. Backspace used to stop here and leave
            # it, so the only way to lose a start was the Clear button.
            if self.start is None:
                return
            self.start = None
            self.heading = None
            self.drag = None
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

    def clear_targets(self):
        """Clear every point - the targets AND the start with its heading.

        The status line always promised "click to place a start again", but
        the start survived the button; only the targets went. Clear means
        start over, and starting over includes the click that began it.
        """
        if self.busy or not self.edit_path:
            return
        self.targets = []
        self.start = None
        self.heading = None
        self.drag = None
        self.route = None
        # Whatever was selected is gone now, whichever kind it was.
        if self.selection and self.selection[0] in ("target", "start"):
            self.selection = None
            self.moving = False
        self.status.set("points cleared - click to place a start again")
        self.update_enabled()
        self.repaint()

    def refresh_labels(self):
        for k in ("smooth", "agl"):
            self.vars[k + "_lbl"].configure(text=str(self.vars[k].get()))
        # Snap standoff to 0.5 m steps and show it that way.
        so = round(float(self.vars["standoff"].get()) * 2.0) / 2.0
        if abs(so - float(self.vars["standoff"].get())) > 1e-9:
            self.vars["standoff"].set(so)
        self.vars["standoff_lbl"].configure(text="%.1f" % so)
        if self.mask_full is not None and not self.busy:
            self.render_mask()

    # ------------------------------------------------------------- generate

    def generate(self):
        if self.busy or self.bake is None or not self.edit_path:
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
                RING_RADIUS, RING_SIDE,
                RING_WAYPOINTS, list(self.targets), self._log,
                smooth_passes=int(self.vars["smooth"].get()))

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
        self.update_enabled()
        self.repaint()
        self.status.set("wrote %d points to %s" % (n, out))

    def _failed(self, msg):
        self.busy = False
        self.update_enabled()
        self.status.set("failed: " + msg)


def main():
    root = tk.Tk()
    Studio(root)
    root.mainloop()


if __name__ == "__main__":
    main()
