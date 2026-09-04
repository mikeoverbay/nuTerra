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
    """The saved route AND the clicks behind it: ([(x, z)], seed) or (None, None).

    Reads the .campath, not the CSV beside the bake. The .campath is the artefact
    that actually ships, so this shows what nuTerra would fly rather than what the
    last run in this folder happened to leave behind - and since version 2 it is
    the only place the seed exists at all.
    """
    path = os.path.join(cp.campath_dir(), map_name + ".campath")
    if not os.path.exists(path):
        return None, None
    try:
        meta, pts = cp.read_path(path)
    except Exception:
        # A half written or older-format file is not worth refusing to open the
        # map over. Draw nothing and let a regenerate replace it.
        return None, None
    return [(p[0], p[2]) for p in pts], meta["seed"]

CANVAS = 780         # starting size only - the map scales with the window
MIN_VIEW = 240


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
            self.buf = ""

        def write(self, chunk):
            # sink is None under pythonw.exe, which has no stdout at all.
            if self.sink is not None:
                self.sink.write(chunk)
            self.buf += chunk
            while "\n" in self.buf:
                line, self.buf = self.buf.split("\n", 1)
                if line.strip():
                    log("  " + line.strip())

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
        self._resize_job = None

        left = ttk.Frame(root, padding=8)
        left.grid(row=0, column=0, sticky="ns")
        ttk.Label(left, text="Maps with a bake").grid(row=0, column=0, sticky="w")

        self.maps = tk.Listbox(left, width=26, height=18, exportselection=False)
        self.maps.grid(row=1, column=0, pady=(2, 2))
        self.maps.bind("<<ListboxSelect>>", lambda e: self.load_selected())

        # The odd spaces, after the rotation list rather than mixed into it.
        self.row_names = []
        self.other_names = []
        self.baked = set()
        self.other_lbl = ttk.Label(left, text="Other spaces", foreground="#777")
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
        self.ring_lbl = ttk.Label(left, text="", foreground="#777",
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

        self.status = tk.StringVar(value="pick a map")
        ttk.Label(left, textvariable=self.status, wraplength=210,
                  justify="left").grid(row=r, column=0, columnspan=2, sticky="w")

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
        # Bound on the ROOT, not the canvas - a Canvas only sees key events when
        # it holds focus, and clicking a slider takes focus away, so bound there
        # Backspace would work until the moment you touched a control.
        root.bind("<BackSpace>", self.on_undo_target)
        root.bind("<Delete>", self.on_undo_target)

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
        self.route, seed = existing_plan(name)
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
        self.mask_full = Image.fromarray(img, "RGB")
        self.repaint()

    def repaint(self):
        if self.mask_full is None:
            return
        im = self.mask_full.resize((self.view, self.view), Image.LANCZOS)
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

        self.photo = ImageTk.PhotoImage(im)
        self.canvas.delete("all")
        self.canvas.create_image(self.ox, self.oy, anchor="nw", image=self.photo)

    # ------------------------------------------------------------ transforms

    def to_view(self, wx, wz):
        """World -> pixels INSIDE the map square (what gets drawn into)."""
        c, r = self.bake.texel_of(wx, wz)
        s = self.view / float(self.bake.w)
        return (c * s, r * s)

    def to_world(self, px, py):
        """Canvas pixels -> world. Takes the letterbox offset off first."""
        s = float(self.bake.w) / self.view
        return self.bake.world_of((px - self.ox) * s, (py - self.oy) * s)

    # ---------------------------------------------------------------- input

    def on_press(self, e):
        if self.bake is None or self.busy:
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
        if self.start is None or self.busy:
            return
        self.drag = (e.x, e.y)
        self.repaint()

    def on_release(self, e):
        if self.start is None or self.busy:
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
        src = getattr(self, "pending", None)
        if not src or not os.path.exists(src):
            self.status.set("nothing generated to save")
            return

        dst_dir = cp.campath_dir()
        os.makedirs(dst_dir, exist_ok=True)
        dst = os.path.join(dst_dir, self.map_name + ".campath")

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
            shutil.copyfile(src, dst)
        except Exception as e:
            self.status.set("could not save: %s" % e)
            return

        self.route_saved = True
        self.update_enabled()
        self.status.set("saved to " + dst)

    def update_enabled(self):
        """Grey out the ring controls when targets are driving the route.

        Targets replace the ring entirely, so Turn, Loop radius and Waypoints do
        nothing the moment there is one - and a control that looks live but is
        ignored is worse than no control. Right-clicking a target used to
        silently kill the Left/Right buttons with no sign of it.
        """
        # Only when there is something generated and not yet published. A
        # route loaded from disk is already saved and Save has nothing to do.
        can_save = (getattr(self, "pending", None) is not None
                    and not getattr(self, "route_saved", False)
                    and not self.busy)
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
        """Backspace or Delete drops the most recently placed target."""
        if self.busy or not self.targets:
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
