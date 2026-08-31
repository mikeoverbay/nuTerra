# Handoff — terrain holes, FX compositing, glow

Session of 2026-08-30/31. All work is on **`master`**, merged from
`particles-cardtest` at `97ef7a7`. Nothing is pushed.

## What landed

| commit | what |
|---|---|
| `a1f7694` | Terrain holes end to end, plus `docs/terrain_holes.md` |
| `fec5127` | Corrected a stale comment about the SH probe grid |
| `87ecf74` | FX volumetrics lit from the baked probe field, off by default |
| `cdabd9a` | Halved the particle card size peak |
| `9973d5c` | FX accumulated in float and composited once — fire is orange, not white |
| `e7e7fc5` | Glow on the FX pass |
| `25e770b` | Glow radius control |
| `e2f697c` | Glow hard wired at the owner's tuning, sliders removed |
| `97ef7a7` | Merge to master (un-reverts the card particle system) |

Two safety tags: **`pre-hdr`** (before the FX compositing work) and
**`master-pre-fx`** (master before the merge).

## Where to read about each

- **`FX_PIPELINE.md`** — the FX pass end to end: accumulation, composite,
  glow, probe-grid lighting, what is locked, how to measure. New this session
  and the main reference.
- **`terrain_holes.md`** — hole block format, the per-chunk mirror, the
  map-wide mask, the shader discard. New this session.
- **`PARTICLES_HANDOFF.md`** — particle simulation and emitter data. Updated.
- **`VFXBIN_PARTICLE_FORMAT.md`** — the `.vfxbin` format. Unchanged this
  session but now the single source for the atlas rect ordering.
- **`FX_plan.md`** — historical recon. Carries a status banner now.
- **`decals.md`** — the decal pass end to end, and why its tangent frame is
  not allowed to come from screen-space derivatives. New, later the same day.
- **`terrain_blending_edge.md`** — the game's own terrain-onto-model blend,
  decoded. A specification, not an implementation.

## The headline result

Fire rendered yellow-white where the game renders it orange-red, because
`gColor` is Rgba8 and the FX composited *after* `deferred.frag` had already
tonemapped. Every blend clamped at 1.0, so overlapping additive cards pinned
red first and drifted to white.

Fire pixels blown to `R=G=255`:

| | |
|---|---|
| before | 24.4% |
| float accumulation, luminance roll-off | 15.9% |
| float accumulation, **peak-channel** roll-off | **0.2%** |
| the game's own frame | 0.0% |

The fix was confined to the FX pass — a second Rgba16f framebuffer sharing
`gDepth`, and one composite pass. SSR, water, fog, FXAA and the minimap are
untouched. A full-frame HDR conversion was considered and rejected as far
larger: the frame round-trips through the 8 bit default framebuffer twice
mid-frame, so converting everything means rebuilding the frame graph.

## Verified, and how

- **Terrain holes** — confirmed on screen by the owner on 101_dday. Load logs
  `terrain hole mask: 896x896, 4221 hole cells set`.
- **Probe-grid FX lighting** — negative control is **bit-identical** (same
  sha256 with the toggle off), so the touch to the locked `volumetric.vert`
  costs nothing when disabled. Positive effect measured at -12.6% on lit smoke,
  but over only 49 pixels; see the caveat below.
- **Glow** — deterministic A/B with `freezefx`: 9768 pixels lifted (1.0% of
  frame), mean added `R=21.4 G=2.6 B=-0.1`, blown 0.51% → 0.39%.
- **Merge** — builds clean, `particles: 98 placement(s)`, `Shaders Built.`,
  `glGetError: NoError`.

## Open / not verified

- **The probe-grid FX toggle does almost nothing on 19_monastery**, and that is
  expected: only 1 of 11 volumetric materials there is `lit=True`. Judge it on
  `07_lakeville`, which authors many. It is off by default and is a look
  decision, not a bug fix.
- **On maps that author `additive=True lit=True` together** (`51_br_battle_city`
  logs up to 19 such materials) the probe-grid toggle *does* change fire. Not
  tested there.
- **The D-Day trenches still show terrain.** They have no authored holes — the
  cliffs do. `g_vertexColorMode = 2` dissolves the trench *model's* lip, not
  the terrain. `g_enableTerrainBlending` is declared in
  `ModelLoaders/PrimitiveLoader.vb` and never assigned or read; the only other
  occurrences of the name are string literals in `Space.bin/modSpaceBin.vb`'s
  property tables. That is the next lead.
