# nuTerra

[![MSBuild Actions Status](https://github.com/mikeoverbay/nuTerra/workflows/MSBuild/badge.svg)](https://github.com/mikeoverbay/nuTerra/actions)

## World of Tanks map offline viewer writen in VB.NET using Visual Studio 2022 (NET 6.0).

![nuTerra](readme_images/nuTerra.png)

## Highlights

- Deferred renderer with reversed-Z, virtual-textured terrain (the game's
  own layer mixing baked into pages on demand), baked map-wide sun shadow
  with an MSM/PCF A/B path, water, trees, roads, decals and the game's
  volumetric FX pass with faithful shader-variant selection.
- **Fire, smoke and glow.** CPU billboard particles driven by the game's own
  `.vfxbin` effect data, composited with the volumetric FX meshes into a float
  buffer so hot fire rolls off with its hue instead of clipping to white, plus
  a bloom pass built from the over-range energy.
- Game-faithful **outland** backdrop: tilemap albedo baked at load the way
  the game's engine does it, ring meshes with the playfield cut out,
  heightmap data-welds that keep the sheet mathematically under the
  terrain (audited on every load), and background threshold decimation
  that drops the two cascades from ~4M to well under 1M triangles with
  sub-metre error - the load path never blocks, the full grid draws until
  the decimated mesh swaps in.
- GPU frustum + raster occlusion culling for models, CPU block culling for
  terrain and outland, and instruments everywhere: per-pass GPU timers, a
  console Snapshot, wireframe overlays, a VT page debug view with a colour
  key, and load-time audits that print what the data actually says.

The decimated outland in the wireframe view - dense where the terrain is,
sparse where it is not:

![Outland decimation](readme_images/outland%20decimation%20test.png)

## Documentation

`docs/` carries the reverse-engineering notes and the per-subsystem references.
Start with the handoff for whichever area you are touching.

| doc | what it covers |
|---|---|
| `HANDOFF_2026-08-31_fx_and_holes.md` | Most recent. Terrain holes, FX compositing, glow |
| `FX_PIPELINE.md` | The FX pass end to end - accumulation, composite, glow, what is locked |
| `PARTICLES_HANDOFF.md` | Card particle simulation and emitter data |
| `VFXBIN_PARTICLE_FORMAT.md` | The `.vfxbin` effect format, cracked from the packages |
| `terrain_holes.md` | Hole block format, the per-chunk mirror, the mask and the discard |
| `terrain_blending_edge.md` | How the game merges models into terrain - a post-pass, NOT holes |
| `terrain_blend.md` | The game's eight-layer terrain blend, transcribed from its VT baker |
| `GAME_LIGHTING_MODEL.md` | The game's lighting model as read out of its shaders |
| `HANDOFF_2026-08-28_lighting.md` | Lighting work - probes, SH grid, tonemapping |
| `HANDOFF_sun_shadow.md` | Baked sun shadow, MSM/PCF |
| `lighting.md`, `game_deferred_decal.md` | Older notes on lighting and decals |
| `map_settings.md` | The per-map settings files and what persists |
| `FX_plan.md` | Historical recon of the FX data chain. Superseded, kept for the format work |

A note on how these are written: they record what was **measured**, and they
call out claims that were believed and later disproved rather than quietly
deleting them. If a doc says something is settled, it says how it was settled.

## Third-party credits

Algorithms adapted from other projects and papers:

- **three.js** - the camera damping model follows OrbitControls
  (MIT). <https://github.com/mrdoob/three.js>
- **Moment Shadow Mapping** - Peters & Klein, i3D 2015, for the MSM
  sun-shadow path.
- **Inigo Quilez** - the smoothed terrain normal generation follows
  <https://iquilezles.org/articles/normals/>
- **Garland & Heckbert** - the outland mesh decimation follows their
  quadric error metric (Surface Simplification Using Quadric Error
  Metrics, SIGGRAPH 1997), threshold-driven with subset placement.

Libraries:

- [OpenTK](https://github.com/opentk/opentk) - OpenGL bindings and math
- [Dear ImGui](https://github.com/ocornut/imgui) via
  [ImGui.NET](https://github.com/ImGuiNET/ImGui.NET) - UI
- [AssimpNet](https://bitbucket.org/Starnick/assimpnet) - model import
- [DotNetZip](https://github.com/haf/DotNetZip.Semverd) - package (.pkg) access
- [Pngcs](https://github.com/leonbloy/pngcs) - 16-bit PNG decoding