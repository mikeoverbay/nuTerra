# Open threads

Loose ends as of **2026-09-06**, written down so a compacted session or a new
one can pick them up cold. Each entry says what is known, what is NOT known,
and what the next concrete step is.

Ordered roughly by how much they will bite.

---

## 0. The FXAA toggle does nothing, and the fix crosses two lanes

`perform_SSAA_Pass` in `modRender.vb` does

    GL.Uniform1(FXAAShader("pass_through"), CInt(FXAA_enable))

and **`shaders/PostProcessing/FXAA.frag` declares no such uniform.** The lookup
returns -1, GL discards the set silently, and the shader has no pass-through
branch in it at all. So FXAA runs on every frame regardless of what the
checkbox says, and has done for as long as the line has existed.

**Why it is not a one-line fix.** The uniform is named `pass_through` but is
fed `FXAA_enable` - opposite senses. Declaring `uniform int pass_through` in
the shader and branching on it literally would make ticking "FXAA" SKIP FXAA.
The semantics have to be settled before either half is written, and the two
halves are in different lanes: the call site is nuTerra's `modRender.vb`, the
branch is in the shader.

**Decided 2026-09-13:** rename the uniform to `apply_fxaa` and pass
`FXAA_enable` through unchanged, rather than inverting at the call site with
`CInt(Not FXAA_enable)`. The name then matches the value it carries and nobody
has to remember an inversion - an inverted flag whose name says the opposite is
how this bug happened in the first place.

**Next step:** one line in `modRender.vb` (rename the uniform in the lookup),
one declaration plus a branch in `FXAA.frag`. Held until after the pending push
so it does not land in a tree four sessions have just declared ready.

**Worth knowing while it stands:** any A/B done by toggling FXAA is comparing a
frame against itself. Found by the Shader IDE + engine session while checking
whether FXAA was responsible for a measured lift in the frame's transfer curve;
it was not, and the toggle being inert is why that had to be checked another
way.

---

## 0b. PrimitiveLoader is missing two shipping vertex formats, and fails silent

A census of every shipping `vertices` section on the NA install - 120,975 of
them, from the PKG Explorer session - found exactly six formats, one stride
each:

    BPVTxyznuvtb        32
    BPVTxyznuviiiwwtb   40
    BPVTxyznuvitb       36     <-- nuTerra has no case for this
    BPVTxyznuv          24
    BPVTxyz             12     <-- nor this
    BPVTxyznuviiiww     32

`PrimitiveLoader.vb:448-495` handles **four** of those six and falls to
`Case Else -> Debug.Assert(False)` on the other two. `stride` is initialised to
0 at :445 and the Else branch does not set it, so in a RELEASE build - where
`Debug.Assert` compiles out entirely - the loader carries on with **stride 0
and no message at all**. That silence is the expensive part, not the missing
arithmetic: `BPVTxyznuvitb` alone is 164 sections under `content/buildings`.

**And three of the seven branches are dead.** `xyznuv`, `xyznuviiiwwtb` and
`xyznuvtb` - the non-BPVT spellings - match nothing: the census found ZERO
sections with a bare header, every one is `BPVT`. So the switch carries three
cases that cannot fire while missing two that do. (`xyznuviiiwwtb` also claims
`stride = 37`, an odd number for a vertex stride, which nothing has ever
exercised.)

### `BPVTxyz` does not garble - it OVERRUNS, and stays plausible

The stride-12 format is the nastier of the two and the reason the fix is not
just two more `Case` lines. It is POSITION ONLY. A reader with fixed attribute
offsets does not produce obvious rubbish: the normal it reads from `+12` is the
NEXT VERTEX'S POSITION, so the stream stays structured, in range, and plausible
the whole way through the buffer. Nothing trips. Exporter Studio hit exactly
this in their own reader and reported it was not a one-line fix.

That is the same failure the directives file calls out under "measure, then
claim" - correct arithmetic over the wrong set, producing a believable answer -
and it is worth knowing that it happens in binary parsing too, not only in
measurement. A garbled mesh announces itself. A mesh read one vertex out of
phase looks like a mesh.

