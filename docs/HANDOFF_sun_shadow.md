# Handoff — terrain colour, layer mixing, and the sun shadow

Supersedes the previous version of this file, which described an architecture
that no longer exists. Where it says "was", that is what the old document
described and what the code actually did.

The owner is the sole developer of nuTerra, a VB.NET / .NET 6 / OpenTK offline
World of Tanks map viewer. He guides tightly, tests every build himself, and is
usually right when he says something looks wrong. Believe the screenshot over
the reasoning.

---

## How to work in this repo

- **Kill the running exe before building.** `Get-Process nuTerra | Stop-Process -Force`.
- Build: `MSBuild.exe nuTerra.sln /t:Build /p:Configuration=Debug /p:Platform="Any CPU"`
  from `C:\nuTerra`. Publishing needs `/p:Platform=x64` (the C++ project fails on
  `Debug|Win32` — pre-existing, unrelated).
- **Shaders only validate at runtime.** A clean build proves nothing about GLSL.
  Launch and check stdout for `Shaders Built.` and `didn't compile`.
- **Run with the working directory set to the bin folder**, not the repo root.
  `ShaderLoader` resolves `shaders` relative to the CWD, so launching from
  `C:\nuTerra` dies on `C:\nuTerra\shaders does not exist` before the window opens.
- Redirect stdout to a file and read it. The log is the primary diagnostic.
- **Console output is buffered when redirected.** A line logged mid-session can
  sit unwritten indefinitely. The **Snapshot** button on the menu bar writes the
  current render state and calls `Console.Out.Flush()`, which is the only reason
  the log is readable while the app is still running.

---

## Engine-wide state that breaks naive code

Set **once at startup**, global, and every one of them has caused a bug here.

| where | what | why it matters |
|---|---|---|
| `Forms/Window.vb:176` | `GL.ClipControl(LowerLeft, ZeroToOne)` | Clip-space z must be **0..1**, not −1..1. OpenTK's `CreateOrtho*`/`CreatePerspective*` produce −1..1. Any projection matrix you build must be remapped. |
| `Forms/Window.vb:179` | `GL.Enable(DepthClamp)` | Geometry outside the depth range is **clamped, not clipped**. A wrong projection silently flattens to 0.0 instead of disappearing — it hides the error. |
| `Forms/Window.vb:182` | `GL.ClearDepth(0.0)` + `DepthFunc.Greater` | Reversed-Z. If you change either for a local pass, restore **both**. Leaving `ClearDepth` at 1.0 makes the whole scene vanish behind the sky. |

One more, which cost most of a session:

> **`gPosition` holds VIEW space, not world space** — despite every writer naming
> the varying `worldPosition`. It is `view * model * vertex` in `TerrainLQ.vert`,
> `model.vert` and `tree.vert` alike. `deferred.frag` compensates with `invView`.
> Do not trust the name; pass `invView * Position` when you need world.
>
> `t_mixer.vert`'s `worldPosition` **is** genuine world space — different pass,
> no camera in it.

---

## Shadow architecture

Two halves of one system, split by **what moves**:

| | casts | receives | where |
|---|---|---|---|
| Live cascades | **trees only** | everything | `MapScene.ShadowMappingPass` |
| Map-wide bake | terrain + static models | everything | `MapSunShadow.Bake` |

Trees stay live because they will be animated, and nothing that moves can live in
a bake that happens once. Everything static is in the bake, which reaches the
whole map instead of stopping at the last cascade.

- Cascade splits are **20 / 75 / 250** (halved from 40/150/500 once the cascades
  no longer had to cover buildings). These MUST match `cascadePlaneDistances` in
  `shaders/common.h` — the shader picks its cascade with them, so if the two
  drift apart it samples the wrong map.
- The bake is sampled **per frame in `deferred.frag`**, not baked into the VT
  page. It costs four taps per lit pixel and is folded into the same
  `sun_shadow` factor as the cascades, so both attenuate the sun term only.
- Strength controls, both saved per map: **Live strength (trees)** =
  `shadow_strength`, **Baked strength (terrain/models)** = `horizon_strength`.
- **Penumbra clip lo/hi** reshapes the transition after filtering and before the
  light sees it, on **both** paths so an A/B compares the filtering only.
  `smoothstep`, never `step` — a hard cut discards the sub-pixel gradient and
  re-aliases the edge.

### Why it is sampled in the final render

It **was** sampled while VT pages were built and folded into terrain albedo as
`gColor.rgb *= horizon_shade`. That was wrong three ways:

1. Albedo feeds the **ambient** term as well as the direct one, so it darkened
   the sky fill that should still be present in shade.
2. A VT page only covers terrain, so a static model standing in a building's
   shadow received nothing at all.
3. A page is baked once, long before anything is drawn on top of it, so the
   shadow landed **ahead of the projected decals**.

Sampling in `deferred.frag` puts it after all three. As a side effect, toggling
the bake no longer forces a VT atlas rebuild — the old comment claiming "it only
enters a page at bake time, so there's no cheaper way" is no longer true.

