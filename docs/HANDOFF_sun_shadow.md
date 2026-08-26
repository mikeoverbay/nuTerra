# Handoff — renderer state

Supersedes the previous version. Covers everything through the water system,
tessellation, tree LODs, wetness/SSR, the camera damping rewrite, and the
first working FX pass (volumetric smoke). Where it says "was", that is what
the code did before and why it changed.

The owner is the sole developer of nuTerra, a VB.NET / .NET 6 / OpenTK offline
World of Tanks map viewer. He guides tightly, tests every build himself, and is
usually right when he says something looks wrong. Believe the screenshot over
the reasoning.

---

## Where this left off

- Base commit `87af744` ("Handoff: session close-out"); the working tree holds
  ALL of 2026-08-25/26's work UNCOMMITTED: FX Stage 0 (volumetric meshes),
  camera damping, per-map `mouse_damp`, minimap eviction fix, fog/ClearColor
  fixes, FX diagnostics in Snapshot. Commit/push from the owner's terminal -
  the agent shell has no SSH key.
- Verified by the owner: the big vista smoke renders and animates. Pending his
  eyes: the base smoke sheets (fixed last - alpha-gain default, see FX below),
  and general FX look.
- **Terrain holes: NOT implemented.** Was fully built on 2026-08-25 and
  reverted by the owner the same day. Everything learned is under Terrain
  below - a re-implementation is one day and one critical one-character fix.
- `docs/FX_plan.md` - the staged particles/FX plan with every format that was
  cracked (BWPs placements, effbin, vfxbin recon). Stage 0 of it is what is in
  the tree; Stage 1 (BWPs markers) is the natural next step.
- The owner's per-map hand tunes (fog level, lighting, water trims) were LOST
  on 2026-08-25 - an agent `rm -rf` hit the work MapSettings folder (Git Bash
  maps `/tmp` to `%TEMP%`; the "stray" copy was the live one). The folder
  reseeds from shipped baselines, and the shipped baseline has `fog_level=0`
  on all 65 maps - so every map currently has NO FOG until re-tuned
  (Settings -> Lighting Settings -> Fog Level, then save).

---

## How to work in this repo

- The repo is `C:\nuTerra`; the owner opens `C:\nuTerra\nuTerra.sln` in
  **VS 2022** and runs Debug|Any CPU -> `bin\Debug\net6.0-windows`. The stale
  clone at `C:\Users\...\source\repos\mikeoverbay\nuTerra` is dead - never
  touch it. When the owner "sees no changes", the answer is a stale OUTPUT:
  rebuild every folder he might launch (x64 Release publishes included).
- **Kill the running exe before building.** `Get-Process nuTerra | Stop-Process -Force`.
- Build: `MSBuild.exe nuTerra.sln /t:Build /p:Configuration=Debug /p:Platform="Any CPU"`
  from `C:\nuTerra`. Publishing needs `/p:Platform=x64 /p:RuntimeIdentifier=win-x64`
  plus `/restore` (the FolderProfile pubxml sits in "My Project" where the SDK
  never finds it - pass properties explicitly).
- `nuTerra.exe <space_name>` (e.g. `101_dday`) loads that map straight away.
  Owner's rule: only agent launches use it; VS launches get the map picker.
- **Shaders only validate at runtime.** Launch and check stdout for
  `Shaders Built.` / `didn't compile`.
- **Run with the working directory set to the bin folder** - `ShaderLoader`
  resolves `shaders` relative to the CWD.
- **Console output is buffered when redirected.** The **Snapshot** button
  flushes; it now also prints the four model cull buckets
  (`opaque/dbl/glass/fx`) and `glGetError` - the first stop for any "FX broke
  something" report. Per-pass GPU times live in the **Stats** panel.

Git specifics that cost real time here:

- A tracked file cannot be gitignored; `git rm --cached` AND the rule.
- Checkout silently overwrites ignored files.
- Scratch screenshots `/t[0-9].png` are ignored; shipping images go in
  `readme_images/`.

## Engine-wide traps