### NEITHER missing format is render geometry - expect NOTHING to appear

This is the part to read before implementing, because the obvious expectation
is wrong and will cost an evening.

`BPVTxyznuvitb` is the **havok collision proxy** format. Every occurrence found
scanning `bld_*.primitives_processed` across the packages sits in a
`lod0/havok/` subfolder and is named `*.hkt.primitives_processed` - measured by
the Shader IDE + engine session, and independently consistent with PKG
Explorer's winding statistics, which put it on the rigid side at +0.92.

`BPVTxyz` is **audio occlusion geometry**. Measured here 2026-09-13: scanning
1,040 `.primitives_processed` across 14 map packages for the exact marker
`BPVTxyz\0` - the NUL matters, or it also matches `BPVTxyznuv` - found 11
sections and every one is

    content/Audio/SoundObstacle/<map>/<map>_SoundObstacle_01.primitives_processed

One per map, plus the `_comp7` variants. A sample rather than a census, but 11
of 11 on a single path shape. Position-only is exactly what a sound occluder
needs, the same way it is what a collision hull needs.

**So adding both strides will make NOTHING APPEAR ON SCREEN.** No building is
missing parts. nuTerra is failing to read collision hulls and audio occluders
that it very likely never draws. Three consequences:

* **Do not go hunting a second bug** when the geometry count does not change.
  That is the fix working.
* **This is a correctness and diagnostics fix, not a missing-content fix.** The
  silent `stride = 0` is worth killing because of the CLASS - the next format
  that ships may well be drawable - not because of these two instances.
* **There may be no way to see it working except a log line**, which is the
  argument for doing the logging half FIRST and the two `Case` entries second.
  The log is the only instrument that will show either of them landing.

**Next step, after the pending push:** name the unrecognised format in a log
line and treat `stride = 0` as a hard error at the call site FIRST; add the two
`Case` entries second. Two entries fix today's corpus; only the logging fixes
the next patch that ships a seventh format.

### The trap waiting in that fix: the lone `i` is NOT a skinned marker

Winding is already handled correctly here and it is worth not breaking.
`load_primitives_indices` applies an unconditional DirectX-to-OpenGL corner
flip at :411, reading `y, x, z`. Skinned meshes ship with the OPPOSITE winding
to rigid ones - measured by PKG Explorer at signed normal-vs-winding **-0.937
for iii/ww meshes against +0.92 for rigid** - so :572 swaps them back, gated on
`hasIdx`, which is set only for the three `iii`/`ww` families. That is the
conditional the measurement says is required, and the comment at :566 says why.

**`BPVTxyznuvitb` has a single `i`, and it is RIGID.** It is the havok proxy
format. Setting `hasIdx = True` for it because the name contains an `i` would
un-flip geometry that was never double-flipped and render all 164 sections
inside out. Add it with `hasIdx = False`.

---

## 1. Three new maps crash natively

The 2026-09-01 game patch added three maps nuTerra has never seen:

    140_fall_tanks
    141_dash_to_go
    142_road_to_dash

All three have **21x21** heightmaps and all three die on load with
**0xC0000005 - an ACCESS VIOLATION**, not a managed exception. That matters:
`101_dday`'s crash was managed (0xE0434352) and turned out to be an array
bound. A native fault is a different animal - bad pointer, bad size, or a GL
call with a degenerate dimension.

**Not investigated at all.** No debugger run, no stack. 21x21 is far smaller
than anything the terrain code has met (the previous minimum was 06_ensk at
37, and the averaging branch is written around 37 explicitly), so a hard coded
dimension or a zero-size buffer is the first place to look.

Next step: run one of them under the debugger and read the fault address.

## 2. drop-dotnetzip - MERGED 2026-09-06

Branch `drop-dotnetzip`, commit `7fb84c3`. Replaces DotNetZip with
System.IO.Compression and closes both Dependabot alerts.

Verified: the rendered frame is **bit identical** to the DotNetZip build -
0 px of 960000, same camera, same settings - and seven of eight test maps load
clean. The eighth was dday, whose crash was the unrelated heightmap bug now
fixed on master.

