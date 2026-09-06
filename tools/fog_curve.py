"""Fog falloff curves for the lamp shafts.

WHAT A CURVE IS
    How much a lamp scatters into the air at a normalised distance s from the
    bulb, s = dist / range, 0 at the bulb and 1 at the edge of the range. One
    number used to do this job - `fog_falloff`, the exponent of one analytic
    shape - and it could not lengthen a shaft without brightening its core,
    because the whole curve moved together. A curve can hold the core where it
    is and pull the tail out.

WHERE THEY LIVE
    VM_FOG_Curve_<n>.png, n = 0..2, beside the .campath files. Both sides
    already agree on that folder: cam_path.campath_dir() finds it by walking up
    from this file, and nuTerra's MapCamPath resolves it the same way when it
    loads the route. nuTerra re-reads the curves on the same Reload Cam Path
    that re-reads the lamps, so a save here is live without a rebuild.

    Each PNG is 256 wide, greyscale, every column one sample, row 0 is what
    nuTerra reads (the other rows repeat it so the file is visible in a
    viewer). The five handles that made the curve are stored in the PNG's text
    metadata under "nuTerra.handles", so the editor re-opens the same handles
    and not a fit of them.

THE FIVE HANDLES
    start      s fixed at 0        the level at the bulb
    trans 1    free                first transition
    trans 2    free                second transition
    falloff    free                where the tail starts falling
    end        s fixed at 1        the level at the edge - defaults to 0, and
                                   the surfaces' own window also reaches zero
                                   there, so shaft and pool agree where light
                                   ends whatever the shape between

    Between handles: monotone cubic (Fritsch-Carlson), so a curve drawn
    falling never bounces back up between two handles.

    python tools/fog_curve.py [<folder>] [--force]   writes the three defaults
"""
import os
import sys

from PIL import Image, PngImagePlugin

N_CURVES = 3
SAMPLES = 256
PNG_ROWS = 16
FILE_FMT = "VM_FOG_Curve_%d.png"
META_KEY = "nuTerra.handles"
HANDLE_NAMES = ("start", "trans 1", "trans 2", "falloff", "end")

# Curve 0 traces the shape the shader used before curves existed,
# (1 - s^4)^2 / (1 + 8 s^2), so a map that never picks a curve renders as it
# did. 1 is a long shaft: the core held out to a quarter of the range, then a
# slow decline. 2 is a wide soft glow with a capped core, for fires and lit
# windows rather than a bulb on a post.
DEFAULT_HANDLES = {
    0: [(0.0, 1.00), (0.20, 0.755), (0.45, 0.351), (0.75, 0.085), (1.0, 0.0)],
    1: [(0.0, 1.00), (0.25, 0.900), (0.55, 0.600), (0.80, 0.250), (1.0, 0.0)],
    2: [(0.0, 0.70), (0.30, 0.700), (0.60, 0.500), (0.85, 0.200), (1.0, 0.0)],
}


def curve_path(folder, k):
    return os.path.join(folder, FILE_FMT % int(k))


def sample_curve(handles, n=SAMPLES):
    """Monotone cubic through the handles, n samples over s in [0, 1]."""
    pts = sorted((float(s), float(v)) for s, v in handles)
    xs = [p[0] for p in pts]
    ys = [p[1] for p in pts]
    k = len(xs)
    if k < 2:
        return [min(1.0, max(0.0, ys[0] if ys else 0.0))] * n

    d = []
    for i in range(k - 1):
        h = xs[i + 1] - xs[i]
        d.append((ys[i + 1] - ys[i]) / h if h > 1e-6 else 0.0)

    # Fritsch-Carlson tangents: zero at a local extremum, harmonic mean
    # weighted by the interval widths elsewhere. This is what keeps a
    # descending curve descending between two handles.
    m = [0.0] * k
    m[0] = d[0]
    m[-1] = d[-1]
    for i in range(1, k - 1):
        if d[i - 1] * d[i] <= 0.0:
            m[i] = 0.0
        else:
            w1 = 2.0 * (xs[i + 1] - xs[i]) + (xs[i] - xs[i - 1])
            w2 = (xs[i + 1] - xs[i]) + 2.0 * (xs[i] - xs[i - 1])
            m[i] = (w1 + w2) / (w1 / d[i - 1] + w2 / d[i])

    out = []
    for j in range(n):
        s = j / float(n - 1)
        i = 0
        while i < k - 2 and s > xs[i + 1]:
            i += 1
        h = xs[i + 1] - xs[i]
        if h <= 1e-6:
            v = ys[i + 1]
        else:
            t = (s - xs[i]) / h
            t2 = t * t
            t3 = t2 * t
            v = ((2 * t3 - 3 * t2 + 1) * ys[i] + (t3 - 2 * t2 + t) * h * m[i]
                 + (-2 * t3 + 3 * t2) * ys[i + 1] + (t3 - t2) * h * m[i + 1])
        out.append(min(1.0, max(0.0, v)))
    return out


