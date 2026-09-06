# Volumetric fog audit — what the lamp-shaft code gets wrong

Read-only audit of the lamp shafts (`MapLampFog` / `lamp_fog.frag`), the cube
they sample (`MapLampShadow`) and the two global fogs, done 2026-09-06 against
`master` at `f6da9047`. Companion to `volumetric_fog.md` (the literature) and
`shadows.md` (the cube).

> **Status 2026-09-06, later.** Items 1, 3 (the ClearColor restore), 4, 5, 6 and
> the item 7 texts are FIXED in the commit after `3a1fc537`. Still open: item 2
> and items 8-12 (where scattering is added and in what space, the bias growth,
> the LOD of the bake), the edge cases, and the AlphaBits question. The fixes
> are reasoned like the findings; the confirming renders listed per item have
> not been made.

**Nothing here was rendered.** Every item was verified by reading the code at
the cited line; consequences are reasoned, and each says what render would
confirm it. Nine finder/reader agents contributed candidates; the adversarial
verification stage never ran (usage limit), so the confidence on each item is
the reviewer's, not a vote.

---

## How it works, and the one ordering fact that matters

`MapLampFog.Draw` draws one bounding sphere per `.campath` lamp, additively,
after the global fog pass. `lamp_fog.frag` intersects the view ray with the
sphere, clips to scene depth via `length(gPosition)`, marches `fog_steps` (48)
Bayer-jittered samples, and per sample: windowed falloff, shadow-cube compare,
Henyey-Greenstein phase, `exp(-density * dist)` lamp→sample, a running
`trans` sample→eye, then rolls the sum off on its peak channel. Surfaces are lit
from the same lamps and cube by `path_lights()` in `deferred.frag`. All three
consumers of the cube (surfaces, shaft, the dead compute volume) invert the
depth encoding identically; the `CullFace(Back)` and `BlendFunc` restore
reasoning in the pass holds.