The reported CVE was never reachable here: it is a directory traversal on
extract-to-DISK and every extract in this codebase went to a MemoryStream.
This is hygiene, not an incident.

Merged into master on 2026-09-06 with no conflicts; the frame from the saved
monastery camera is bit-identical to the DotNetZip build (0 of 960000 pixels
differ) and no DotNetZip DLL remains in the output. Only comments still name it.

## 3. water_mask_wet is on in settings, off in code

`WATER_MASK_WET` defaults to **False** in `modGlobalVars`, but the Abbey config
that was propagated to all 64 maps carries `water_mask_wet=1`, so it is
effectively **on everywhere**.

It is the fix for the water plane covering the sky (the "marbling"), and lakes
were confirmed fine with it on. But it was shipped off-by-default deliberately,
and it is now live on maps it was never tested against.

Decide: make the default True to match reality, or strip the key from the
configs. The current split is the worst of both - the code says one thing and
every map says the other.

## 4. Abbey lighting is a baseline, not per-map lighting

`5797700` copied 19_monastery's tuned config to all 64 maps. Every map now has
baked shadow, the SH probe mix and a working exposure, where before they had
untuned defaults.

But Abbey is a **sunset town** and its `tonemap_exposure=3.627` /
`ambient=0.282` carry over literally. 33_fjord, an overcast daylight map, comes
out visibly dark. This is a starting point to tune from, not finished work, and
it should not be remembered as "lighting is done".