| trap | detail |
|---|---|
| Clip control | `ZeroToOne` + `DepthClamp` + reversed-Z (`ClearDepth 0`, `Greater`). Any projection you build must be remapped. |
| `gPosition` is VIEW space | Every writer names the varying `worldPosition`; every one stores `view * model * vertex`. (`t_mixer.vert` IS world.) |
| Shader includes | `#include "common.h" //! #include "../common.h"` - the `//!` half is an editor hint. |
| DX index flip | Universal triangle reversal; skinned formats (iiiww) get their winding restored after the format is known. |
| View-ray vs vertical | Water/depth comparisons measure the VERTICAL column, never the view ray. |
| **OnUpdateFrame spins at ~127,000 calls/s** | UpdateFrequency 0 + IsMultiThreaded. `DELTA_TIME` is the RENDER frame's dt and is wrong there by ~600x. Anything time-based in the update loop must run its own Stopwatch (see `rot_clock`). Three damping attempts felt "direct 1:1" before this was measured. |
| **GL globals leak between passes** | `ClearColor`, `BlendFunc`, `DepthTest`, `CullFace` are process-global. The minimap left ClearColor navy for the whole app's life; the FX pass leaving DepthTest on wiped every frame to that navy (FXAA's fullscreen quad failed reversed-Z against cleared depth). Every pass must restore what the next stretch assumes: post-water runs depth-test OFF, blend func (SrcAlpha, 1-SrcAlpha). |
| **New ImGui windows go in Window.vb's UI pass** | A window opened from modRender's HUD section never appears. Also always SetNextWindowPos/Size when a toggle turns on - a first-open window is an invisible sliver otherwise. |
| **VRAM pressure eats write-once caches** | At ~5.6/8 GB the driver evicts; evicted texture content comes back UNDEFINED. The minimap's cached pre-render died this way (white square) - it re-renders per frame now. Never trust a rendered-once texture to survive. `TARGET_TEXEL 0.25` reclaims ~1.9 GiB from the shadow bake if headroom is needed. |

---

## FX (new - Stage 0 of docs/FX_plan.md)

**Volumetric GFX meshes render.** `shaders/custom/volumetric_effect[.|_vtx.|_layer_vtx.]fx`
materials (smoke columns, flame sheets) route to `ShaderTypes.FX_volumetric = 11`:

- **Cull bucket 4**: cull.comp routes `shader_type == 11` into `indirect_fx`
  (SSBO binding 7, atomic counter at parameters offset 12,
  `numAfterFrustum(3)`). The FX check comes FIRST - these materials are also
  double-sided and would otherwise vanish into the opaque dbl bucket.
- **Forward pass** `MapStaticModels.draw_fx`, called in modRender after water:
  lit frame in gColor, scene depth live, premultiplied blend
  (ONE, 1-SrcAlpha) so alpha and additive materials share one multidraw
  (additive outputs `(rgb*a, 0)`). Restores DepthTest OFF and the
  conventional BlendFunc on exit - both were hard-won (see traps).
- **Shaders** `Model_shaders/volumetric.vert/.frag` are a TRANSCRIPTION of the
  game's compiled `volumetric_effect_vtx` fxo. The `.fxo` is a ZIP; its
  `effect` entry holds DXBC blobs findable by magic; `fxc /dumpbin` (Windows
  Kits) disassembles them with full reflection - parameter names, offsets,
  defaults. This is the proven path for any game shader question.
- **Material params ride generic GLMaterial vec4 slots** - the mapping is
  commented identically in MapLoader.load_materials and volumetric.vert; keep
  in lockstep. diffuse=map1, distortion(velocity)=map2.
- **The "colour" vertex stream is load-bearing**: RGBA8 per vertex
  (PrimitiveLoader.load_primitives_colour -> vertsColour buffer, VAO attrib 6).
  The alpha shapes the whole silhouette: `alpha = sat((texA + vertA*fade - 1) * gain)`.
  Meshes WITHOUT the stream must read WHITE (buffer is white-initialised) -
  a zero default makes them invisible by construction.
- **Unauthored `alphaFadeAmountFresnel` defaults to gain 1** (1,1,1,0). The
  compiled register default is gain 0 - that default is dead on arrival and
  made `vista_smoke_01` (the base smoke) invisible while `_02` (authoring
  gain 1) worked.
- The UV warp trick, kept verbatim from the game: warp amount scales with
  `|alphaOffset - vertexAlpha|`, so transparent edges billow harder than the
  core. `FX_TIME` (seconds, wraps hourly) drives all scrolling.
- Diagnostics: load logs every volumetric material (textures ok/MISSING -
  missing demotes to unsupported rather than sampling an invalid bindless
  handle), every unknown material property, and every unsupported-fx model
  with its name. Snapshot prints bucket counts + glGetError.
- NOT done: backdrop distortion (heat haze needs the composed frame), skinned
  volumetrics, fog coupling (fog mask rides gColor.a and smoke now writes
  alpha there - if fog misbehaves inside plumes, colour-mask the FX pass's
  alpha writes).

**Cracked FX data (see docs/FX_plan.md for the full chain):** BWPs section =
288 ambient effect placements on Overlord, 80-byte records {4x4 matrix, BWST
string key of `particles/environment/.../*.eff`, flags, 0.1f}; `.effbin` =
forward/deferred `.vfxbin` path pair; `.vfxbin` = WG typed records (LOD blocks,
emitters, curves) - schema not cracked. Overlord authors NO GFX_models
placements; its per-map particles.xml registers 666 effect bins.

