# Handoff — renderer state

Supersedes the previous version. Covers everything through the water system,
tessellation revival, tree LODs, wetness/SSR, and the boat fixes. Where it
says "was", that is what the code did before and why it changed.

The owner is the sole developer of nuTerra, a VB.NET / .NET 6 / OpenTK offline
World of Tanks map viewer. He guides tightly, tests every build himself, and is
usually right when he says something looks wrong. Believe the screenshot over
the reasoning.

---

## Where this left off

- `master` = `terrain-color-fix` = the same commit; everything is committed,
  working tree clean. Pushing needs the owner's terminal - the agent shell has
  no SSH key for the remote - so run `git push -u nuTerra master` there.
- Verified by the owner this session: tessellation (on, 60 m envelope), the
  boat winding fix, the waterline pinned by the vertical-depth metric.
- **Next feature queued: terrain holes.** The data format is fully cracked and
  documented below; the implementation plan is three steps and half a day.
- `readme_images/` holds front-page screenshots; the README references
  `readme_images/nuTerra.png`. Overwriting that file updates the front page
  with no README edit.

---

## How to work in this repo

- **Kill the running exe before building.** `Get-Process nuTerra | Stop-Process -Force`.
- Build: `MSBuild.exe nuTerra.sln /t:Build /p:Configuration=Debug /p:Platform="Any CPU"`
  from `C:\nuTerra`. Publishing needs `/p:Platform=x64`.
- **Shaders only validate at runtime.** A clean build proves nothing about GLSL.
  Launch and check stdout for `Shaders Built.` and `didn't compile`.
- **Run with the working directory set to the bin folder**, not the repo root -
  `ShaderLoader` resolves `shaders` relative to the CWD.
- **Console output is buffered when redirected.** The **Snapshot** button on the
  menu bar writes the current render state and flushes, which is the only way
  the log is readable while the app runs. Per-pass GPU times live in the
  **Stats** panel (GL_TIME_ELAPSED, read a frame late so asking does not stall
  the pipeline being measured).

Git specifics that cost real time here:

- **A tracked file cannot be gitignored.** t1.png sat in `.gitignore`-adjacent
  limbo showing as modified forever; the fix is `git rm --cached` AND the
  ignore rule - either alone does nothing.
- **Checkout silently overwrites ignored files.** Switching branches destroyed
  the local t1.png without a warning, because ignored files get no overwrite
  protection. Anything ignored-but-precious does not belong in the repo tree.
- Scratch screenshots at the repo root (`/t[0-9].png`) are ignored; images
  meant to ship go in `readme_images/`.

## Engine-wide traps

| trap | detail |
|---|---|
| Clip control | `ZeroToOne` + `DepthClamp` + reversed-Z (`ClearDepth 0`, `Greater`). Any projection you build must be remapped; wrong ones flatten to 0.0 instead of disappearing. |
| `gPosition` is VIEW space | Every writer names the varying `worldPosition`; every one stores `view * model * vertex`. Pass `invView * P` when you need world. (`t_mixer.vert` worldPosition IS world - no camera in that pass.) |
| Shader includes | `#include "common.h" //! #include "../common.h"` - the `//!` half is an editor hint. Changing it breaks the loader. |
| DX index flip | The index loader reverses every triangle for DX-to-GL. **Skinned formats (iiiww family) ship with the opposite winding** and get their order restored after the vertex format is known (`PrimitiveLoader`). Boats are skinned; that is why they were inside out. |
| View-ray vs vertical | Any water/depth comparison measured along the view ray changes with the camera by construction. The water shore fade and boat mask measure the VERTICAL column for exactly this reason - the view-ray version made boats appear to sink as the camera moved. |

---

## Shadows

**The map-wide bake is the only caster.** Terrain, static models, and trees
(alpha-tested, so leaf-shaped) all render into one depth bake at map load,
sampled per frame in `deferred.frag` on the sun term only - never albedo,
never ambient. Ortho box fitted to the terrain footprint (derived from the
same expressions PageLoader uses - the axes are asymmetric, do not hand-derive)
and squared up; near/far bracket the geometry.

- `Bake()` must run AFTER `set_light_pos()`. It once ran before: LIGHT_POS was
  zero, the matrix went NaN, and the log printed `expected~NaN` for a whole
  session while the camera maths was rewritten around it. Read the instrument.
- Live cascades are **off** (`USE_SHADOW_MAPPING = 0`) with controls removed.
  The pass, FBO and shaders remain; restore the two controls and the flag when
  tree animation lands. Splits were 20/75/250 when last live and must match
  `cascadePlaneDistances` in common.h.
- Moment Shadow Maps behind an A/B toggle (Settings -> Shadow Mapping), with
  penumbra clip lo/hi applied to BOTH paths so the comparison is fair.
- Bake size is gated by `TARGET_TEXEL`, not `MAX_SIZE`. Currently 0.05 ->
  pinned at 32768^2 16-bit = 2 GiB. Measured: 8192 vs 16384 showed no visible
  difference - the sharpness came from moving sampling out of the VT page.
  `TARGET_TEXEL = 0.25` reclaims ~1.9 GiB and likely looks identical.

## Terrain

**Layer mixing** (`t_mixer.frag`), matching the game's behaviour:

- Height blend is a threshold, not a crossfade: contenders within **0.05** of
  the tallest survive, weighted by splat TWICE. The map-authored BWT2
  blendHeight (~0.3) is NOT the mix threshold - feeding it in was the
  washed-out terrain.
- The dominant layer supplies the whole surface response - normal, specular,
  AO. Normals are argmax, never averaged (averaging flattens exactly at
  transitions).
- Normal maps are AG format; the macro blend must mix ALL FOUR channels or X
  and Y of the normal come from different textures at distance.