`water_y_offset`, `water_exclude_band` and `water_fog_mul` were deliberately
NOT copied - they are per-map water geometry. Backups of both the repo and work
copies are at `C:\nuTerra_backups\`.

## 5. 23_westfeld chunk heights look wrong

Owner's observation. Westfeld and dday are the only two maps the patch reshaped
to **133x133**; both crashed until `heightsTBL` was sized to max(69, mapsize).

They load now, but loading is not the same as being right. The terrain code was
written when every map was 69x69: the averaging branch still only special-cases
`mapsize < 69`, and the heightsTBL consumers (`get_Y_at_XZ`, mouse picking,
neighbour sampling) index it with coordinates derived from world position. The
crash was fixed by making the array big enough, which says nothing about
whether the INDEXING is right at 133.

Next step: compare a height lookup against a raw offline read of
`terrain2/heights` from the .pkg. If dday is wrong the same way, it is the
scaling maths, not the map.

## 6. The resolve samples the cubemap in view space

`deferred.frag` builds `R_env = reflect(-V, N)` from VIEW space vectors and
feeds it to a world oriented `cubeMap` at lines ~823 and ~856
(`prefilteredColor`). No `invView`. That makes the environment reflection
rotate with the camera for **every** specular surface in the scene.

Only the pooled-water path was fixed (it converts through `invView` and clamps
the horizon). The general specular path still has it.

Pre-existing, not introduced by any of this work, and not measured - it is
visible in the code, not in a screenshot. Worth an A/B before believing it
matters.

**2026-09-10.** Still open, and there is now an instrument for it: the
**Look-at cube** checkbox (Section Visibility) draws the environment cubemap as
a 1 m box standing on the look-at point, each face showing the matching face of
the cube. A reflection argument that used to be about an invisible vector is now
something to look at. `Rdom.xz *= -1.0` sits on top of the view-space lookup as
a fudge; it is a 180 degree yaw, not a mirror, and it should go when the space
is fixed rather than being tuned.

## 7. Pooled water: rim and reflection content

Two known-imperfect things in the shipped water:

- **The rim is hard edged.** `POOL_CUT` thresholds a low resolution wetness
  texture that is hugely magnified up close, so the boundary stair-steps along
  texel edges. Blending it needs a gradient, and gGMF.a cannot serve while that
  channel is max-blended flat against the terrain's own wetness.
- **Reflections are sky only.** `ssr.frag` skips sky by design and ADDS where
  water.frag MIXES, so an SSR building hit lands on top of the sky reflection
  instead of replacing it. Making SSR mix for pool pixels is the fix, and the
  surface-kind byte now identifies those pixels cheaply.
- **The handedness flip was lost and is back.** `af1a4e3c` added
  `R_w = vec3(-R_w.x, R_w.y, R_w.z)` to the pool's cube lookup; `63af7051`
  deleted the whole block when it took the cube out of the wet path, and the
  block written to replace it never got the sign back. That is why the
  environment read backwards in pools. Restored 2026-09-10 and **confirmed by
  the owner the same day at a camera with real standing water** - the earlier
  A/B could not see it (see the measurement below). The sign is needed because
  the cube comes from the game, which is DirectX: D3D lays its cube faces out
  left handed and GL samples them right handed, so a correct world direction
  lands on the mirrored face. `water.frag:80` does the same thing and its
  comment has pointed at deferred the whole time.
- **`water.frag` clamps its cube lookup at `y >= 0.02`, deferred at `0.4`.**
  Deferred's `SKY_FLOOR` exists because the cube has a sunset and BUILDINGS
  painted into its horizon band - the Look-at cube shows that band plainly - and
  0.02 samples straight into it. The forward water pass never got that fix.
  Untested: no camera to hand renders both passes at once.

**Measured 2026-09-10 at `cam=-16.2835,3.7183,-0.2196,158.1983,0,4.9512`,** so
nobody re-derives it: the pool block passes its guard on **508 px, 0.1% of the
frame**, at negligible mix weight, and the forward water pass draws **0 px**.
Neither is what paints the wet-looking ground in that view. Any A/B of a pool
change needs a camera with real pooled water on screen - that one has none, and
that is why a fix which visibly works reported zero changed pixels here. **No
cam string for a pooled-water view is recorded yet; the flip was confirmed by
eye.** Writing one down would turn this from a look into a still-for-still
regression test, which is what the rest of this repo measures with.

## 8. Camera flight

Step 1 (the 2.5 m ground clamp) is built. Steps 2-4 are designed and not
started - see `camera_flight_plan.md`, which includes why the bake has to be
its own FBO pass rather than a reinterpretation of the beauty pass.

## 9. Lamps and fog - what is still open after 2026-09-06

The fog session (`HANDOFF_2026-09-06_fog.md`) rebuilt the global fog, added
per-light falloff curves and tuned monastery; the lights session before it is
`HANDOFF_2026-09-06_lights.md`. Left open, in the order they bite:

- **Shaft colour space.** The shaft is added after the tone curve into the
  8-bit back buffer; the ground pool is added before it. Faint shafts stay
  faint and gain gets pushed. Fix: a small half-float shaft target added before
  `correct()` in deferred.frag. One design decision, not taken yet.
- **Shafts vs global fog are separate media.** A shaft is never dimmed by the
  fog between eye and lamp. Small version: scale each shaft by the global fog
  factor at its distance.
- **Smoke is exempt from fog by coverage**, so distant smoke reads too clear.
  Proper fix fogs it by emitter distance. Walked in full 2026-09-08 -
  see `fx_fog_interaction.md` for the mechanism and two routes. Additive FX
  are worse off than smoke: they carry alpha 0 by contract, so the coverage
  term never sees them and they take the full background fog.
- **The height fog inside deferred.frag** (~1480-1510, hard-coded density
  0.005) still runs pre-tonemap when fog_level > 0. Redundant now; remove.
- **Bulb Placer**: done as `BulbPlacer.vb` - see `bulb_placer.md`. Bulbs live
  in the campath and light every instance. Open: no shadow cubes for bulb
  lights; only the 32 nearest to the camera are lit.
- **No loader yet reads `light_catalogue.xml`**; every value in it is a guess.
- **X must be negated** when placing from `map_lights.xml`; one placed light
  confirms. `209_wg_epic_suburbia` fails to scan on `props("colorTex")`.
- Audit items unverified: depth bias growing as t^2 (~1 m at a 20 m rim), the
  cube baked from LOD 1.
- `fog_noise_m` makes cells 32x smaller than its number says. Rename/rescale.
- Path Studio's left column is 952 px tall; the notes block should move.

## 10. The bloom chain - what is still open after 2026-09-08

Session handoff is `HANDOFF_2026-09-08_bloom_and_bulbs.md`. The bloom flicker
under camera motion is fixed (the bright pass was point-sampling one pixel in
sixteen); these are what it left behind.

- **The glow-occlusion test is inverted for reversed-Z.**
  `fx_composite.frag:96` computes `bloom.a - sceneD` where the convention
  stated in `lamp_bulb.frag:61` wants `sceneD - bloom.a`. REASONED: this pins
  `pass_through` near 0.15 across the halo, so the bloom runs at about 15% and
  `FX_GLOW_STRENGTH = 2.0` is partly compensating. Fixing it makes the halo
  ~6.7x brighter and needs the strength re-tuned in the same commit. Measure
  first.
- **`fx_bright.frag`'s depth default calls 1.0 "the far plane".** Under
  reversed-Z it is the NEAR plane. The number happens to be the right safe
  default; the reasoning in the comment is not.
- **The blur targets are Rgba16f again.** The Rgba8 pair was never committed
  and a revert took it. `FX_GLOW_RANGE` and the encode/decode in
  `fx_bright` / `fx_composite` are still in the tree with nothing to fit.
- **`shaft_fbm3` octave 4 is below the shaft march's Nyquist limit.** At
  `fog_noise_m = 250` the octaves are 7.8 / 3.9 / 1.95 / 0.98 m against a
  0.83 m step for a 20 m lamp. It can only alias. Drop to 3 octaves if the
  Lamp fog timer needs it - the noise is now sampled per step, not four times
  per ray.
- **Bulb sprite occlusion has never been verified** with a lamp behind a wall.

### Banding on smoke — open, two candidates, 2026-09-08 evening

Owner reports smoke clean in the **gFX_HDR** viewer and banded on screen.
Confirmed by inspection: **gFX_BloomA contains no smoke at all**, so the bloom
chain cannot be the source, and the glow-occlusion smoothstep above is ruled
out for this symptom. `msm_blur` is a plain linear 9-tap on all four channels
and is also clear. `colorCorrect.frag` — a 16-step 3D LUT that would band
beautifully — is **not in the frame**: `color_correct()` is commented out at
`modRender.vb:481`.

Two live candidates, not yet separated:

1. **8-bit quantisation with no dither.** `gColor` is Rgba8 and the frame
   round trips through the 8-bit back buffer more than once. Nothing dithered.
   Addressed by `FX_DITHER` — see `FX_PIPELINE.md` "Output dither". Landed and
   building, but **not confirmed to be the cause**.
2. **The fog classifies smoke as sky.** `DeferredFog.frag:163` decides sky by
   `dist < 0.001` on `gPosition`, and **FX cards never write `gPosition`** — so
   a smoke pixel against the sky takes `f = fog_sky` and gets mixed toward
   `fog_tint_ovr`. The `gFX_cover` term at line 210 is the only thing holding
   it back, and it is weakest where it matters: thin smoke has low alpha, and
   additive FX carry alpha 0 by contract so fire takes the **full** sky fog.
   The amount then varies with the smoke's own alpha ramp, which would put the
   contours exactly where they are seen. This is §9 / `fx_fog_interaction.md`
   showing up as a visible artefact rather than a design note.

**The discriminator, no rebuild:** drag `fog_level` (or `fog_sky`) to 0 with a
plume on screen. Bands vanish → candidate 2, and the dither is treating a
symptom. Bands survive → candidate 1. Do this before building anything else.

**Resolved 2026-09-08 evening, from the owner's stills.** The Still button was
writing all along — to `G:\nuTerra_ScreenCaps\still`, the `record_dir` in the
user settings, not the C: drive the handoff looked at. `still_049` to
`still_052` from 15:03 settled it, and it was neither candidate: the live
settings had `fog_level = 0` (candidate 2 inert) and scanlines through the
plume showed no plateau longer than 5 px (candidate 1 absent; the dither is
working). The edges were (a) cards cut along the roof plane with a fixed
0.5 m soft fade, and (b) grey cards silhouetted over blue ones — the two
emitters author very different colour tracks and the cards were unlit. Both
addressed in the card pass; see `FX_PIPELINE.md` "Card lighting and the
size-relative soft fade". Still open from it: centre-sorted card order, which
the lighting hides rather than removes.