## Camera

Mouse feel is the three.js OrbitControls damping model, transcribed: input
accumulates into a pending delta per axis; each frame the camera takes
`dampingFactor` of the pool and the rest decays. One mechanism = smoothed drag
AND release coast. Rotate, zoom (log-domain exponent pool - sign can never
flip), and pan (world-space pool, so a coast holds its heading) all ride it.
dt comes from `rot_clock`, NOT DELTA_TIME (see the 127 kHz trap). The knob is
`mouse_damp` - per-map settings key, default 0.1, live slider under
Settings -> Camera. 1.0 = the old direct response. The old per-event handler
(dead zones, sin shaping, misbound single-line If/Else) is gone.

## Shadows

Unchanged from the previous handoff and still true: the map-wide bake is the
only caster (terrain + static models + alpha-tested trees), sampled in
deferred.frag on the sun term only. Bake() after set_light_pos(). Live
cascades off but intact. MSM behind the A/B toggle. Bake size gated by
`TARGET_TEXEL` (0.05 -> pinned 32768^2 = 2 GiB; 0.25 reclaims ~1.9 GiB and
likely looks identical - now also relevant as VRAM headroom, see traps).

## Terrain

Layer mixing, tessellation (60 m envelope, AABB distance), wetness/SSR: all as
before - height blend threshold 0.05 weighted by splat twice, dominant layer
supplies the whole surface response, AG normal maps mixed on all four
channels, layer projection from true world position. Sun needs sun, geometry
does not.

**Terrain holes - data cracked, implementation REVERTED (owner's call).**
`terrain2/holes` per chunk: `"zip\0" u32 u32-size` zlib -> `"hol\0" 64x64
ver=1` -> 64x64 1-bit mask. What the build-and-revert taught:

- `get_holes`' `63 - ((x1*8)+q)` X-mirror is WRONG. Proven offline: decode all
  chunks in Python straight from the pkg, composite on the gui minimap - only
  bit-index-ascending = +X, row-ascending = +Z lands features on authored
  lines (wrong orientations paint the out-of-map border). Drop the `63 -`.
- The owner states the authored holes are NOT the trench cutouts he needs on
  Overlord; what punches terrain under trench models is still an open
  question (their visuals carry no cut flag - only a shaderless `s_ramp_0`
  collision group).
- The reverted implementation shape, if wanted again: map-wide R8 stamped at
  `g_uv_offset` origins, white=render, nearest; discard only in a
  TERRAIN_HOLES compile variant of LQ/HQ (a discard statically kills early-Z,
  so only chunks with holes pay); never in the shadow bake; never geometry.

**VT (virtual texture) - found and REVERTED with the same sweep, must-know:**
the port is Brad Blanchard's demo (bgfx examples/40-svt is the same code -
diff against it, not memory). Its stability rests on AddRequestAndParents +
coarse-first sort. A prebake/pin/burst speed-up broke that and was reverted.
One REAL divergence was found: the feedback pass subtracts MipBias when
requesting but the terrain shaders did NOT subtract it when sampling - the
fix (subtract the same bias in TerrainLQ/HQ.frag) was verified working and
then swept out in the revert. Whoever touches VT next should re-apply that
one first. Settle pace = "Uploads per frame" (ctor-bound; Rebuild VT).

**Still uncracked**: `terrain2/horizonshadows`.

## Water / Models / Trees / Per-map settings

All as the previous handoff: BWWa fully parsed (sun tint at +0xE0 is not a
reflection tint; `cBWWa` must not be freed in cleanup), corner-quad bodies,
vertical shore fade and boat mask; skinned winding restored; SHADOW_MAP_LOD
junk-LOD gap still open; trees bucket by authored LOD profile with
mip-compensated alpha test; per-map settings fall back to startup defaults
(`mouse_damp` rides this now; a missing key means the startup default, which
is why adding a setting is one Yield in modMapSettings.Fields).

Open from before: trees visible through a building (depth/G-buffer).

## One piece of advice

Four revisions of this file and it is still the only rule that matters: every
real bug here fell to an instrument, never to an argument. This session alone:
three "broken" damping attempts were one measurement away from the 127 kHz
loop; the "FX crash" was a counter line and a glGetError away from being a
depth-test leak plus an evicted cache; the invisible smoke was one material
dump away from a dead register default; and the fog was not a bug at all but
a knob a deleted settings file had reset. When something looks wrong,
instrument it - and read what the instrument already said.
