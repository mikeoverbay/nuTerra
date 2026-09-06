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
| the automated camera flight design | `camera_flight_plan.md` |
| **the newest handoff: the fog rebuilt, curves, monastery tuned** | `HANDOFF_2026-09-06_fog.md` |
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
| `FX_PIPELINE.md` | the FX pass: accumulation, HDR composite, glow, probe lighting. **What is locked, and how to measure it.** |
| `PARTICLES_HANDOFF.md` | the card particle *simulation* and emitter data (a reference despite the name) |
| `VFXBIN_PARTICLE_FORMAT.md` | the `.vfxbin` container - the single source for atlas rect ordering |
| `decals.md` | the decal pass, its two easily-confused shaders, and its tangent frame |
| `terrain_holes.md` | hole block format, the per-chunk X mirror, the map-wide mask |
| `terrain_blend.md` | how `t_mixer.frag` bakes eight terrain layers into VT pages |
| `game_PBS_tiled.md` | the game's `PBS_tiled` / `PBS_tiled_atlas` dirt curve and GCM modulation, transcribed; which material constant is which |
| `lighting.md` | `deferred.frag` - resolve order, channel traps, both specular models, and the term-by-term comparison against the game's BRDF |
| `shadows.md` | all three casters: the baked sun map, the baked lamp cubes, the parked cascades - and the traps they share |
| `volumetric_fog.md` | froxel grids, per-light marching and analytic airlight against `lamp_fog.frag`; ranked changes, with sources |
| `volumetric_fog_audit.md` | read-only audit of the shafts, the cube and both global fogs: 6 defects, 6 model errors, nits, perf, with line cites and how to confirm each; status banner says what is fixed |
| `ui_panels.md` | ImGui panels: where they live, the placement helpers, and why `imgui.ini` beats `FirstUseEver` |
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

## Handoffs, newest first

| document | covers | status |
|---|---|---|
| `HANDOFF_2026-09-06_fog.md` | global fog rebuilt from gPosition, per-light falloff curves, the noise lessons, monastery tuned to a reference, the still runner | **current** - read its rules section |
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
