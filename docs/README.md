# docs

Two kinds of document live here, and they age differently.

**References** describe how something works now. Fix them when the code
changes. **Handoffs** are session records - they are dated, they are written
mid-flight, and they are allowed to be wrong once the work moves on. Check a
handoff's status banner before trusting it.

## Start here

| you want | read |
|---|---|
| **what is still open** | `open_threads.md` |
| **the newest handoff: Path Studio - the lane navigator, the Edit path lock, street-lamp light controls, the 72-byte light record** | `HANDOFF_2026-09-09_path_studio.md` |
| **START HERE: the shading pass built, measured and REVERTED - what was learned, the patch, the pick-up** | `HANDOFF_2026-09-08_shading_pass.md` |
| **the newest handoff: bulb sprites, emissive panes, the bloom chain, banding on smoke** | `HANDOFF_2026-09-08_shading_pass.md` | environment specular, energy conservation, the Tank Exporter gloss/metal curves, Fresnel and wet-spec sliders: built, measured, reverted; the diff is `docs/patches/shading_pass_2026-09-08.patch` | **current** - read section 3 before touching the resolve |
| `HANDOFF_2026-09-08_bloom_and_bulbs.md` |
| the automated camera flight design | `camera_flight_plan.md` |
| **the newest handoff: the fog rebuilt, curves, monastery tuned** | `HANDOFF_2026-09-06_fog.md` |
| **start here for the next session** - terrain mixer, dirt, model AO, the finished Bulb Placer | `HANDOFF_2026-09-06_bulb_placer.md` |
| the lamps, shadows, shafts and Bulb Placer session before it | `HANDOFF_2026-09-06_lights.md` |
| current state of the renderer's shading and water | `HANDOFF_2026-08-31_pbr_glow_water.md` |
| how fire, smoke and glow are composited | `FX_PIPELINE.md` |
| **how surfaces are shaded, and where we differ from the game** | `lighting.md` |
| **anything that casts or receives a shadow** | `shadows.md` |
| light in the air: the lamp shafts against the published fog techniques | `volumetric_fog.md` |
| **what the lamp-shaft code gets wrong**, ranked, with fixes | `volumetric_fog_audit.md` |
| **adding or fixing an ImGui panel** | `ui_panels.md` |

**Shading work starts at `lighting.md`.** It covers `deferred.frag` end to end:
the resolve order, the two channel names that lie, both specular models and the
switch between them, a term-by-term table against the game's own BRDF, and a
ranked list of what is missing. Read it *before* `GAME_LIGHTING_MODEL.md` - that
one is the specification, this one is what we actually do with it.

## References