- **Particle density** remains the big open one — rate 1-5/s against 3-6 s
  lifetimes gives 3-30 live cards per emitter, which cannot make the game's
  dense column. See `PARTICLES_HANDOFF.md`.
- **`volumetric.frag` still divides each card by its own luminance** before
  accumulation. That is now redundant and slightly caps single-card brightness,
  but it is inside the locked shader and removing it changes the look. Left
  deliberately.

## Corrections made to the record

Worth knowing about, because each one was believed and written down before
being disproved:

- A commit message asserted the monastery smoke was the volumetric mesh. It is
  **cards**. `PARTICLES_HANDOFF.md` now says so.
- The `63-x` per-chunk mirror in `get_holes` was removed on the strength of a
  comparison against `global_AM.dds` — but that image was itself rendered
  mirrored, because `global_AM` maps to world through a negative affine on both
  axes. The mirror was right and was restored.
- A card-size before/after measurement (mean R-G 27.5 → 34.3) was taken across
  two captures with the camera moved between them. It measured framing, not the
  change. The numbers in `cdabd9a`'s message are unsound; the change itself is
  fine.
- The glow's first measurement reported the added light as **green**. That was
  particle sim drift between two live runs, not the glow. With `freezefx` it is
  strongly warm.
- The first glow implementation added bloom *after* the roll-off, which clipped
  and undid part of the HDR win (0.50% → 1.70% blown). Summing before the
  roll-off reverses it.
- The composite first normalised by luminance, which does not bound the
  channels and left 15.9% still pinned.

The pattern is the same every time: a plausible mechanism, no measurement, or a
measurement whose control had drifted. `FX_PIPELINE.md` ends with the metric
and the exact commands that settle these.

## Late finding: terrain blending is not holes

Decoded at the end of the session from the game's own
`terrain_blending_edge.10.dx11.fxo`, written up in **`terrain_blending_edge.md`**.

**The game never punches holes for trenches.** A fullscreen post-process paints
terrain albedo and normal *onto the model's* G-buffer pixels, faded by height
above the terrain and dithered with a triplanar noise. The model merges into
the ground because its lower edge is rewritten to be ground.

That kills the approach we were heading toward - masking terrain by a model's
footprint is wrong in kind, not just in precision - and it explains why D-Day's
trenches have no authored holes while the cliffs do. The two systems are
unrelated. `terrain_holes.md` still stands for cliffs.

`g_vertexColorMode = 2` is a *separate* mechanism dissolving the model's own
lip. No vertex colour appears in the blending pass at all.

**Next step** is in `terrain_blending_edge.md` under "Implementing it in
nuTerra": every input already exists, and `g_enableTerrainBlending` needs to
reach the G-buffer flag channel.

## Parked work - LANDED

Both changes that were parked on `wip/parked-2026-08-31` are now on master.
The branch is left in place as the record of why they were held back.

| commit | what |
|---|---|
| `9b7adc8` | alpha cutout in the baked shadow map's model pass |
| `afab033` | "Show vertex colours" debug view |

### Alpha cutout - verified live, effect is real but small

Structural claims all check out: `CandidateDraw.material_id` exists
(`common.h:74`), attribute 4 (texcoord1) *is* enabled on the `allMapModels`
VAO that `draw_models` binds, and the test is character for character the one
in `mDepthWrite.frag`.

Runtime: `Shaders Built.`, zero `didn't compile`, `sun shadow: drew models
(425 commands)`, `glGetError: NoError`.

A/B on 19_monastery at the close fire camera, `freezefx settle=200`:

| | |
|---|---|
| null control, same build twice | **0 px** - bit identical |
| cutout on vs off | **3 px**, all brighter, max delta 24 |

So the path is live and signed correctly - a cutout can only *remove* shadow,
and every changed pixel got brighter. Three pixels is only a smoke test: that
camera barely sees any alpha-tested caster's shadow.

**Confirmed on screen by the owner (2026-08-31): fences cast their own shape.**
That was the one gap the commit was parked on, and it is now closed.

The dependency to remember: the cutout only does anything if the models are
actually in the bake. `draw_models` in `MapSunShadow.vb:378` returns early
unless `DONT_BLOCK_MODELS` is true, and that flag also gates the model *load*
(`MapLoader.vb:65`). With "Draw models" off the bake holds no model geometry
at all, so fences neither cast a solid silhouette nor a cut one - a null
result there means the models were absent, not that the cutout failed.

