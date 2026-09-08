# Why the FX and the fog fight

Walked 2026-09-08 from the frame order in `RenderEngine/modRender.vb` and the
two shaders that meet here, `shaders/PostProcessing/DeferredFog.frag` and
`shaders/Final_render/fx_composite.frag`.

**Nothing in this document is implemented.** It is a diagnosis and two routes.

Claims are marked. **READ** means taken directly off the code cited.
**REASONED** means derived from what the code does, not observed on screen.

## The headline

**The fog pass does not know where the smoke is.** It computes fog from
`gPosition`, and no FX card writes `gPosition`. By the time it runs, the smoke
has already been composited into `gColor`, so the fog is applied to smoke pixels
using the distance of whatever *solid surface sits behind them*.

There is already a hack that tries to cover this. It is one knob choosing
between two wrong answers, and neither is the right one.

## The order

**READ**, `modRender.vb`:

| Step | What | Where it writes |
|------|------|-----------------|
| 1 | particles (smoke), `draw_fx` (fire), lamp panes, bulb sprites | `gFX_HDR` |
| 2 | `build_fx_glow()` | `gFX_BloomA` |
| 3 | `composite_fx()` | **`gColor`** |
| 4 | FXAA, glass pass, `base_rings` | `gColor` |
| 5 | **`global_fog()`** | `gColor` |
| 6 | `lamp_fog.Draw()` (shafts) | `gColor` |

The FX are in `gColor` at step 3. The fog arrives at step 5 and cannot tell
them apart from the scene.

## What the fog keys off

**READ**, `Scene/MapFog.vb` — *"Distance and height come from gPosition in the
shader"*. `DeferredFog.frag:141-157`:

    vec3  vpos = texture(gPosition, uv).rgb;    // VIEW space
    float dist = length(vpos);
    bool  sky  = dist < 0.001;
    float f    = sky ? fog_sky : 1.0 - exp(-fog_density * dist);

`gPosition` is written by solid geometry only. FX cards write colour into
`gFX_HDR` and nothing else.

## The existing hack

**READ**, `DeferredFog.frag:192-194`:

    // Smoke and fire are in front of whatever they cover; fog them by their
    // coverage, not by the sky behind them.
    f *= 1.0 - clamp(texture(gFX_cover, uv).a, 0.0, 1.0) * fx_cover;

`gFX_cover` is `gFX_HDR` bound at unit 4 (`MapFog.vb:60`), so its alpha is the
FX buffer's **accumulated coverage**. `fx_cover` is 1.0 whenever the FX block
ran that frame, 0.0 otherwise (`MapFog.vb:61`) — a staleness gate, not a
tuning value.

## Why it cannot be tuned right

Four consequences. 1, 2 and 4 are **REASONED**; 3 is **READ**.

1. **Fully covered smoke gets zero fog.** At `a = 1` the fog amount is scaled
   to nothing. A plume 300 m out renders at full contrast while the scene
   around it is washed to the fog tint — it pops *out* of the haze it should
   be buried in.

2. **Thin smoke gets the wrong fog.** At `a = 0.3` it takes 70% of the
   *background's* fog, computed from the wall or sky behind it rather than
   from its own distance. Near smoke against far geometry is heavily
   over-fogged.

3. **Additive FX get no coverage at all.** The FX contract is premultiplied
   colour with **alpha 0** for anything additive — stated in
   `lamp_bulb.frag`, `model_lamp_glow.frag` and `lamp_fog.frag`. So
   `gFX_cover.a` is 0 for fire, lamp panes and bulb sprites, and every one of
   them receives the **full background fog**, mixed toward the tint by the
   depth of whatever is behind. The comment above the hack says it handles
   "smoke *and fire*"; it cannot — fire contributes no coverage to read.

4. **A seam slides through the plume.** As smoke drifts across a rooftop edge
   the background depth jumps from the roof to the sky, so `f` jumps with it
   and a hard fog boundary moves *through* the smoke.

The correct fog for a smoke fragment depends on **the smoke's own distance**.
Step 5 never has it, so no value of `fx_cover` is right.

## Route A — fog the cards, keep the order

Copy the distance/height/noise fog term into `particle.frag` and
`volumetric.frag` (and the lamp pane and bulb shaders), so each card fogs
itself by its own world position. Leave `fx_cover` at 1 so the fullscreen pass
skips covered pixels.

* Contained to shaders. No frame reorder.
* Fixes smoke completely.
* **Does not fix consequence 3.** Additive FX carry no coverage, so the
  fullscreen pass still gives them a second, wrong dose.
* Third copy of the fog function. There is precedent — `lamp_fog.frag` already
  carries one, *"Copied from DeferredFog.frag so both passes see one cloud"* —
  but a shared include in `common.h` would be better than a third divergent
  copy. `common.h` has no fog function today, only `fog_tint` and `fog_level`
  in the properties block.

## Route B — move the composite past the fog

Fog the cards as in A, move `composite_fx()` to **after** `global_fog()` so the
fullscreen pass only ever sees solid geometry, then delete `gFX_cover`,
`fx_cover` and the hack entirely.

* Correct by construction, for smoke and additive FX alike.
* Removes a knob that cannot be set correctly.
* Moves the FX past FXAA, the glass pass and `base_rings`. Each needs checking:
  `composite_fx` currently lands before them and something may depend on that.
* `modRender.vb` carries an explicit ordering warning in this block already
  (*"DO NOT move the particle call back above draw_fx — the fire-after-smoke
  rule breaks silently, with no error"*), so this area has bitten before.

## Related, not the same

`fx_composite.frag`'s glow-occlusion test has its reversed-Z comparison
backwards — see the note in `open_threads.md`. Different bug, same file.