| document | subject |
|---|---|
| `FX_PIPELINE.md` | the FX pass: accumulation, HDR composite, glow, probe lighting, output dither, the lit smoke cards. **What is locked, and how to measure it.** |
| `fx_fog_interaction.md` | why the fog pass cannot fog the FX correctly - the order, the coverage hack, and two routes out. Nothing in it is implemented |
| `PARTICLES_HANDOFF.md` | the card particle *simulation* and emitter data (a reference despite the name) |
| `VFXBIN_PARTICLE_FORMAT.md` | the `.vfxbin` container - the single source for atlas rect ordering |
| `decals.md` | the decal pass, its two easily-confused shaders, and its tangent frame |
| `terrain_holes.md` | hole block format, the per-chunk X mirror, the map-wide mask |
| `terrain_blend.md` | how `t_mixer.frag` bakes eight terrain layers into VT pages |
| `game_PBS_tiled.md` | the game's `PBS_tiled` / `PBS_tiled_atlas` dirt curve and GCM modulation, transcribed; which material constant is which |
| `bulb_placer.md` | the Light Bulb Placer, the campath bulb table, how bulbs become per-instance lights, the driver-sized slot cap, the four cone shapes |
| `lighting.md` | `deferred.frag` - resolve order, channel traps, both specular models, and the term-by-term comparison against the game's BRDF |
| `shadows.md` | all three casters: the baked sun map, the baked lamp cubes, the parked cascades - and the traps they share |
| `volumetric_fog.md` | froxel grids, per-light marching and analytic airlight against `lamp_fog.frag`; ranked changes, with sources |
| `volumetric_fog_audit.md` | read-only audit of the shafts, the cube and both global fogs: 6 defects, 6 model errors, nits, perf, with line cites and how to confirm each; status banner says what is fixed |
| `tk_event_traps.md` | Path Studio's Tk pickers: why a click loaded the wrong map, six times over - write-back, a threaded rebuild, a modal inside the handler, a discarded click, a key the widget class had already eaten, and a two-pixel drag walking the selection down the list - the trace that found them and `tools/picker_click_test.py` that clicks badly on purpose |
| `ui_panels.md` | ImGui panels: where they live, the placement helpers, and why `imgui.ini` beats `FirstUseEver` |
| `shader_ide.md` | the in-app shader IDE: a tab per stage, the trial-compile that keeps a broken shader out of the frame, the revert guard, the overlay highlighter |
| `Tank Docs\` | the tank module - a vehicle loaded straight from the packages, kept out of the core: design paper, loader notes, verified formats |
| `map_settings.md` | per-map render settings: where they live, how they load |

## Decoded from the game

These describe **World of Tanks' own shaders**, not nuTerra's. They are
specifications and evidence, not descriptions of this renderer.

| document | subject |
|---|---|
| `GAME_LIGHTING_MODEL.md` | the game's `resolve_lighting` - BRDF, probe packing, `_GMM` |
| `terrain_blending_edge.md` | the game paints terrain **onto models**; it does not punch holes for trenches. Not implemented here |
| `game_PBS_tank.md` | the game's **tank** shader: G-buffer packing, material composite order, decal systems, and the permutation model |
| `game_deferred_decal.md` | the game's `deferred_decal.fx` |
| `game_water.md` | the game's WATER: its own G-buffer with a per-body id, flow-mapped six-tap normals, a half-res lighting pass with both shadow atlases, and the depth-ramp sub-pipeline |

## Handoffs, newest first

| document | covers | status |
|---|---|---|
| `HANDOFF_2026-09-08_bloom_and_bulbs.md` | texture views, the bloom flicker, lamp fog steps and noise, emissive lamp panes, the bulb sprite, output dither, and what the banding on smoke turned out to be | **current** - committed in seven pieces, see its top banner |
| `HANDOFF_2026-09-06_fog.md` | global fog rebuilt from gPosition, per-light falloff curves, the noise lessons, monastery tuned to a reference, the still runner | read its rules section |
| `HANDOFF_2026-09-06_lights.md` | lamp lighting, shadows, volumetric shafts, the light catalogue and the Bulb Placer | same day, earlier; its open items are settled in section 4 and the fog handoff |
| `HANDOFF_2026-08-31_pbr_glow_water.md` | PBS_tank decode, PBR specular, glow depth-test, pooled water | still accurate for what it covers |
| `HANDOFF_2026-08-31_fx_and_holes.md` | terrain holes, FX HDR composite, glow, the parked branch landing, the decal checkerboard | earlier the same day |
| `HANDOFF_2026-08-28_lighting.md` | the lighting/probe-grid session | **historical** - read its section 9 first; much of sections 7-8 was reverted |
| `HANDOFF_sun_shadow.md` | the sun shadow bake, flicker hunt, outland cull | older, still largely accurate; superseded as a reference by `shadows.md` |
| `FX_plan.md` | original FX recon | **historical** - the plan part is done |

## House rules these documents assume

- **Measure, then claim.** Several entries here exist because a plausible
  mechanism was written down before it was tested and turned out to be wrong.
  Where a document states a number, it usually also states the command that
  produced it.
- **Run the null control.** Same build, twice, same camera. If that is not 0
  pixels, the comparison cannot support a conclusion. This has caught two
  confident wrong answers.
- **Shaders validate at runtime only.** A clean build says nothing about GLSL;
  check stdout for `Shaders Built.` and `didn't compile`.
- **Shader source is `nuTerra/shaders/`.** `bin/.../shaders` is build output.