- Layer projection: `uv = (dot(U.xyz, wp_game), -dot(V.xyz, wp_game)) + 0.5`,
  true world position, height included, W ignored. The synthetic chunk-local
  point it replaced displaced the textures three ways at once - no axis flip
  could fix it because nothing was mirrored.

**Tessellation** is on by default and persists (`My.Settings.use_tessellation`).
Envelope matches the game: HQ within **60 m** (was 300 - all of it subpixel),
displacement clamped to 1 m and faded to zero by 60 so the HQ/LQ handover
cannot pop. Per-layer displacement remap `min(h^r1.z,1)*r1.x + r1.y` in the
page bake, guarded for unauthored layers. HQ/LQ selection measures distance to
the chunk **AABB** - the old origin-corner distance was wrong by up to ~141 m
and made the HQ set depend on camera position and heading.

**Wetness**: global AM alpha minus `0.4 x` blended layer height, gated by
flatness, written to `gGMF.a` ("Wetness in a" was always its documented job).
Wet ground tightens specular (POWER 6 -> 96 by mask) and the sun-derived sum
rolls off through `1-exp(-x)` - the hard clamp was the flat white saturation
on wet ground and track decals. SSR marches the lit frame for wet reflections;
cubemap fallback. Sun terms are gated by the baked shadow; geometry
reflections deliberately are not. That is the rule: **sun needs sun,
geometry does not.**

**Terrain holes - data cracked, NOT implemented.** `terrain2/holes` in the
per-chunk `.cdata_processed` zips TerrainBuilder already opens:
`"zip\0" u32 u32-size` wrapping zlib; inflates to `"hol\0" w=64 h=64 ver=1`
then a 64x64 1-bit mask (8 bytes/row) per 100 m chunk. Himmelsdorf authors it
in 120 of 121 chunks. Plan: per-map R8 mask texture addressed by Global_UV,
discard in TerrainLQ/HQ and in the sun bake terrain pass.

**Still uncracked**: `terrain2/horizonshadows` (build_horizon_texture returns
Nothing; notes on the failed layout are in TerrainTextureFunctions).

## Water

Parsed fully from **BWWa**: per-body 340-byte blocks + four shared streams
(cell boxes / mesh verts / indices / unidentified bytes) addressed by
**prefix-sum (start,end) pairs at +0x134**. Confirmed offsets: bbox corners at
+0x00 (min/max, equal Y = the surface), sun glint power/scale at +0xB0, deep
colour at +0xC0, fresnel bias/exponent at +0xD0, **sun tint at +0xE0** - it is
NOT a reflection tint; Mines authors it orange and multiplying the sky by it
turned the lake orange. Monastery authors white, which is how it hid.

- **`cBWWa` must not be freed in `ReadSpaceBinData` cleanup.** It was - the
  parse succeeded and the data was nulled in the same function, which was the
  entire mystery of water never appearing. The null is commented out with the
  others (BWST/BWT2/WGSD).
- Geometry: corner quads per body. The tessellated mesh (with shoreline
  holes) is parsed and waiting in the streams if rectangles ever fall short.
- Shading: authored fresnel curve, 8-frame ripple loop (two frames blended),
  SSR geometry reflections with sky fallback, sun glint gated by the baked
  shadow, vertical-column shore fade, per-map height trim (`water_y_offset`).
- Boat mask: water discards over **up-facing** model surfaces within
  `water_exclude_band` metres VERTICALLY below the plane. Both qualifiers are
  load-bearing: without up-facing, submerged hull sides mask a stripe of
  water; without vertical depth, the waterline moves with the camera.
- Not done: flow-map advection (flow_map.dds direction/amplitude pair),
  foam, per-body 128^2 R32F shore depth maps, the water reflection probes.

## Models

- Skinned (iiiww) winding restored after the universal DX flip - see traps.
- Yacht LODs measured: all four skinned, identical Y ranges. LOD swaps do NOT
  move geometry; if something near a boat moves with the camera, suspect a
  view-dependent water metric first.
- **Open**: `SHADOW_MAP_LOD = min(1, MAX_LOD_ID)` picks LOD 1 but the loop
  skips `lod.junk` - a model whose LOD 1 is junk emits no bake command at all.
  A specific model missing its shadow is probably this.
- **Open**: trees visible through a building (depth/G-buffer, pre-dates
  everything above).

## Trees

- Every SRT LOD is packed at load; instances bucket by distance against the
  asset's own authored LOD profile (header 0x30), no invented thresholds. No
  far cull - a tree past the last LOD keeps drawing its cheapest geometry.
- The foliage alpha test lowers its cutoff with the mip level: alpha mips
  average toward the mostly-empty atlas mean, so a fixed 0.5 erased whole
  trees at distance. The card was never the problem; the discard was.
- Tree instance matrices carry the -1 x display mirror, which inverts
  gl_FrontFacing; the vertex stage pre-negates the normal by determinant so
  the two-sided flip in the fragment stage stays correct.

## Per-map settings

A missing key falls back to the startup default (`CaptureDefaults` once at
launch, `ResetToDefaults` at the top of `load_map`, BEFORE map data so
authored values still win and the saved file still overrides both). Adding a
setting no longer requires touching the 65 files. Water knobs
(`water_y_offset`, `water_exclude_band`) ride this system.

## One piece of advice

Unchanged through three revisions of this file, and it earned its keep again:
every real bug here was found by measuring, and every one survived a round of
confident reasoning first. The boats "sank" through two plausible fixes until
the metric itself was questioned; the water was invisible for a day because a
cleanup freed the parse in the same function; the ordering bug printed
`expected~NaN` all session. When something looks wrong, instrument it - and
read what the instrument already said.