### Moment Shadow Maps (A/B toggle)

**Settings → Shadow Mapping → Moment shadow maps (A/B)**. Peters & Klein 2015.

The point is that moments are **linear**. A depth comparison must be compared
before it is averaged, so a depth map can be neither blurred nor mipmapped, and
PCF must spend taps every frame. Moments can be both — once, at bake time — so
sampling is a single trilinear fetch plus an LDLᵀ solve.

- `sun_depth_*.frag` write `vec4(z, z², z³, z⁴)` unconditionally; with MSM off
  the FBO has `DrawBuffer = None` and the writes are discarded.
- `filter_moments` runs a separable 9-tap Gaussian then `GenerateTextureMipmap`.
- Capped at `MSM_MAX_SIZE = 4096`, RGBA32F, ~341 MiB with mips.
  **32F is a prototype choice** — 16F halves it, the paper's 4×8 quantisation
  quarters it again. Neither is worth debugging until the method is proven.
- **Light leaking is the failure mode.** Test a building casting onto ground some
  distance behind it. `Penumbra clip lo` is the knob.
- **Flat open ground** is the other risk: the Hankel matrix is singular where
  depth is constant. `moment bias` is the knob.

---

## Terrain layer mixing

`t_mixer.frag` reimplements the game's terrain shader. The algorithm, confirmed
against the shipped one:

```
w_i   = splat_i * layerMask_i,  normalised over all 8
hw_i  = max(h_i, 1/255) * w_i
peak  = max(hw_i) over all 8
c_i   = max(hw_i + 0.05 - peak, 0) * w_i      <- note w_i appears twice
result = sum(c_i * albedo_i) / sum(c_i)
```

It is a **threshold, not a crossfade**: only layers within the band of the
tallest contender contribute at all, everything else is culled to zero. That is
what makes a transition follow the height maps instead of smearing.

Two fixes that produced the visible colour improvement:

1. **`blend_height` was fed the map-authored BWT2 value (~0.3).** The game
   thresholds against a **hardcoded 0.05**. Six times too wide: since the `w_i`
   sum to 1 the contenders are small numbers, so a 0.3 band admitted nearly every
   painted layer, all eight contributed, and the result averaged toward mud.
   Now `TCommonProperties.GAME_BLEND_HEIGHT = 0.05F`.

   The authored value cannot be the game's source either — `g_blendGlobalThreshold`
   lives at `cb0[360]` and that shader declares only `cb0[351]`, so it is out of
   reach. Whatever BWT2/`blendHeight` drives, it is not this. It is still loaded
   into `blend_height_authored` and shown in the panel marked `(unused)`.

2. **Normals were blended across all 8 layers.** The game runs an **argmax** over
   the blend weights and takes a single normal sample from the winner — albedo
   blends, normals do not. Averaging eight normal maps pulls them all toward the
   mean, which is flat, so relief vanished exactly where two textures met.

Other confirmed details, for whoever works on this next:

- **Macro is the same layer at 1/8 scale** — `frac(uv)` vs `frac(uv/8)`, not a
  separate texture set.
- `g_vtTileParams.w` is the macro fade (our `macro_fade`); `.x`/`.y` are the
  micro/macro sample LODs.
- The **macro blend set has no height threshold** — plain weighted average. Only
  the micro set is culled. The two are lerped by the macro fade, so transitions
  soften with distance for free.
- Blend maps carry **two weights per texture, in `.a` and `.g`** — the two
  highest-precision channels in BC3. Four textures, eight layers.
- `layerMask` multiplies **before** normalisation, so masking a layer
  redistributes its weight rather than darkening the pixel. Our `active_layers`
  stand-in is not quite the same operation — an open item.
- `height_contrast` is **ours, not the game's**. 1.0 is game behaviour; below 1
  lifts mid heights and works against the threshold.

---

## Per-map settings

`modMapSettings` saves one text file per space. **A missing key now falls back to
the global default**, which was not previously true.

`Load` only applies keys a file contains, and these values live in module state
that outlives a map — so an absent key used to inherit **the previous map's
value**. Open a tuned map then an untuned one and the first map's lighting
carried across. The visible symptom was that adding any new setting appeared to
require appending it to all 65 files.

Fixed properly:

- `CaptureDefaults()` runs once at startup, after `CommonProperties.Init()` and
  the `DONT_BLOCK_*` flags.
- `ResetToDefaults()` runs at the **top of `load_map`, before
  `get_environment_info`** — so map data from `environment.xml` and `space.bin`
  is applied after the reset and still wins (`blend_height` from BWT2 keeps its
  authored value), and the saved file is applied last and overrides both.

---

## Bugs found and fixed — do not reintroduce

