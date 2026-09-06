# Handoff — the fog: global fog rebuilt, shafts curved, monastery tuned

Covers `61bf28a6` .. `3d476543`, all on `master`, 2026-09-06, the session after
`HANDOFF_2026-09-06_lights.md`. Everything visual here was judged from
headless stills taken from the owner's saved camera; every number came from a
capture or a code read, and the code reads say so.

---

## 1. What shipped

| commit | what |
|---|---|
| `61bf28a6` | docs reconciled with the tree; four lights-handoff open items settled from the code |
| `59e162ad` | menu bar no longer drawn over the map picker |
| `f6da9047` `3a1fc537` | `volumetric_fog.md` (literature) and `volumetric_fog_audit.md` (what the shaft code got wrong) |
| `c7382c36` | shaft fixes: extinction on every step; shaft honours the Lamp Shadows box; FrontFace set, not assumed; ClearColor restored by the Bulb Placer; fog box culling |
| `3eecc170` | per-light falloff CURVES, `VM_FOG_Curve_0..2.png`, editor in Path Studio, campath light record 32 → 36 bytes |
| `a845322a` | Path Studio light controls in their own right-hand panel |
| `251a27c1` | **global fog rebuilt**: distance + height from gPosition, tint / density / height / floor / noise settings |
| `0b511ca3` | shaft gain, density, phase, steps, light gain, lamp shadows persisted per map |
| `f4b9d644` `19a16aee` | 19_monastery tuned to the reference |
| `32de267a` | fog no longer wipes smoke and fire over the sky |
| `b009855b` | fog as a full-screen quad; sky keeps showing through (`fog_sky`) |
| `90004dfc` | camera ground clamp stops the pitch, not just the eye |
| `026e1060` `cf5e6968` `c4bba61e` `1657e8f1` `3d476543` | fog noise: shared with the shafts, settable size, 3D, path-averaged, fixed-step march |

### The global fog was not fogging

`DeferredFog.frag` read its amount from the frame's alpha. That alpha travels
through the window's back buffer, requested with `AlphaBits = 0`, and arrives
as 1.0. **Measured: fog_level 0.55 and 1.0 rendered the same frame** — a flat
gamma lift with no depth in it. The "cloudy patchiness" was a fractal cloud
multiplied by six and mixed in as colour. Everything the owner had been doing
with light gain was compensating for fog that was not there.

It now computes its own factor from gPosition:

```
f  = sky ? fog_sky : 1 - exp(-fog_density * dist)
f *= exp(-max(0, y - floor) / fog_height)          floor = map MEAN + fog_floor
f *= mix(1, 0.4 + 1.2 * noise, fog_noise)          noise: see below
f *= 1 - FX coverage                                smoke keeps its own look
out = mix(frame, tint, clamp(f * fog_level))
```

drawn as a **full-screen quad** (the old map-sized box only fogged what it
covered on screen, so the outland could escape), in display space (both inputs
are already tonemapped), with the tint the map's own unless overridden.

### The noise, four lessons in one afternoon

1. 2D over the ground plane → walls striped vertically (every point up a wall
   shared one value). **3D value noise**, four octaves, trilinear.
2. Sampled where the ray ends → the field painted onto the walls as mottle.
   **Average along the ray.**
3. Averaged at fixed FRACTIONS of the path → swam with every camera move and
   zoom, because the samples slid through the field. **Fixed 10 m strides
   anchored at the surface**, up to eight: a sample never moves, it is only
   added or dropped at the near end.
4. The shafts sample the same field, same scroll, same scale (`fog_noise_m`),
   at four points along each lamp's chord, so beams and banks drift together.
   Cost stays under the shadow compares already in the march.

The base cell is `fog_noise_m / 32` metres — the number reads larger than the
cells it makes, a leftover of the 2D field's scaling kept so the tuned map did
not move. Rename or rescale when convenient; both shaders use the same mapping.

### Curves instead of one falloff number

One analytic falloff could not lengthen a shaft without brightening its core.
Each lamp now names curve 0, 1 or 2 — a 256-sample row in
`nuTerra/cam_paths/VM_FOG_Curve_<n>.png` — and the shader samples it by
`s = dist / range`. Path Studio has the editor (five draggable handles: start,
two transitions, falloff start, end pinned to zero; monotone cubic between).
Handles live in the PNG's text metadata so the editor re-opens them. The
campath light record grew to 36 bytes with a `uint32 curve`; both readers go by
`light_stride`, so old files read as curve 0. nuTerra re-reads the curves on
the same Reload Cam Path that moves the lamps (`MapCamPath.load_gen`,
`MapLampFog.ensure_curves`).

### 19_monastery, as shipped

| setting | value |
|---|---|
| fog_level / fog_density / fog_height / fog_floor | 0.95 / 0.05 / 200 / 0 |
| fog_sky / fog_noise / fog_noise_m | 0.8 / 0.8 / 50 |
| fog tint | 0.45, 0.28, 0.18 |
| tonemap_exposure / sun_strength / ambient / brightness | 1.0 / 0.2 / 0.10 / 0.6 |
| shafts: gain / density / phase / steps | 1.5 / 0.03 / 0.55 / 48 |

The shipped file, its `bin` copy and the work copy in
`%TEMP%\nuTerra\MapSettings` are identical. It carries the owner's live tuning
it was built on: **FXAA off**, tessellation on, the SH grid values.

---

## 2. How the tuning was done — reproduce it

`tools/still_from_snapshot.py <run_name> [key=value ...] [--arg <nuTerra arg>]`