One thing to know: for `FX_volumetric` materials `alphaTestEnable` is
overloaded to mean *additive*, not *alpha tested* (`MapLoader.vb:1517`), and
the shadow draw list carries every non-`no_draw` prim group with no shader-type
filter. So FX meshes in the bake now run a cutout test against an unrelated
channel. That is **not new** - `mDepthWrite.frag` has always done exactly this
for the cascades - so the change brings the bake into parity rather than
introducing a class of bug. Worth fixing in both places, together, later.

### Vertex colour view - the parking reason did not reproduce

It was parked over a non-stop `#131222 ... depth comparisons disabled ...
shadow sampler` warning, cause unproven, best hypothesis the live `SetDefine`
recompile.

That hypothesis was tested directly this session. A temporary hook drove
`SetDefine`/`UnsetDefine` on the model shader every 10 frames for 600 frames -
about 60 live recompiles, the define flipping on and off throughout. The
capture confirms the hook really fired (59% of pixels differ from a known-off
frame, i.e. the view was active at capture). **Zero `#131222`.** GL debug
output is definitely reaching the log: `#131154` appears 9 times in the same
run.

Also ruled out: the warning does not appear with the shadow map viewer forced
open either, which was the other plausible trigger, since `DebugDraw` turns
`TextureCompareMode` off on `depth_tex` for its draw.

The negative control for the view itself is **bit-identical** - same sha256
with the toggle off - which is the bar the probe-grid work had to clear.

What is still unexplained is the owner's original sighting. The best lead is
now `ShaderLoader.vb`'s own `StopUse` comment, which documents this exact
warning from the **water** shader and attributes it to a program left bound,
its sampler declarations validated against unrelated texture state at whatever
GL call comes next. That is a pre-existing hazard, unrelated to this view.
Only `deferred.frag` and `water.frag` declare a `sampler2DShadow` at all, and
neither is the model shader. If it recurs, look for a pass that returns
between `Use()` and `StopUse()`.

## Later the same day: the decal checkerboard

The owner reported "weird banding on decals, looks like Z fighting, any cam
movement changes the banding" on 101_dday. It was not depth ordering at all.

`get_tbn` in `box_decals_color.frag` built the decal's tangent frame from
`dFdx`/`dFdy` and divided by the UV Jacobian determinant. That UV is
reconstructed from the depth buffer, so at grazing angles the determinant
collapses and the tangent explodes - and NVIDIA's fine derivatives differ
between the even and odd pixels of a quad, so it alternated pixel to pixel and
wrote a 1-pixel checker into `gNormal`.

Fixed in `5908570` by taking the tangent from the CPU, where it is exact: a
decal's UV mapping is affine in its own box. `301b369` then removed the parallax
chain that fix orphaned, including a `gPosition` sample the pass had no business
making. Full write-up in **`decals.md`**.

Signed checker energy at the owner's camera: **+0.540 -> -0.003**, null control
0 px. Owner confirmed on screen: "no more swimming".

Two method notes, both of which nearly produced a wrong answer:

- **`DecalProject.frag` is not the decal shader.** It is the 3D map cursor
  (`MapCursor.vb`). Patching it produced a bit-identical frame, which looks
  exactly like "my change did nothing" rather than "I edited the wrong file".
- **A build that fails still leaves you a runnable exe.** MSBuild could not
  replace `nuTerra.exe` while the owner had it open, and the resulting
  "bit-identical" verification was the *old* binary rendering. Check the build
  succeeded before believing a null result - the bin copy of the shader is the
  quickest tell.

## Environment traps re-confirmed this session

- **Never build through the Bash tool** — git-bash rewrites MSBuild's
  `/switches` into paths. Build via PowerShell.
- **Shaders validate at runtime only.** A clean build proves nothing about
  GLSL; check stdout for `Shaders Built.` and `didn't compile`.
- **Shader source is `nuTerra\shaders\`**; `bin\...\shaders` is build-copied
  output. Edit the source and rebuild, or you test the old shader.
- **A single stray CR makes git treat a source file as binary**, silently
  skipping `text=auto` normalisation and reporting the whole file as rewritten.
  Diagnose with `git ls-files --eol`: healthy is `i/lf w/crlf`, broken is
  `i/-text w/-text`. This poisoned one commit before it was caught.
- **`snapshot.txt` holds the snapshot section only**, not load-time logs.
  Redirect stdout to capture those.
- **Verify `fx_pass.png`'s write timestamp advanced** before copying it. A
  stale capture is indistinguishable from a null result.