**When the fog and shafts draw, framebuffer 0 is bound, not gColor.**
`modRender.vb:388` binds FB 0 and clears; FXAA draws the resolved frame into it
and `copy_default_to_gColor` copies it back into the gColor *texture* as an
input; `MainFBO.attach_C()` at :411 is a DSA DrawBuffers call and does not
rebind. The shafts therefore land in the window's back buffer: 8-bit,
display-referred (tonemapped, LUT-graded, FXAA'd), requested with
`AlphaBits = 0` (Window.vb:174).

---

## Defects — wrong output with default settings

1. **Shadowed and out-of-range steps never advance the eye transmittance.**
   `lamp_fog.frag:165` `if (dist >= lamp_range) continue;` and `:187`
   `if (vis <= 0.0) continue;` both jump past `:200` `trans *= step_trans;`.
   Air in shadow still extinguishes light, so lit fog seen *through* a shadowed
   span is over-bright by `exp(density * shadowed_m)`: ×1.5 through 6 m, ×2
   through 10 m, ×4 through 20 m at density 0.07. The shaft's own lit/shadow
   edge contrast is therefore wrong in a view-dependent way, and the `:202`
   early-out is never evaluated on those steps. **Fix:** derive transmittance
   from position at the top of the loop, `trans = exp(-fog_density * (t - t0))`,
   before either `continue` (also fixes item 10). **Confirm:** render through a
   wall's shadow toward lit fog behind it, before/after.

2. **Shaft and pool are in different colour spaces.** The pool is added to
   linear light at `deferred.frag:1443`, then LUT (`:1467`), grey, height fog
   (`:1510`), `correct()` (`:1523`: `1-exp(-x*exposure)`, two pow gammas). The
   shaft's `pow(lamp_color, 2.2) * level` (`lamp_fog.frag:150`) is added One/One
   after all of that, into FB 0. Tone exposure, Bright Level and the map LUT
   move the pool and leave the shaft untouched; the shaft reads redder / more
   saturated than the pool under it. The peak roll-off (`:219-225`) bounds the
   shaft's *own* term only: over a lit pool `dst + lit` still clips per channel
   toward white, which is what the comment at `:216-218` says it prevents. That
   property holds over black sky only. **Fix options:** add the shaft before
   `correct()` (a linear target `deferred.frag` reads), or encode the increment
   through the same curve; roll off the *sum* (needs a copy of dst) — both wait
   on the gColor→Rgba16f item already open.

3. **The sky test breaks while the Bulb Placer is open.** Sky is
   `length(gPosition) <= 0.001` (`lamp_fog.frag:135-140`). gPosition is cleared
   with the global ClearColor (`modRender.vb:72-74`, all attachments), and
   `MapLampView.Render` (`:187`) leaves ClearColor at `(0.10, 0.11, 0.14, 1)`.
   With the placer open every sky pixel reads as a surface 0.204 m from the eye:
   from outside a lamp `t1 <= t0` discards, inside a lamp the march covers 20 cm.
   Shafts against the sky — where they are most visible — vanish. **Fix:**
   restore ClearColor in `MapLampView.Render` (as `MapMinimap` does), and make
   the sky test read the reversed-Z depth (`== 0`) or the gGMF sky flag instead
   of trusting a clear value.

4. **The shaft ignores the Lamp Shadows checkbox.** `MapLampFog.vb:61-63`
   `have_vol` tests `ready` and `depth_tex` only; `modRender.vb:1200-1204` zeroes
   `lamp_shadow_count` when `LAMP_SHADOW_ENABLED` is off. Unticking unshadows
   the pools and leaves the shafts carved — the "free A/B" (Window.vb:1970,
   shadows.md Controls) is not one for the shafts. Compound: a campath reload
   while the box is off sets `lights_dirty` but `modRender.vb:124` skips the
   bake, so the shafts keep sampling cubes baked for the *old* lamp positions
   until the box is re-ticked. **Fix:** include `LAMP_SHADOW_ENABLED` in
   `have_vol`.

5. **FrontFace can arrive flipped.** `MapDecals.vb:137` `GL.FrontFace(decal.winding)`
   per decal (Cw for negative-determinant matrices, MapLoader.vb:763-768); the
   restore block at `:187-193` does not put Ccw back, and nothing between the
   decal pass (`modRender.vb:231`) and the shaft pass (`:441`) resets it — the
   only resets are `:27` (frame start) and `:453` (after the shafts). On a map
   whose *last* decal is mirrored, `CullFace(Back)` keeps the sphere's NEAR
   faces and the shafts die the moment the camera enters a lamp — the bug the
   comment at `MapLampFog.vb:91-102` describes. `DeferredFog.frag:85`'s
   `gl_FrontFacing` discard inverts too. Order-dependent. **Fix:**
   `GL.FrontFace(Ccw)` at the top of `MapLampFog.Draw` (it sets every other
   state it depends on), or restore it in `draw_decals`.

6. **The global fog depends on the base rings to turn culling off.**
   `modRender.vb:422` `GL.Enable(EnableCap.CullFace)`; the only Disable before
   the fog box is `MapBaseRings.vb:22`, inside a routine that returns at `:15-17`
   when `BASE_RINGS_LOADED` is false (spaces without team bases). There the
   box's back faces are culled and its front faces are discarded by
   `DeferredFog.frag:85`, so the pass draws nothing. Hidden today because
   `light_fog` defaults to 0. **Fix:** `GL.Disable(CullFace)` in `global_fog`.

---

## Model errors — plausible output from the wrong model

7. **Density can only dim.** The integrand (`lamp_fog.frag:197-198`) has no
   scattering coefficient: `fog_density` appears only in the two extinction
   terms and the constant `fog_gain` stands in for σ_s. Raising "air density"
   makes every shaft shorter *and fainter*, never denser. `modGlobalVars.vb:879-880`
   and the tooltip at Window.vb:2010 say "thicker air and a shorter, denser
   shaft", which the code cannot produce. **Fix:** multiply the integrand by
   `fog_density` (σ_s ≈ σ_t for fog) and retune gain, or correct the two texts.

8. **Eye transmittance starts at the sphere, not the eye.** `:155` `trans = 1.0`
   at `t0` (sphere entry, `:127`). A lamp at 200 m and one at 20 m give shafts
   of identical intrinsic brightness while the ground under the far one has
   been fogged twice (height fog `:1510`, noise pass). `volumetric_fog.md` and
   the `:191-193` comment say both journeys are attenuated; only the inside of
   the sphere is. Partly a deliberate look choice — the 0.07/m medium exists
   nowhere else in the frame — but undocumented as such. **Fix:**
   `trans = exp(-fog_density * t0)`, or scale `lit` by the resolve's fogFactor.

9. **Pool and shaft disagree radially.** The shaft applies `to_lamp =
   exp(-density * dist)` (`:195`); `path_lights` (`deferred.frag:733`) applies no
   extinction at all. At the rim of a 20 m lamp that is an extra ×0.25 on the
   shaft, on top of falloff 8 (`LAMP_FOG_FALLOFF`) against 12
   (`PATH_LIGHT_FALLOFF`). The comment at `:167-168` claims shaft and pool agree
   about where light stops; only the `s = 1` zero is shared.

10. **Jitter moves the sample but not the transmittance.** The sample sits at
    `(i + jitter) * dt` (`:160`) while `trans` holds the value at the step
    START, so each Bayer cell scales its pixel by a fixed
    `exp(density * jitter * dt)`: about ±3 % on a 20 m lamp (dt 0.83 m), ±7 % on
    a 50 m lamp — a fixed 4×4 pattern in every smooth glow. The per-step
    `* dt` is also a point sample, not the integral `(1 - step_trans)/density`
    Frostbite uses (see `volumetric_fog.md`). Same fix as item 1.

11. **The constant depth bias grows as t² in metres.** `LAMP_SHADOW_BIAS`
    0.0015 in the 0..1 encoding is, for F = 20, N = 0.5, about 0.13 m at 6.5 m
    and about 1 m at 19 m (`dz/dt = F·N / ((F−N)·t²)`). Near the rim of a 20 m
    lamp, lit fog leaks up to a metre behind walls. shadows.md calls the bias
    "constant" without the distance behaviour. **Confirm:** `lampdebug` at a wall
    base near a lamp's range.

12. **The cube is baked from LOD 1.** `MapLoader.vb:102` `SHADOW_MAP_LOD =
    Math.Min(1, MAX_LOD_ID)` selects the draw list the lamp bake reuses
    (`MapLampShadow.vb:292-301`). The shaft's occluder is the fixture and post
    at 0.3–0.5 m (`lamp_fog.frag:31-33`), exactly the detail a LOD 1 mesh is
    likeliest to drop. Unverified. **Confirm:** `lampdebug` beside a fixture, or
    bake the lamp cubes from LOD 0 and diff.

---

## Edge cases, robustness, nits

- **Range clamp mismatch.** Bake far plane `Math.Max(range_m, 1.0)`
  (`MapLampShadow.vb:149`); both samplers use `Math.Max(0.1, range_m)`
  (`MapLampFog.vb:114`, `modRender.vb:1163`). Lamps authored under 1 m (allowed,
  MapCamPath.vb:87) compare against the wrong F.
- **`fogphase=` is unclamped** (`Program.vb:150`) while `foggain/fogfall/fogdens`
  are; the slider stops at 0.9. `fogphase=1` renders nothing (HG numerator 0),
  above 1 it subtracts light.
- **`normalize(d)` unguarded at the lamp position** (`lamp_fog.frag:189`);
  `deferred.frag:625` and `lamp_vol.comp:49` guard the same case.
- **No `GL_TEXTURE_CUBE_MAP_SEAMLESS`** anywhere; linear + compare on the cube
  array can hairline along the 12 face-edge planes.
- **`verify()` cannot detect handedness.** It reads the centre texel of the −Y
  face, which is invariant under a mirrored or rotated face; `MapLampShadow.vb:229-234`
  and shadows.md say it covers the up vectors. It does verify remap, encoding
  and that layer 3 looks down.
- **Bake from UI code.** Window.vb:1975 calls `Bake()` inside `SubmitUI`, which
  shadows.md and the modRender.vb:120-123 comment forbid.
- **`bake_ms` is submission time**, stopped before the first GPU sync
  (`verify(0)`); the "13-16 ms" in shadows.md/handoff is a CPU number.
- **Polygon offset value** (2.5, 8.0) is left set after the bake; only the
  enable is cleared.
- **`have_vol`** names the shadow cube; the volume it was named for is gone.

---

## Performance

- The `trans < 0.002` early-out (`:202`) is unreachable at defaults:
  0.943⁴⁸ = 0.06 for a full 40 m chord, and it is skipped on shadowed steps
  anyway. Every fragment runs all 48 steps.
- Inside a lamp the sphere covers the whole screen; overlapping lamps each add a
  full march. No half-res, no reuse.
- `bake_volumes` + `report_volume` still run on every re-bake with a synchronous
  readback; nothing samples `vol_tex` (already in shadows.md / handoff §4).

---

## The global fog pass (`MapFog` / `DeferredFog.frag`)

- Lines 103-112 reconstruct a world position from depth, then `:118` overwrites
  it; the depth binding and `invMVP` are dead work.
- `gColor.a` is never written (undefined); harmless while the target has no
  alpha and blending is off.
- With `fog_level > 0`, **both** the in-shader height fog (`deferred.frag:1482-1510`,
  hard-coded density 0.005 and 0.75) and the noise pass apply, toward three
  different tints: `0.75 * fog_tint`, `fog_tint`, `6 * noise * fog_tint`.
  `fog_alpha` (`:778`) is a tint multiplier, not a level.
- **Open question:** the height-fog mask travels through `outColor.a` → FXAA →
  the back buffer → `copy_default_to_gColor` → `deferred_mix.a`. The window asks
  for `AlphaBits = 0`; if the driver honoured that, alpha reads back as 1 and the
  noise fog degenerates to a `pow(1/2.2)` lift by `fog_level`. Yet the comment at
  `DeferredFog.frag:143-160` describes a NaN symptom that needed that alpha to
  be real. One `GL_FRAMEBUFFER_ATTACHMENT_ALPHA_SIZE` query on `GL_BACK_LEFT`
  settles it.

## Seen in passing, outside the fog

- The FXAA-off suspicion was WRONG. A capture with `fxaa=0` (run 0 of the
  2026-09-06 tuning session) rendered the full frame; the copy-back path is
  fed by something the audit did not trace. Struck, not proved.

## Resolved after the audit

- The alpha-mask question is settled by measurement: fog level 0.55 and 1.0
  rendered the same frame, a flat lift with no depth in it, exactly the
  degenerate case predicted. `DeferredFog.frag` now computes its own factor from
  gPosition (distance + height), fogs the sky as the far end of the ray, keeps a
  small adjustable noise, and takes a tint override; `fog_density`,
  `fog_height`, `fog_floor`, `fog_noise`, `fog_tint_r/g/b` are map settings.

---

## What was checked and holds

- The cube's perspective remap, encoding `z01 = (F − F·N/t)/(F − N)`, and its
  inversion are the same in `deferred.frag`, `lamp_fog.frag` and `lamp_vol.comp`.
- `CullFace(Back)` keeping the far faces (sphere wound inward vs Ccw) is
  correct while FrontFace is Ccw — see item 5.
- `BlendFunc` and depth state are restored to what the rest of the frame uses.
- Henyey-Greenstein is normalised; `cos_t = dot(normalize(lamp − p), rd)` is
  the forward-scatter convention.
- The roll-off is monotonic, continuous at the knee, never raises a channel and
  stays below 1 — for the shaft's own term (item 2 for the sum).
- The 12×18 inscribed sphere undercuts the analytic radius by ≤ 2.4 %; the
  lost contribution is under one 8-bit step at defaults.
- The per-lamp Y is resolved identically (`get_Y_at_XZ_fast + pos.Y`) in the
  bake, the surfaces and the shafts.

## Suggested order

Items 1, 3, 4, 5 and 6 are a few lines each and change what a viewer sees.
Item 7 is a tooltip fix or a retune. Items 2 and 8-10 are one design decision
about where scattering is added and in what space, best taken together with the
Rgba16f gColor item.