1. **`Bake()` ran before `set_light_pos()`.** The bake was at `MapLoader.vb:444`,
   `set_light_pos` was the last statement of `load_map` at `:475`. On the first
   map of a session `LIGHT_POS` is zero, `.Normalized()` gives NaN, the view
   matrix goes all-NaN, every vertex is NaN, nothing rasterises, and the depth
   map comes back exactly as cleared.

   **The tell was in the log the whole time:** `expected~NaN`. That field is
   `(centre · sun_view_proj).Z / .W` — a merely mis-aimed camera gives a finite
   wrong number, so NaN can only come from a NaN matrix. On a second map load it
   was finite but stale: the previous map's sun. That is what "you look from
   point is wrong" was.

   `set_light_pos` is **not idempotent** (it flips `LIGHT_ORBIT_ANGLE_Z` off its
   own previous value), so it was moved, never duplicated.

2. **The bake's centre was half a chunk out in both axes.** `PageLoader.LoadPage`
   uses `xMin = 100 * b_x_min` but `yMin = 100 * (b_y_min - 1)` — genuinely
   asymmetric. Derive the box from those expressions, never by hand.

3. **The ortho box was ~3× deeper than the map**, which left a 16-bit depth
   buffer with about eleven usable bits and the stair-stepped edges to match. It
   is now fitted to the map's silhouette at the current sun angle and squared up
   (a non-square box on a square texture gives anisotropic texels, and the coarse
   axis is what an edge staircases along).

4. **`unbind_textures(7)` leaked units 7 and 8** — it releases `0..count-1`.

5. **The shader include convention is `#include "common.h" //! #include "../common.h"`.**
   The `//!` half is an editor hint; the loader resolves the plain name. Changing
   it to `"../common.h"` breaks it. Every shader in the repo uses this form.

---

## Open items

- **The baked shadow is holding 2 GiB.** `TARGET_TEXEL = 0.05` pins it to
  `MAX_SIZE = 32768`. Measured: 8192 → 16384 changed nothing visible, so the
  sharpness came from moving the sampling out of the VT page, not from
  resolution. `TARGET_TEXEL = 0.25` gets back to 8192 at 128 MiB and very likely
  looks identical. **`TARGET_TEXEL` is the gate, not `MAX_SIZE`.**
- A VRAM budget (`VRAM_BUDGET = 0.25`) steps the size down a power of two at a
  time and logs when it does. An oversized allocation does not fail cleanly, it
  thrashes — which reads as "the baked shadow costs frame time" when it does not.
- **`layerMask` vs `active_layers`** — see the mixing section.
- **Washed-out shade** has a second, separate contributor: shadowed pixels are
  *pure ambient* by construction (`Ambient_level *= (1.0 - direct_light)`, and
  direct is zero), so every ambient control lands exclusively on them.
  `ambient_sat` literally desaturates toward luminance. `G_prefilteredColor`
  (cubemap IBL) is added with no attenuation at all.
- **Shadow acne / bias.** `PolygonOffset(1.5, 4.0)`. The `factor * slope` term is
  format-independent and still works; the `units * r` constant term shrank with
  the tighter depth range, which means less peter-panning for free. If acne
  appears, the factor is the knob.
- **Culling mismatch.** `Bake()` does `GL.Disable(CullFace)` for the whole pass;
  `MapStaticModels.shadow_mapping_pass()` explicitly enables it.
- **`SHADOW_MAP_LOD = Math.Min(1, MAX_LOD_ID)`** (`MapLoader.vb:90`) picks LOD 1,
  but the enclosing loop skips `If lod.junk`. Any model whose LOD 1 is junk emits
  **no shadow command at all** while its LOD 0 renders normally. A specific model
  missing its shadow is probably this.
- **Trees visible through a building** that should occlude them. Reported by the
  owner, never investigated. Depth/G-buffer issue, unrelated to shadows.
- **`t_mixer`'s `worldPosition` varying now has no reader** — the sun shadow that
  used it moved to `deferred.frag`. Left in place because `VS_OUT` and the `in`
  block must match exactly between stages and removing it risks a link error for
  no measurable gain.
- **Map picker shows all installed spaces**, including hangars, comp7 and battle
  royale. Intersecting `scripts/arena_defs/_list_.xml` with the installed set
  would give rotation-only without the two problems that got that approach
  rejected before (unshipped maps offered, event spaces hidden). Cheap proxy:
  the 13 non-rotation spaces are exactly the ones with no
  `gui/maps/icons/map/stats/<name>.png`, and `MapMenuScreen.Init` already does
  that lookup.
- **Diagnostics to remove when done:** the `drew terrain` / `drew models` counts
  in `MapSunShadow`, the `VT bake #N` logging in `PageLoader.LoadPage`, and
  layer-projection logging in `TerrainTextureFunctions.vb`.

---

## One piece of advice

Unchanged from the last version of this file, and it earned its place again this
session: every bug in this feature was found by measuring, and every one of them
survived a round of confident reasoning first. The ordering bug in particular was
printing `expected~NaN` into the log for an entire session while the camera maths
around it was rewritten five times. When the owner says something looks wrong,
instrument it before theorising — and read what the instrument already said.
