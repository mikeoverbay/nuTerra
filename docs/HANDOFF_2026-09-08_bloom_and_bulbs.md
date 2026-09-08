# Handoff — bulb sprites, emissive panes, and the bloom chain

2026-09-08, on `master`, the session after `HANDOFF_2026-09-06_bulb_placer.md`.
Last commit is `8afd1904`.

**Committed 2026-09-08 evening**, in the split §5 proposed, with the pane and
bulb commits merged because their globals and sliders share hunks:
`9e200e0e` texture views · `f9fccf71` texture formats · `8af6cf34` street
lamps · `75f8a0f6` lamp fog · `348c7414` FX glow · `54264225` output dither ·
`75005e11` smoke cards · then this handoff with its companions. Not pushed.
The smoke-card commit is the evening's follow-on; see `FX_PIPELINE.md`
"Card lighting and the size-relative soft fade".

Claims are marked. **VERIFIED** means observed in a run or read straight off
the code cited. **REASONED** means derived from the code but not seen on
screen. The owner drives the display; the assistant does not take screenshots.

---

## 1. What is already committed

`c48afbe5` shipped the street-lamp glass work — `TextureEngine/TexturePatch.vb`,
`assets/lamp_glass.ntpx`, `tools/make_lamp_patch.py`, the alpha cutout at
threshold 63, double-sided, and the Street lights / Path lights switches. That
part is done and pushed.

## 2. What this session settled

### 2.1 The texture viewer showed a font atlas — VERIFIED fixed

`make_opaque_view` took the view format as a parameter defaulting to `Rgba8`,
and `gFX_BloomA` is `Rgba16f`. `glTextureView` only permits a view inside the
source's own format class, so 32-bit over 64-bit was illegal.

The failure is silent in three stages, which is why it looked like a texture
bug rather than a format bug:

1. `glTextureView` raises `INVALID_OPERATION` and leaves the `GenTextures`
   name with **no object behind it**.
2. The alpha swizzle then fails on that same name.
3. ImGui binds an invalid name; the bind is a **no-op**, so the previously
   bound texture stays live — ImGui's own font atlas.

Fixed by making the view **ask the source**: `GLTexture` now records what
`Storage2D`/`Storage3D` allocated in `storage_format`, and `make_opaque_view`
reads it. There is no longer a parameter to get wrong. Also added
`delete_view()` — the two FX views were never being deleted, leaking two
texture names per resize.

**VERIFIED**: 4 GL errors before, 0 after.

### 2.2 The bloom flickered under camera motion — VERIFIED fixed

`gFX_HDR` was created **Nearest** (`FBO_main.vb`), while `fx_bright.frag`'s
header comment claimed *"the source is sampled Linear, so this doubles as the
downsample."* It did not. At `BLOOM_DIV = 4` the bright pass read **1 source
pixel in 16** and discarded the rest.

A bulb core a few pixels across either landed on that one sampled pixel or
missed it. The threshold below is a hard subtract with **no soft knee**, so
hitting it lit the whole halo and missing it extinguished the halo completely.
Camera still, same pixel sampled every frame, steady. Camera moving, sub-pixel
drift, halo strobing.

Fixed in two halves, both needed:

* `gFX_HDR` is now **Linear**. Safe for the other reader — `fx_composite`
  takes it with `texelFetch`, which ignores filter state.
* `fx_bright` does a **real box average**: four bilinear taps at the quadrant
  centres of each block, offset `bloom_div/4` source texels. With the source
  Linear, a tap on a texel boundary returns the average of the two, so each tap
  covers a 2×2 and the four cover all 16 source texels in four fetches. The
  offset comes from a new `bloom_div` uniform rather than a hard-wired 4.

**The diagnostic lesson, worth keeping.** Three wrong turns preceded this
(lamp fog jitter, lamp fog noise anchoring, and before that a reversed-Z
theory). The owner's two observations settled it in one line each:
*"it flickers with fog and lamp shafts off"* and *"HDR is not flickering"*.
Stable input, flickering output, means the flicker is **created** in the stage
between them — and the bloom chain has exactly one stage nothing else in the
frame goes through. Isolate by buffer before theorising about mechanism.

### 2.3 Lamp fog — two real fixes, neither was the flicker