def write_curve(path, handles):
    """Write one curve PNG with its handles in the metadata."""
    vals = sample_curve(handles)
    row = bytes(int(round(v * 255.0)) for v in vals)
    img = Image.frombytes("L", (SAMPLES, 1), row).resize((SAMPLES, PNG_ROWS),
                                                         Image.NEAREST)
    info = PngImagePlugin.PngInfo()
    info.add_text(META_KEY, ";".join("%.5f,%.5f" % (s, v) for s, v in handles))
    info.add_text("nuTerra.generator", "tools/fog_curve.py")
    os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)
    img.save(path, pnginfo=info)
    return path


def read_handles(path):
    """The handles stored in a curve PNG, or None if it has none."""
    try:
        with Image.open(path) as img:
            txt = (getattr(img, "text", None) or {}).get(META_KEY)
    except Exception:
        return None
    if not txt:
        return None
    try:
        hs = [tuple(float(c) for c in pair.split(",")) for pair in txt.split(";")]
    except ValueError:
        return None
    return hs if len(hs) == len(HANDLE_NAMES) else None


def read_samples(path):
    """Row 0 of a curve PNG as 256 floats, resampled if the width differs."""
    with Image.open(path) as img:
        g = img.convert("L")
        w = g.size[0]
        px = g.load()
        return [px[int(round(j * (w - 1) / float(SAMPLES - 1))), 0] / 255.0
                for j in range(SAMPLES)]


def write_defaults(folder, force=False):
    """Write the three default curves; existing files stay unless force."""
    written = []
    for k in range(N_CURVES):
        p = curve_path(folder, k)
        if force or not os.path.exists(p):
            write_curve(p, DEFAULT_HANDLES[k])
            written.append(p)
    return written


