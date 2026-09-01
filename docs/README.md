# docs

Two kinds of document live here, and they age differently.

**References** describe how something works now. Fix them when the code
changes. **Handoffs** are session records - they are dated, they are written
mid-flight, and they are allowed to be wrong once the work moves on. Check a
handoff's status banner before trusting it.

## Start here

| you want | read |
|---|---|
| current state of the renderer | `HANDOFF_2026-08-31_fx_and_holes.md` |
| how fire, smoke and glow are composited | `FX_PIPELINE.md` |
| how the deferred resolve is ordered | `lighting.md` |

## References

| document | subject |
|---|---|
| `FX_PIPELINE.md` | the FX pass: accumulation, HDR composite, glow, probe lighting. **What is locked, and how to measure it.** |
| `PARTICLES_HANDOFF.md` | the card particle *simulation* and emitter data (a reference despite the name) |
| `VFXBIN_PARTICLE_FORMAT.md` | the `.vfxbin` container - the single source for atlas rect ordering |
| `decals.md` | the decal pass, its two easily-confused shaders, and its tangent frame |
| `terrain_holes.md` | hole block format, the per-chunk X mirror, the map-wide mask |
| `terrain_blend.md` | how `t_mixer.frag` bakes eight terrain layers into VT pages |
| `lighting.md` | `deferred.frag` - the order of the resolve, which is where its bugs were |
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
| `HANDOFF_2026-08-31_fx_and_holes.md` | terrain holes, FX HDR composite, glow, the parked branch landing, the decal checkerboard | current |
| `HANDOFF_2026-08-28_lighting.md` | the lighting/probe-grid session | **historical** - read its section 9 first; much of sections 7-8 was reverted |
| `HANDOFF_sun_shadow.md` | the sun shadow bake, flicker hunt, outland cull | older, still largely accurate |
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