Both landed before the bloom cause was found. They are correct on their own
merits and worth keeping, but they did not fix what they were aimed at.

* **Fixed step LENGTH, not a fixed count.** `dt` was `(t1 - t0) / fog_steps`
  with `t1` clipped to the per-pixel scene surface, so the step length was a
  function of the distance to whatever the pixel looked at. Neighbours across
  a silhouette got unrelated step lengths, and a centimetre of camera movement
  re-spaced all 48 samples rather than moving the last one. Now
  `dt = 2 * lamp_range / fog_steps` with the count varying, plus a
  `if (t > t1) break` guard so the rounded-up tail cannot sample past the
  surface. `fog_steps` is now a count across a full diameter, so the old value
  buys the same peak cost and short chords cost less.

* **World-anchored noise.** The owner diagnosed this one: *"We need a fixed
  point in space to make the fog and not off of cam angle."* `drift_at()` was
  always correct — it maps a world point through `noise_metres` and the shared
  scroll. The problem was **where** it was sampled: four taps at fractions of
  the chord, `t0 + (t1 - t0) * k/3`, with every step lerping between them.
  That lattice both **slid** (a fixed lump of fog read from a different place
  each frame, so the field boiled on any camera motion) and **stretched** (four
  taps always spanning the chord, so a different noise frequency either side of
  every silhouette). Now `drift_at(p)` at the sample's own world position.

  **Cost**: 4 noise fetches per pixel per lamp became up to 48, each a 4-octave
  fbm. Watch the "Lamp fog" GPU timer. **REASONED**: with `fog_noise_m = 250`
  and `noise_scale = 4` the base cell is 7.8 m, so the four octaves are
  7.8 / 3.9 / 1.95 / 0.98 m; step size for a 20 m lamp is 0.83 m, which puts
  octave 4 below the march's Nyquist limit. It can only alias, and aliasing
  flickers. Dropping `shaft_fbm3` to 3 octaves should cost nothing real.

## 3. Found, not fixed

### 3.1 The glow-occlusion test is inverted for reversed-Z

`fx_composite.frag:96` computes `bloom.a - sceneD`, which suppresses when the
**glow** is nearer than the surface. The intent, per its own comment, is to
suppress when the **surface** is nearer than the glow. The engine's convention
is stated in `lamp_bulb.frag:61` — *"REVERSED Z: the engine clears depth to 0
and tests Greater, so NEARER is a LARGER value... nothing nearer is
scene <= mine"* — so the test wants `sceneD - bloom.a`.

**REASONED consequence, not measured.** Unlit texels default to `d = 1.0`
(`fx_bright.frag`, whose comment calls 1.0 "the far plane" — it is the **near**
plane under reversed-Z). After blurring, `bloom.a` sits near 1.0 across the
halo while `sceneD` for anything past a few metres is near 0. With
`FX_GLOW_OCCLUSION = 0.85` and a 0.012 bias, `pass_through` is pinned near
**0.15 essentially everywhere** — the bloom runs at about 15% and
`FX_GLOW_STRENGTH = 2.0` is partly paying for it.

Left alone deliberately: correcting the sign makes the halo roughly 6.7×
brighter and wants the strength re-tuned in the same change. Instrument it
before touching it.

### 3.2 The FX and the fog fight

Full write-up in **`docs/fx_fog_interaction.md`** (new, this session), with
both routes and their costs. Headline: the fog computes its amount from
`gPosition`, no FX card writes `gPosition`, and `composite_fx()` folds the FX
into `gColor` **before** the fog pass runs — so smoke is fogged by the distance
of whatever solid surface is behind it. The `gFX_cover` term in
`DeferredFog.frag:194` is one knob choosing between two wrong answers, and
additive FX (fire, lamp panes, bulb sprites) carry **alpha 0** by contract so
they get no coverage at all and take the full background fog.

Expands the existing `open_threads.md` §9 item.

### 3.3 The 8-bit blur targets were lost in a revert

`make_bloom_target` allocates **Rgba16f** again. The owner had asked for RGBA8
blur targets; that change was never committed and a `git checkout` during an
unrelated revert took it. `fx_bright` and `fx_composite` still carry the
`glow_range` encode/decode that was its 8-bit companion — harmless in float
(divide then multiply back) but inconsistent with the stated intent.