class CurveEditor:
    """A Toplevel with the three curves and five draggable handles.

    Pick a curve, drag its handles, Save. start and end keep their s (0 and 1),
    the middle three keep their order. Save writes the PNG beside the campaths
    and calls on_saved(path).
    """

    W, H, M = 520, 260, 24
    GRAB = 10

    def __init__(self, master, folder, on_saved=None):
        import tkinter as tk
        from tkinter import ttk
        self.tk = tk
        self.folder = folder
        self.on_saved = on_saved
        self.drag = None

        self.top = tk.Toplevel(master)
        self.top.title("Fog falloff curves")
        self.top.resizable(False, False)

        bar = ttk.Frame(self.top, padding=(8, 8, 8, 4))
        bar.grid(row=0, column=0, sticky="we")
        ttk.Label(bar, text="Curve").pack(side="left")
        self.index = tk.IntVar(value=0)
        for k in range(N_CURVES):
            ttk.Radiobutton(bar, text=str(k), value=k, variable=self.index,
                            command=self.load_current).pack(side="left", padx=(6, 0))
        ttk.Button(bar, text="Reset to default", command=self.reset).pack(
            side="left", padx=(16, 0))
        ttk.Button(bar, text="Save", command=self.save).pack(side="left", padx=(6, 0))

        self.canvas = tk.Canvas(self.top, width=self.W, height=self.H,
                                bg="#11141c", highlightthickness=0)
        self.canvas.grid(row=1, column=0, padx=8, pady=4)
        self.canvas.bind("<Button-1>", self.on_press)
        self.canvas.bind("<B1-Motion>", self.on_drag)
        self.canvas.bind("<ButtonRelease-1>", self.on_release)

        self.status = tk.StringVar(value="")
        ttk.Label(self.top, textvariable=self.status, padding=(8, 0, 8, 8)).grid(
            row=2, column=0, sticky="w")

        self.handles = list(DEFAULT_HANDLES[0])
        self.load_current()

    # ---- coordinates -------------------------------------------------------
    def to_px(self, s, v):
        return (self.M + s * (self.W - 2 * self.M),
                self.M + (1.0 - v) * (self.H - 2 * self.M))

    def from_px(self, x, y):
        s = (x - self.M) / float(self.W - 2 * self.M)
        v = 1.0 - (y - self.M) / float(self.H - 2 * self.M)
        return min(1.0, max(0.0, s)), min(1.0, max(0.0, v))

    # ---- file --------------------------------------------------------------
    def path(self):
        return curve_path(self.folder, self.index.get())

    def load_current(self):
        p = self.path()
        hs = read_handles(p) if os.path.exists(p) else None
        if hs is None:
            self.handles = list(DEFAULT_HANDLES[self.index.get()])
            self.status.set("%s - not saved yet, showing the default"
                            % os.path.basename(p))
        else:
            self.handles = hs
            self.status.set(os.path.basename(p))
        self.draw()

    def reset(self):
        self.handles = list(DEFAULT_HANDLES[self.index.get()])
        self.status.set("default - not saved")
        self.draw()

    def save(self):
        p = write_curve(self.path(), self.handles)
        self.status.set("saved %s - Reload Cam Path in nuTerra to see it" % os.path.basename(p))
        if self.on_saved:
            self.on_saved(p)

    # ---- mouse -------------------------------------------------------------
    def on_press(self, e):
        best, best_d = None, self.GRAB * self.GRAB
        for i, (s, v) in enumerate(self.handles):
            x, y = self.to_px(s, v)
            d = (x - e.x) ** 2 + (y - e.y) ** 2
            if d <= best_d:
                best, best_d = i, d
        self.drag = best

    def on_drag(self, e):
        i = self.drag
        if i is None:
            return
        s, v = self.from_px(e.x, e.y)
        n = len(self.handles)
        if i == 0:
            s = 0.0
        elif i == n - 1:
            s = 1.0
        else:
            # Keep the order: a handle may approach its neighbours but not
            # pass them, or the curve would fold back on itself.
            lo = self.handles[i - 1][0] + 0.01
            hi = self.handles[i + 1][0] - 0.01
            s = min(hi, max(lo, s))
        self.handles[i] = (s, v)
        self.status.set("%s  s %.2f  level %.2f" % (HANDLE_NAMES[i], s, v))
        self.draw()

    def on_release(self, _e):
        self.drag = None

    # ---- paint -------------------------------------------------------------
    def draw(self):
        c = self.canvas
        c.delete("all")
        for q in (0.0, 0.25, 0.5, 0.75, 1.0):
            x0, y0 = self.to_px(q, 0.0)
            x1, y1 = self.to_px(q, 1.0)
            c.create_line(x0, y0, x1, y1, fill="#2c3242")
            x0, y0 = self.to_px(0.0, q)
            x1, y1 = self.to_px(1.0, q)
            c.create_line(x0, y0, x1, y1, fill="#2c3242")
        c.create_text(self.M, self.H - 6, text="bulb", fill="#8a93a6", anchor="sw")
        c.create_text(self.W - self.M, self.H - 6, text="range", fill="#8a93a6", anchor="se")

        vals = sample_curve(self.handles)
        pts = []
        for j, v in enumerate(vals):
            pts.extend(self.to_px(j / float(SAMPLES - 1), v))
        c.create_line(*pts, fill="#4ab3d8", width=2, smooth=False)

        for i, (s, v) in enumerate(self.handles):
            x, y = self.to_px(s, v)
            r = 6
            if i in (0, len(self.handles) - 1):
                c.create_rectangle(x - r, y - r, x + r, y + r, fill="#ffd9a0", outline="")
            else:
                c.create_oval(x - r, y - r, x + r, y + r, fill="#ffd9a0", outline="")
            c.create_text(x, y - 12, text=HANDLE_NAMES[i], fill="#d7dce6", anchor="s")


def main(argv):
    force = "--force" in argv
    args = [a for a in argv[1:] if not a.startswith("--")]
    if args:
        folder = args[0]
    else:
        sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
        import cam_path as cp
        folder = cp.campath_dir()
    for p in write_defaults(folder, force=force):
        print("wrote", p)
    print("curves in", folder)


if __name__ == "__main__":
    main(sys.argv)
