# Handoff - 2026-08-28, lighting session

Companion to `HANDOFF_sun_shadow.md` (current through 21c56d1) and
`GAME_LIGHTING_MODEL.md` (the transcription of the game's own resolve).

> **STATUS: historical. Read section 9 first, then weigh sections 2-8 against
> it.** This was written mid-session, so "uncommitted" below means uncommitted
> *that day*. What survived landed in `30b77bb`; much of sections 7 and 8 was
> reverted. The probe grid is real and shipped; the resolve rewrite is not.
> Current renderer truth lives in `HANDOFF_2026-08-31_fx_and_holes.md` and
> `FX_PIPELINE.md`.

Everything below was **uncommitted at the time of writing**: 12 files, ~978
insertions, plus two new shaders and two new docs. The tree builds clean and
runs.

---

## 1. Committed today

| commit | what |
|--------|------|
| `66bc328` | Lighting: sun and ambient as mutually exclusive regimes |
| `12efcb7` | Trees: normals face the viewer, not a chain of winding assumptions |
| `9a859ac` | MapSettings: persist everything the panel can set (25 -> 53 fields) |
| `21c56d1` | MapSettings: Abbey baseline, full 53-key edition, all 64 maps |

Note `66bc328` was **later superseded** - see section 4. The mutually-exclusive
contract it introduced is gone from the working tree.

---

## 2. Uncommitted, and solid

These were each verified by measurement, not by eye. Worth keeping.

### SH probe grid (the big one)
Maps ship a baked probe FIELD we never loaded: `probes/sh_grid/*_sh_grid.dds`,
an RGBA16F **volume texture, 8 slices**, one probe every 5 m (10 m on Karelia).
Seven slices carry the packed SH9, the eighth channel of slice 6 is the probe's
reference **height**; slice 7 is padding.

* World box comes from a space.bin section we never read: **WGSH**, 32 bytes,
  `{i32 size, i32 ver, i32 count, vec3 centre, vec3 size, f32 fadeDistance}`.
  Verified across 8 maps - `size.xz / gridDim` lands on exactly 5.00 m every
  time except Karelia's 10.00.
* X is **mirrored** relative to our world. Proven numerically, not guessed:
  each probe stores its own reference height, so sampling both mirror choices
  and comparing against real world height gives ~2.8 m mean error for the
  mirrored mapping and roughly double for the other.
* The grid has its **own** fallback probe, `probes/sh_grid/*_rem_sh.xml`, and it
  is **not** `probes/global/rem_sh.xml` - on Abbey the global one is ~1.8x
  brighter (sh0 1.537/1.436/1.376 vs 0.842/0.844/0.982). Fading the grid out to
  the global one put a bright band across the top of every wall. The grid's DC
  average (0.76) matches its companion, not the global.
* Loader: `TerrainBuilder.load_sh_grid`, called from `get_environment_info`.
  Bespoke DDS read - the general loader has no volume path.

Controls under Lighting Settings: `SH probe grid`, `show probe field`,
`normal offset m`.

**Two bugs found and fixed inside this** worth remembering:
1. The normal offset was mutating `world_pos` before the height fade read it,
   so all flat ground was blended ~17% toward the global probe, by a
   slope-dependent amount. The offset must move the LOOKUP only.
2. The box edge was a hard `outside ? 1 : 0`, drawing a ring across the terrain
   at 700 m. Now eased over two probes.

### Two-pipe shadow test
`shadow_strength` and `horizon_strength` were lerping the shadow **toward lit**
*inside* the shadow functions, so any value below 1 left a floor the sun could
always shine through - visible as sun specular on plainly shaded surfaces,
because `1-exp()` on a glint term amplifies any residue.

Both functions now return the **raw** shadow. The test happens first, on the raw
value, and the strength sliders apply only inside the lit pipe:

```glsl
bool  in_sun     = sun_raw > 0.0;
float sun_lift   = mix(1.0, cascade_raw, shadow_strength)
                 * mix(1.0, baked_raw,  horizon_strength);
float sun_shadow = in_sun ? sun_lift : 0.0;
```

Shadowed surfaces also source their reflection from the **probe** rather than the
sky cubemap - the cube contains the sun, so dimming it was the wrong correction.

### Auto exposure + the game's tone curve (newest, least tested)
`combined_hdr_resolve` keeps a `g_avgLumMap` and computes
`exposure = (k + 1) / avgLum`. That is most of why an in-game screenshot of a
shadowed courtyard reads bright and ours reads dark.

* `shaders/PostProcessing/avg_lum.*` writes **log** luminance into a 256x256
  R16F with a full mip chain (`MainFBO.lum_tex`); `GenerateMipmap` averages it
  to one texel. Log space = geometric mean, so one bright window cannot drag
  exposure down.
* Runs at the end of the frame, so the tonemap reads the **previous** frame -
  which is what eye adaptation wants anyway.
* Curve: `c = 2^(-1/(2c+k))`, then black-point and scale. Those two are **not**
  free parameters - they are whatever makes the curve pass through 0 and 1, so
  both derive from k: `black = 2^(-1/k)`, `scale = 1/(1-black)`. k is the only
  knob, which is lucky because the runtime constants were not readable.

Controls: `Auto exposure (game)`, `Tone shoulder k`, `Exposure min/max`.
`Tone Exposure` becomes a compensation multiplier while it is on.

### Smaller, all verified
* `gAUX_Color` renamed **`gGlass`** - its only consumer is `glassPass.frag`,
  which samples it as `glassMap`. Opaque geometry also dumps surface normals
  there and nothing reads them.
* **`gGMF.a` was never written for models** after an earlier edit removed the
  only line that wrote it. Undefined alpha = undefined wetness, which randomly
  pushed the specular lobe to `WET_POWER 96` and switched on the wetness cubemap.
  `main()` now writes 0 explicitly.
* Textures viewer showed `gColor`/`gGMF` as near-black. Not a shader bug - ImGui
  alpha-blends, and both carry a MASK in alpha. Fixed with `glTextureView`s that
  share storage and override only the alpha swizzle. Cannot affect sampling.
* `Blits` GPU timer added. The gColor/gColor_2 ping-pong costs **0.020 ms
  (0.9%)** - the double-buffer refactor is not worth doing. Trees are 27% of
  frame time and are the real target.

---

## 3. Uncommitted, and NOT converged

**The model PBR path.** `model.frag` plus the model branch of `deferred.frag`.
Nothing here matches the in-game reference yet and the premise changed several
times. Do not trust the current values; do trust the findings.

### What is actually established
* Map `_GMM` and tank `_GMM` are **different families**. Tank Exporter's curves
  (`pow(R/0.8,7)`, `pow(G/0.5,5)*1.5`) were fitted to tanks and **saturate** on
  map content: the milk can's G of 0.586 became metal 1.0, stripping the diffuse
  the reference plainly shows. Monastery stonework (G 0.47) also clamped to
  fully metallic.
* The channel layout is **conditional**, straight from the game's PBS_ext writer:
  ```
  mov  r2.x, r3.y                        ; metal = G
  movc r2.xy, g_enableAO, r3.zx, r2.xy   ; if g_enableAO: metal = B, AO = R
  ```
  `cb0[74].y` is `g_enableAO` (offset 1188). Both readings are correct, for
  different materials - which is why this was so slippery.
* The canisters set `g_useNormalPackDXT1` but **not** `g_enableAO`, so for them
  **R = gloss, G = metal**.
* `resolve_lighting` uses **no curves**: gloss read linearly, metal only
  gamma-decoded. Current code now does that - `metal = pow(G, 2.2)` gives the
  can 0.30 rather than 1.0, keeping the diffuse gradient the reference shows.
* Milk cans and buildings use the **same** shader (`PBS_ext.fx`). There is no
  model-specific path; every change hits every building too.

### The reference
An in-game screenshot of the Abbey courtyard. The cans are **matte** - soft
vertical gradient, no sharp reflection, pale warm grey, roughly as bright as the
stone behind them, and **in shadow**. They are not chrome. A surface at
`metal = 1.0` has no diffuse and cannot produce that gradient at all.

Also: the reference's shadowed courtyard is **bright and readable**. Ours is
much darker. That is the exposure gap section 2 addresses, and it should be
judged before any more material tuning.

### Invented knobs that are NOT in any reference shader
Flagged so they can be stripped once the look settles: `ENV_REFLECTION`,
`PBR_SKY_FLOOR`, the `3.33 * SPECULAR` model gain, and the whole PBR curve panel
(`PBR_R_SCALE/POWER`, `PBR_G_SCALE/POWER`, `PBR_METAL_MUL`) which is now bypassed
by the game decode. `PBR_DEBUG` (**Show channel**) is worth keeping.

---

## 4. Superseded today

`66bc328`'s "sun and ambient are mutually exclusive" contract is **gone**. The
game does not do it: ambient is always present, scaled by occlusion, and the sun
adds on top. It gets away with that because its ambient is a directional probe
field - a face turned from the sun already receives less irradiance. Our flat
ambient was why we needed the hard split; the probe grid is the real fix.

Also removed: `sun_tint` (no blend toward white exists in the game - the sun IS
`m_color`) and `ambient_sat` (ambient is tinted by `m_ambientTint`, not
desaturated). Their UBO fields survive as dead floats - removing them shifts the
std140 layout in `common.h` AND the VB struct, and a mismatch silently corrupts
every property after them. Not worth it mid-session. Their keys are also gone
from map settings, so the 64 files carry unknown keys that Load logs and skips -
same pattern as the retired `shadow_mapping`.

---

## 5. Method notes (these cost real time today)

* **git-bash mangles MSBuild switches.** `/t:Build` and `/nologo` get rewritten
  into paths (`C:/Program Files/Git/nologo`), MSBuild fails with MSB1008, and a
  grep for `error BC` finds nothing - so it *looks* like a clean build. Two
  "builds" silently did nothing. **Always build through PowerShell and check the
  dll timestamp moved.** Same trap hits `fxc /dumpbin`.
* `Console.WriteLine` output is capturable with
  `Start-Process -RedirectStandardOutput`. The Snapshot file only holds the
  snapshot section, not load-time logs.
* A change can look live when it is not: shader edits load from disk at runtime,
  so `.frag` changes take effect even when the VB build failed.
* Hot-reloading a `const` into the bin copy of a shader is a cheap way to A/B a
  uniform at a fixed camera. Restore from source afterwards.

---

## 6. Suggested next steps

1. **Judge auto exposure first.** It moves everything and it is the biggest
   single difference from the reference. Nothing material should be tuned until
   the exposure model is settled.
2. Then re-check the cans and the monastery stonework with the game decode.
   Expect metal ~0.30 and ~0.19 respectively, both keeping their diffuse.
3. If it holds up, strip the invented knobs from section 3.
4. Commit in slices - probe grid / WGSH, shadow two-pipe, auto exposure, and the
   small fixes are all independent of the unresolved model PBR.
5. Trees are 27% of frame time (`Trees 0.633 ms` of `2.339 ms` total). That is
   the optimisation target when performance comes up.

---

## 7. Fable pass, same day - the shaders sorted  [REVERTED - see section 9]

> **None of this is in the tree.** The whole resolve port was reverted on
> 2026-08-29. The DATA findings it records (pmrem HDR alpha, the BRDF LUT
> layout, metal as an F0 magnitude, the conditional `_GMM` layout) are
> measurements of the game's own files and remain true. The nuTerra code
> described below does not exist.

The three-model hybrid in `deferred.frag` (old Phong + half-ported Tank
Exporter + fragments of the game model) is gone. The lit path now implements
`GAME_LIGHTING_MODEL.md` end to end, and section 3''s non-convergence is
resolved by not tuning the hybrid at all.

### Two facts found by measurement, and they were the whole problem

* **`pmrem.dds` is DXT5 with an HDR exponent in alpha.** The game decodes
  `rgb^2 * 2^(9a) / 8`; alpha spans 38..255 on Abbey''s global probe, a
  0.3x..64x multiplier, sun disc at the top. We were reading it as flat sRGB
  and discarding alpha - which is why reflections needed `ENV_REFLECTION` and
  the `3.33` gain to exist. `pmrem_decode()` in deferred.frag now does it
  properly, everywhere the cube is sampled.
* **`env_brdf_lut.dds` (misc.pkg) is A16B16G16R16F, indexed `(alphaR, NdotV)`,
  scale in R, bias in G.** Verified against its own corner texels (bias -> 1 at
  grazing on smooth, scale -> 1 / bias -> 0 at normal incidence). The old code
  sampled a corner of it at `(NdotV*0.1, rough*0.1)` through SRGBtoLINEAR.

### What deferred.frag is now

* Linear-light pipeline: albedo gamma decoded (2.2) at input like the game''s
  `g_gammaCorrection`, all lighting in linear HDR, tonemap at the end. The
  auto exposure curve finally operates in the space it was designed for.
* Sun: the game''s BRDF verbatim - GGX with their roughness curve
  (`(1-g^2)^2 + max(0.3-1.3g, 0)`), Schlick-Gaussian Fresnel,
  Smith-Schlick visibility, Burley diffuse, `Fd *= 1-min(metal^2*3.2,1)`
  conservation, specTint hue. One unit change, commented: the whole term is
  scaled by PI so Lambert-white matches the old `albedo*NdotL` and the 64 map
  baselines keep their meaning.
* Metal is an **F0 magnitude** (gamma decoded), NOT a metalness lerp - the
  id-7 byte 59 decoding to exactly 0.04 is the proof. No `mix(0.04, albedo)`
  workflow, no albedo premultiply.
* Env specular: game section 3 - dominant-direction lerp, `alphaR^2` mip with
  grazing blur, split-sum LUT, `env *= min(local/global, 1)` per channel from
  the probe grid (replaces the luminance `sky_vis` + `PBR_SKY_FLOOR`).
* The reflection vector is world space now (invView, then the display mirror
  on x, same as the probe grid). It was view space before - every reflection
  swam with camera yaw.
* Wetness = gloss lift to 0.85, nothing else. The WET_POWER lobe and the
  separate wet cubemap term fell out. Water keeps its tuned glint, PMREM
  decoded, gated by the raw-tested shadow as before.
* Grading LUT moved AFTER the tonemapper (display space); its HDR magnitude
  carve-out is now just a free guard.
* Families: models take r=gloss / g=metal; terrain+outland take g (the mixed
  spec sample) as gloss, metal 0. Trees/roads unchanged writers, now matte
  through the same BRDF.

### model.frag

* `decode_gmm` implements the conditional layout from the PBS_ext writer:
  `g_enableAO ? gm.gb : gm.rg`, i.e. AO on: R=AO, G=gloss, B=metal. AO
  multiplies albedo (no spare G-buffer channel for GB1.w). Applied in ext,
  dual, detail and repaint entries.
* The `pow(1/1.3)` albedo lift in main() is gone - deferred decodes the whole
  G-buffer in one place now.

### Removed (shader + VB + map settings fields + panel)

`ENV_REFLECTION`, `PBR_R_SCALE/POWER`, `PBR_G_SCALE/POWER`, `PBR_METAL_MUL`,
`PBR_SKY_FLOOR`, the `3.33 * SPECULAR` model gain, WET_POWER, the model
`1-exp()` spec knee (the tonemapper rolls off highlights now). Kept: **Show
channel** (`PBR_DEBUG`), the two-pipe raw shadow test, everything in section 2.
Old map settings files carry the retired keys; Load logs and skips them.
`props.SPECULAR` now touches only the water glint.

### Verified

Builds clean, Abbey loads with no GL debug output, renders sane at overview
distance (screenshotted). NOT yet judged against the in-game reference at
street level - the milk cans, the courtyard exposure, and reflection stability
while orbiting all need eyes on them. Settings applied count dropped 60 -> 53,
exactly the retired fields.

---

## 8. The thing that was missing: there is no HDR path  [PARTLY REVERTED]

> The auto-exposure code this section dissects was reverted with section 7,
> so `measure_avg_luminance` no longer exists and the feedback loop described
> below is not currently running. The STRUCTURAL fact still holds at HEAD:
> `gColor` is `Rgba8`, `deferred.frag` tonemaps in-shader, and there is no
> HDR buffer anywhere. The two shader bugs listed as "mine" went with the
> revert as well.

Found after section 7, and it subsumes it. Two bugs of mine plus one
architectural gap that has been there all along.

### My own regressions from the section 7 rewrite, both fixed

1. **BRDF LUT sampled with Repeat wrap.** `openDDS` sets
   `TextureWrapMode.Repeat` for everything. The old code only ever read a
   0.1 x 0.1 corner of `env_brdf_lut`, so it never mattered. Sampling the full
   `(alphaR, NdotV)` range does: `NdotV = 1` - any surface facing the camera -
   lands on the last texel centre, and a linear tap there blends into row 0,
   the **grazing** row, whose bias is 0.98. That painted a near-full Fresnel
   over every head-on surface, which from a top-down camera is most of the
   frame. Fixed with `ClampToEdge` on the LUT at load plus a half-texel inset
   in the shader.
2. **Dielectric F0 was 0.** Terrain, roads, trees and water all write metal 0,
   and this encoding takes metal AS the F0 magnitude, so they got no Fresnel
   at all - no head-on sky reflection, no sun glint, wet asphalt mirroring
   nothing. `metal = max(metal, 0.04)`. The game never hits this because its
   material id 7 forces the byte 59 which decodes to exactly 0.04.

Also enabled `GL_TEXTURE_CUBE_MAP_SEAMLESS` - the reflection path now samples
blurry PMREM mips where face-edge bleed is visible, and the cube was on Repeat.

### The gap: auto exposure measures its own output

`measure_avg_luminance()` binds **`MainFBO.gColor`**, and it runs at the very
end of the frame - after the deferred resolve, water, FX and FXAA. At that
point gColor holds the **finished, tonemapped, gamma-encoded 8-bit frame**.

So the loop is:

```
exposure = (k + 1) / avg( tonemap( scene * exposure ) )
```

Exposure appears on **both sides**. This is not a measurement, it is a
fixed-point solve, and its fixed point pins the displayed mean to roughly the
same value for every scene. The game's `g_avgLumMap` measures the HDR scene
*before* tonemapping, which is independent of exposure - a categorically
different quantity.

Measured: raising `sun_strength` 0.27 -> 2.0, a **7.4x** increase in sun
energy, moved the frame mean **+16%** and p95 **+11%**. The loop eats it.

The `log()` / geometric-mean reasoning in `avg_lum.frag` is sound but has
nothing to work with: its input has already been crushed into [0,1] by the
tone curve, so `avg` can only ever occupy a narrow band and the system cannot
tell a dark courtyard from an open field.

### Why this matters more than any of the material work

**`gColor` is `Rgba8`.** There is no HDR buffer anywhere in the renderer. The
deferred pass tonemaps in-shader and writes display-space bytes, so:

* highlights clip at 1.0 before water, FX and FXAA ever composite over them;
* there is nothing to measure even if avg_lum pointed somewhere sensible;
* the PMREM range decoded in section 7 - a genuine 0.3x..64x - is crushed into
  8 bits the instant it is computed.

That is why the frame reads flat, and why three total rewrites of the BRDF all
produced near-identical screenshots: **the exposure loop cancels whatever the
lighting does, and the 8-bit target throws away the range it was working in.**
It also means handoff step 1, "judge auto exposure first", could not have been
carried out - there was nothing there to judge.

### The fix, NOT done - it is a real refactor and wants a decision

1. New `scene_hdr` target at `Rgba16f` (or promote gColor and split the
   G-buffer albedo onto its own texture - gColor is currently both).
2. `deferred.frag` outputs **linear HDR**: no tone curve, no grading LUT, no
   output gamma.
3. Water / SSR / FX composite into the HDR target. Their blit dance is
   unchanged, just wider.
4. `avg_lum` measures **that** buffer - which is what its log-space geometric
   mean was designed for.
5. A new final pass does exposure + tone curve + grading LUT + gamma, writing
   display bytes to the default framebuffer. FXAA runs on that.

Touches FBO_main, the modRender pass order, water, SSR and FXAA. Everything in
sections 2 and 7 is independent of it and can commit first.

---

## 9. What actually landed - 2026-08-29, commit 30b77bb

Sections 7 and 8 were both reverted. This is the state of the tree.

### Reverted, and not coming back unless asked

The game-resolve port, the linear-light pipeline, the auto exposure work and
the two-pipe shadow rework are all gone. `deferred.frag` is the owner's own
pushed version: `mix(1.0, raw, strength)` inside both shadow functions, the
LINEAR `direct_light` split (no smoothstep gate), the sun gated by
`sun_shadow`, and `lut_color_correction` without the HDR magnitude guard.

The one thing worth remembering from that attempt: the linear split is
energy conserving. `ambient*(1-d) + sun*d` with d = N.L * shadow sums to a
constant across every facing angle, which is why it has no terminator band.
The `smoothstep(0.05, 0.35, ...)` regime gate does not - it strips all the
ambient at N.L = 0.35 while the sun it grants is still only a third of full.

### The open question, unanswered

Lit surfaces read dark because the two terms are the SAME magnitude:

```
shade  = albedo * irradiance(1.29) * AMBIENT(0.202) = albedo * 0.261
sunlit = albedo * NdotL * sun_rgb(0.97) * sun_strength(0.27)
       = albedo * NdotL * 0.262
```

So a lit surface is at best equal to the shade it replaced and darker at any
other angle. `sun_strength` was almost certainly authored when the sun ADDED
to ambient rather than replacing it. Raising it toward 0.8-1.0 is the lever;
this was never tried. Note also that the panel misleads - ambient carries a
hidden ~1.29x from the SH DC term that the sun does not, so 0.202 and 0.27
are not the comparison they look like.

### Landed: the SH probe field

* **WGSH**, 40 bytes in space.bin: `{i32 size, i32 ver, i32 count, vec3
  centre, vec3 size, f32 fade}`. Abbey: centre (0,0,0), size
  1400x200x1400, fade 15 m. 1400/280 = exactly 5.00 m per probe.
* `load_sh_grid` in TerrainBuilder, beside `load_sh_ambient`. Volume as a
  Texture3D, clamped on all three axes.
* The field's OWN companion probe, from the `rem_sh.xml` beside the grid -
  NOT probes/global, which is 1.8x brighter here.
* `ResMgr.LookupBySuffix` - the grid carries a content hash in its name,
  all zeros on 80 maps but per-environment on Murovanka, North America and
  others. `GLTexture.SubImage3D` - only the compressed 3D upload existed.
* `deferred.frag` swaps the global probe's irradiance for the field's at ONE
  point and changes nothing else. `sh_grid_enabled` 0 makes the checkbox a
  live A/B against the previous shader.
* `probe_field.frag/.vert` - a whole separate program selected by "show
  probe field", so the lighting shader has no knowledge of the grid.

### What is actually in a probe (measured, not assumed)

32 numbers, stored a CHANNEL per slice:

| slice | contents |
|-------|----------|
| 0,1,2 | red, green, blue: (constant, L.y, L.z, L.x) |
| 3,4,5 | red, green, blue: quadratic (xy, yz, zz, xz) |
| 6     | (x^2-y^2) for r,g,b, and **.w = reference HEIGHT in metres** |
| 7     | carries data; the game's resolve reads only seven slices |

Evidence it is real irradiance: the centre probe's constant term is
R 0.468 / G 0.546 / B 0.709 - sky blue - and `L.y` is the dominant linear
term everywhere and positive, strongest in blue. Light from above. Slice 6's
alpha spans -100..+102 m across the grid, which is exactly the 200 m WGSH
box.

**Two earlier claims corrected by that dump.** Slice 7 is NOT padding (std
0.216, range -0.30..0.92). And probes do NOT bake to near black inside
buildings - zero of 78,400 sit below 0.05, the darkest is 0.027 against a
mean of 0.77. The normal-offset slider's stated justification was wrong; it
still biases a wall's sample toward open air, but not for that reason.

### Also landed

* GFX markers removed entirely - class, shaders, draw call, globals, both
  settings keys, the UI, and the dead keys in all 64 shipped settings files.
* Menu: "Lighting Settings" -> "PBR shading", "Map" -> "Section Visibility",
  "Map Settings" -> "Save Map Settings". The duplicated shadow strength
  slider is now one "Shadow Mix" under PBR shading.

### Method notes worth keeping

* **Never A/B by screenshot while the camera can move between launches.**
  Two attempts were void: one photographed a different application entirely
  because `SetForegroundWindow` cannot steal focus while the user is working,
  the other compared two different camera positions. Verify the window is
  actually frontmost, or just look at it yourself.
* Hot reload works: copy `nuTerra\shaders\...` over
  `bin\Debug\net6.0-windows\shaders\...`. The FileSystemWatcher is on the
  BIN folder, recursive, any file - it fires and every program recompiles
  next frame. One `GL Error InvalidValue` per reload is inherent to that
  path, not to any particular shader (reproduced by touching an untouched
  file).
* An editor holding a stale copy will silently undo a session's work on
  save. It happened here, to both source and bin.