`FX_GLOW_RANGE = 4.0` and the encode are in the tree; only the target format
is missing.

## 4. Open, in the order it bites

1. **Commit the tree.** 847 lines uncommitted is the biggest risk here; a
   second accidental revert would cost the session (§3.3 is what one already
   cost). Split proposed below. **Done** - see the banner at the top.
2. **The occlusion sign** (§3.1) — measure `pass_through`, fix, re-tune
   `FX_GLOW_STRENGTH` in the same commit.
3. **Blur targets back to Rgba8** (§3.3) — one line in `make_bloom_target`;
   the shader side is already there.
4. **FX vs fog** (§3.2) — pick route A or B from `fx_fog_interaction.md`.
5. **`shaft_fbm3` to 3 octaves** (§2.3) if the Lamp fog timer moved.
6. **`shaft_density = 0`** in the monastery settings still removes all
   extinction — `trans` and `to_lamp` both evaluate to 1.0, so the march never
   damps and never early-outs. Not a bug, but it is the most expensive and
   least stable setting available.
7. **Never verified**: bulb sprite occlusion with a lamp behind a wall.
8. `LookupBySuffix` still ignores res_mods — deliberate, noted.

## 5. Proposed commit split

Five commits, smallest blast radius first:

| # | files | what |
|---|---|---|
| 1 | `OpenGL/GLTexture.vb`, `FBOs/FBO_main.vb` (view parts) | texture views derive their format from the source; delete all four |
| 2 | `TextureEngine/TextureMgr.vb`, `MapLoader.vb`, `PrimitiveLoader.vb` | DX10 / BC7 / BC6H / BC4 / BC5 / ATI1 / R32F loading |
| 3 | 4 new `model_lamp*` shaders, `MapStaticModels.vb`, `cull.comp`, `common.h`, `ShaderLoader.vb` | emissive lamp panes in their own bucket |
| 4 | `lamp_bulb.frag/vert`, `modRender.vb` (bulb parts), `modGlobalVars.vb` | the bulb sprite and its star glare |
| 5 | `fx_bright.frag`, `FBO_main.vb` (filter), `modRender.vb` (uniform), `modGlobalVars.vb` (`FX_GLOW_RADIUS`) | the bloom: box-average downsample and the 1-texel kernel step |

Keep 5 separate — it is the flicker fix and the banding fix, and both are the
kind of thing that gets bisected later.

`nuTerra/cam_paths/19_monastery.campath` is modified and **belongs in the
commit** with the bulb work; it carries the placed bulbs.

---

## 6. Evening session — banding on smoke

Appended 2026-09-08 evening, handing to Fable. **Committed since** (see the
banner at the top);
this session added to the same uncommitted tree (`DeferredFog.frag`,
`fx_composite.frag`, `MapFog.vb`, `modGlobalVars.vb`, `Window.vb`), so the
split in §5 now needs a sixth entry for it.

### 6.1 Two false starts, recorded so they are not repeated

**A light nobody could find.** Time went into hunting "hard wired lights or
bulbs" in code. There are none, and this is now established rather than
assumed: `MapCamPath.lights()` has exactly two writers — the file reader at
`MapCamPath.vb:713` and `ExpandBulbs` at `:574` — `path_lights` and `bulbs`
are only ever assigned from the file or emptied, and the only other
`New CamBulb` is the placer's drag-handle placeholder, which reaches nothing
until Save. The light in question was authored data: **a bulb record on
`Fire_monastery_big` in `19_monastery.campath`**, pure red `(1,0,0)`, DUAL_COWL,
level 0.14 — colour and kind both suggest a saved debug-red session copied off
a lamp rather than an authored fire. Left alone on the owner's instruction
("none from files").

**A dither in the wrong pass.** See §6.3. It broke transparency, and the fix
for that broke the FX entirely for one build. Both are corrected.

### 6.2 Where the banding is NOT — VERIFIED

Owner: smoke clean in the **gFX_HDR** viewer, banded on screen. Isolating by
buffer, the same move that settled the flicker in §2.2:

* **gFX_BloomA has no smoke in it at all.** The bloom cannot be the source,
  and §3.1's occlusion smoothstep is ruled out for this symptom.
* `msm_blur` is a plain linear 9-tap over all four channels. Clear.
* `colorCorrect.frag` is a 16-step 3D LUT and would band beautifully — but
  `color_correct()` is **commented out** at `modRender.vb:481`. Not in the
  frame. Do not chase it.

Why BloomA is empty is itself a change from §2.2's fix: the new box average
lowers what reaches the hard `FX_GLOW_THRESHOLD = 0.42` subtract, so smoke
that used to clear it on its brightest pixel now averages below, and
`e / glow_range` divides what survives by 4. The light smoke glow the 0.42
floor was chosen to produce is currently gone. Not necessarily wrong, but it
is not what §2.2 intended.

### 6.3 What landed — `FX_DITHER`

Sub-LSB triangular-PDF dither on the final `gColor` write in
`DeferredFog.frag`, slider **Output dither** (Settings → Section Visibility),
default 1.0 LSB, 0 = off. Full write-up in `FX_PIPELINE.md`.

**The reason it is in the fog pass and not `fx_composite` is load-bearing.**
The composite BLENDS premultiplied colour (`One / OneMinusSrcAlpha`), so a
dither on its rgb breaks `rgb = colour × coverage` and lands as additive noise
on every pixel the FX never covered — that is what "something got broke in how
it handles transparency" was. Scaling by `fx.a` fixes the algebra and
under-dithers thin smoke, which is the case that bands. The fog pass writes
`gColor` outright and owns the pixel. `fx_composite.frag` now carries a
comment saying so.

### 6.4 The open question, and the cheap way to settle it

**REASONED, not measured:** the more likely cause may be candidate 2, not the
dither's target. `DeferredFog.frag:163` classifies sky by `dist < 0.001` on
`gPosition`, and **FX cards never write `gPosition`** — so smoke against the
sky is classified as sky, takes `f = fog_sky`, and is mixed toward
`fog_tint_ovr`. The owner's own read was *"almost like sky is being mixed in"*,
and the code agrees. `gFX_cover` (line 210) is the only brake, and it is
weakest exactly where it matters: thin smoke has low alpha, additive FX carry
alpha 0 by contract so fire takes the full sky fog, and the amount then tracks
the smoke's own alpha ramp — which would put the contours where they are seen.
This is §3.2 / `fx_fog_interaction.md` surfacing as a visible artefact.

**Do this first, it needs no rebuild:** drag `fog_level` (or `fog_sky`) to 0
with a plume on screen. Bands gone → it is the fog/sky misclassification, and
`FX_DITHER` is treating a symptom. Bands survive → it is the quantiser.

### 6.5 Loose ends from tonight

* **No still was ever captured.** Two attempts left nothing on disk;
  `C:\nuTerra_ScreenCaps\still` still holds only a Sep 5 file. Check the log
  for `record: still saved -` before trusting the Still button.
  *Correction, later that evening:* they were captured, to
  `G:\nuTerra_ScreenCaps\still` — `record_dir` in the user settings points at
  G:, not C:. The button works; the folder was wrong.
* **`fx sort: order changes since last snapshot`** read **3886** at the 14:09
  snapshot and **36** at 14:50 from a different camera. Alpha-blended cards are
  order-dependent; a number that size means overlapping cards are re-sorting
  under camera motion. It will not band, but it will shimmer. Unexamined.
* The smoke is **alpha-blended cards, not a volumetric march** — `volumetric.*`
  is the game's material name. The only real march in the renderer is
  `lamp_fog.frag`. Worth knowing before anyone reads the filename and plans
  around it.
* `modGlobalVars.vb` carries **duplicated and contradictory doc comments** in
  the uncommitted lamp/glow block — two `<summary>` tags on `LAMP_PANE_GAIN`
  and on `LAMP_BULB_SPIKE`, `LAMP_BULB_GLARE` left with none, and stale prose
  above `FX_GLOW_RADIUS` / `FX_GLOW_PASSES` still describing 2.7 and SIX when
  the values are 1.0 and 2. Cosmetic, but it will produce XML-doc warnings and
  it contradicts the committed values. Not touched — it is the owner's WIP.
