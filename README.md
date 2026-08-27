# nuTerra

[![MSBuild Actions Status](https://github.com/mikeoverbay/nuTerra/workflows/MSBuild/badge.svg)](https://github.com/mikeoverbay/nuTerra/actions)

## World of Tanks map offline viewer writen in VB.NET using Visual Studio 2022 (NET 6.0).

![nuTerra](readme_images/nuTerra.png)

## Highlights

- Deferred renderer with reversed-Z, virtual-textured terrain (the game's
  own layer mixing baked into pages on demand), baked map-wide sun shadow
  with an MSM/PCF A/B path, water, trees, roads, decals and the game's
  volumetric FX pass with faithful shader-variant selection.
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