Reads the camera from `%TEMP%\nuTerra\snapshot.txt` (the owner's last
Snapshot), writes the work settings file from the shipped one plus the
overrides, launches `nuTerra.exe <map> cam=... still=1 out=<scratch>`, waits
for `still_000.png`, kills the process. 26 s a run. It never uses `snap` or
`snapquit` — **those overwrite the owner's snapshot**. It DOES overwrite the
work settings copy; copy the shipped file back when done (the script prints
the command).

Read the result with the image reader and send it to the owner; judge against
the reference; change one thing; repeat. Twelve runs got monastery from a
blown-out sunset to the reference.

---

## 3. Open

- **Shaft colour space** (audit item 2). The shaft is added after the tone
  curve into the 8-bit back buffer; the pool is added before. Faint shafts stay
  faint, so gain gets pushed. The fix is to add the shaft before `correct()`
  through a small half-float target — one design decision, not yet taken.
- **Shafts vs global fog** (audit items 8, 9). Separate media: a shaft is never
  dimmed by the global fog between eye and lamp. Scaling each shaft by the
  global factor at its distance is the small version.
- **Density only dims** (audit item 7) — texts corrected, model not.
- **Bias grows as t²** (item 11), **cube baked from LOD 1** (item 12): unverified.
- Smoke is exempt from fog by coverage, so distant smoke reads too clear; the
  proper fix fogs it by emitter distance.
- The height fog inside `deferred.frag` (~1480-1510, hard-coded 0.005) still
  runs before the tonemap when `fog_level > 0`. Weak, pre-tonemap, and
  redundant now; remove or fold in.
- `fog_noise_m` is 32× the cell it makes. Rename or rescale.
- Path Studio's left column is 952 px tall; the notes block is next to move.
- The owner's uncommitted `19_monastery.campath` and edited `VM_FOG_Curve_0.png`
  are theirs; `docs/fog-cube-01.jpg` is an untracked reference image.

---

## 4. Rules that earned their place today

**Measure the null control first.** Fog 0.55 and fog 1.0 rendered the same
frame. That one comparison found the whole problem; hours of gain tuning had
not.

**Every camera-dependent sample position swims.** Fractions of a path, the
path's end point, anything relative to the eye: the field slides through it.
Anchor samples in world space and only add or drop at the ends.

**A 2D field has no vertical axis.** It looks fine on the ground and stripes
every wall.

**The runner overwrites the work settings.** Restore the shipped file after a
capture session or the owner launches into the last experiment.

**Work in `C:\nuTerra` on `master`.** The worktree the session opened in cost
three rounds of "still broken" while Visual Studio rebuilt `master` without the
fix. The owner: "we have git, we don't work in a private reserve."

**Kill nuTerra before building.** It holds the exe, and MSBuild fails ten
retries later with an error that looks like a compile problem.

**Relaunch monastery at the owner's camera after every build.** Standing
request. `nuTerra.exe 19_monastery cam=<snapshot line>`.

**The owner pushes. The agent never does.**

---

## 5. Later the same day: Path Studio bakes and shows the map

Two additions to `tools/path_studio.py`, both pure Python, no nuTerra involved.

**Bake terrain (Python).** `tools/terrain_bake.py` reads a map that nuTerra has
never opened straight from its pkg - `spaces/<map>/<xxxx><yyyy>o.cdata_processed`,
one zip per 100 m chunk, `terrain2/heights` = 36-byte header + 69x69 RGBA PNG of
int32 millimetres - and writes the same four bake files MapFlightBake does, with
`source=python-terrain` in the meta. TERRAIN ONLY: top equals floor, the mask is
empty, no models or trees. The chunk-to-world mapping was SEARCHED, not derived:
every orientation, mirror, span and offset rasterised and compared with nuTerra
own floors; the winner (X mirrored per chunk and per map, 69 samples over
106.25 m with a two-sample margin) plus the water raise (BWWa bodies out of
space.bin, rectangles lifted to their surface as MapFlightBake does) matches
nuTerra to 0.00-0.04 m RMS on every map that has a real bake. The residual
before the water raise WAS the water: dry maps matched to 0.00 m, wet ones did
not. The button never replaces a nuTerra bake without asking; if one was, open
the map in nuTerra and it writes the real one back. Game path comes from
nuTerra user.config (`GamePath`).

**global_AM underlay.** Checkbox plus blend slider on the left panel. The pkg
global_AM.dds (4096 DXT5, Pillow decodes it) is resized to the bake grid and
flipped VERTICALLY - scored, not assumed: only that flip puts the obstacle mask
on the AM high-frequency detail (gradient ratio 1.25 against under 1.0 for the
other three). Open ground shows the AM; obstacle cells keep the mask colours.

Verified headlessly: Himmelsdorf baked and loaded from nothing; monastery
composite shows the orange obstacles sitting on the AM town.

## 6. The smeared rock: the mixer's macro combine

The owner's rock at `cam=-23.3694,3.495,-0.7712,...` read as a blur next to
crisp cobbles. Cause in `t_mixer.frag`: `mix(micro, macro, influence)` on both
albedo and normal, with the influence read from `L.r2.x` (the macro
displacement scale) - Rock_4 came out 80 to 100 percent macro, at 5 cm per
texel. The game adds `influence * max(macro - mean, 0)` on top of the full micro
and sums the normals. Transcribed from `terrain2_5` blob 05 and the VT baker
blob 13; the field map and formulas are in `terrain_blend.md`, "Macro, normal
and global map". The global AM term was rebuilt to the game's height-gated
deviation form at the same time. Rebuild VT picks it up; nothing outside the
shader and the docs changed.

Observed, not chased: `run.log` fills with `GL Error InvalidValue` every frame
once the page atlas reports full. It predates this change (a shader edit cannot
raise a GL API error) but nobody has looked at it.
