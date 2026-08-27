# Handoff — renderer state

Supersedes the previous version. Covers everything through the FX pass
(sorting, shader-variant fidelity, new material families), PBS_tiled_global,
and the OUTLAND rebuild - now COMPLETE and committed through three commits:
game-faithful bake + ring meshes, heightmap data-weld + 1024 grid +
wireframe, and outland water + the two-edge wall fix. Where it says "was",
that is what the code did before and why it changed.

The owner is the sole developer of nuTerra, a VB.NET / .NET 6 / OpenTK offline
World of Tanks map viewer. He guides tightly, tests every build himself, and is
usually right when he says something looks wrong. Believe the screenshot over
the reasoning.

---

## Where this left off

- Outland work landed as three commits on `master`: `b504394` (bake, ring
  meshes, PBS_ext_outland transcription, placement fix, load_outland
  False -> True), `f757d11` (heightmap data-weld, OUTLAND_GRID 1024,
  wireframe view), `d39fc12` (outland water sheet, water saturation clamps,
  the fract(-u) two-edge wall fix, far-cascade weld, seam audits). The first
  two are pushed; `d39fc12` may still need a push from the owner's terminal -
  the agent shell has no SSH key. A README third-party credits section rode
  along in f757d11.
- One same-day REVERT sits between b504394 and f757d11: a per-vertex weld +
  8x seam subdivision and a load-time port of Blender's QEM decimator (froze
  the load twice - likely quadratic dead-face growth, prune never verified).
  The owner called it: small provable steps, prototype heavy mesh work
  offline first. The Blender port notes live in the session scratchpad;
  Garland-Heckbert + the collapse guards are worth revisiting OFFLINE if
  triangle counts ever matter (400k drawn today, his card does 180 fps).
- The outland verified good by the owner's own eyes on prohorovka, dday,
  lakeville. Verify next on a snow map and a desert map (different tile
  sets), and the two BIG epic maps (208/209 - 1024-chunk grids).
- Still pending the owner's eyes from last session: `hills_outland_smokes`
  brightness (stand-in lighting multipliers), Abbey/Prokhorovka smoke after
  the fade fixes, and the D-Day base smoke sheets.

## How to work in this repo

- The repo is `C:\nuTerra`; the owner opens `C:\nuTerra\nuTerra.sln` in
  **VS 2022** and runs Debug|Any CPU -> `bin\Debug\net6.0-windows`. The stale
  clone at `C:\Users\...\source\repos\mikeoverbay\nuTerra` is dead - never
  touch it. When the owner "sees no changes", rebuild every folder he might
  launch.
- **Kill the running exe before building** (`Get-Process nuTerra |
  Stop-Process -Force`) - and remember that kills the owner's live session,
  so batch changes before a rebuild.
- Build: `MSBuild.exe nuTerra.sln /t:Build /p:Configuration=Debug
  /p:Platform="Any CPU"` from `C:\nuTerra`.
- `nuTerra.exe <space_name>` (e.g. `101_dday`) loads a map directly. Only
  agent launches use it; the owner's VS launches get the map picker.
- **Shaders only validate at runtime.** Launch and check stdout for
  `Shaders Built.` / `didn't compile`.
- **Run with the working directory set to the bin folder** (ShaderLoader
  resolves `shaders` from CWD).
- **Snapshot writes `%TEMP%\nuTerra\snapshot.txt`** (latest press wins) as
  well as the console. When the owner says "snapshot taken", read that file.
  It carries the cull buckets (`fx=` is the volumetric pass), the FX
  sort-order-change counter (reset each press; ~0 while stationary is
  healthy), glGetError, and a name for every FX model in the bucket
  (`fx in view:` lines) - the model picker cannot identify FX draws.

### Cracking game shaders (the proven recipe, now exercised four times)

An `.fxo` in `shaders.pkg` is a ZIP; its `effect` entry holds DXBC blobs
findable by the `DXBC` magic (u32 total size at blob offset 24). Disassemble
with `fxc /nologo /dumpbin` from
`C:\Program Files (x86)\Windows Kits\10\bin\10.0.19041.0\x64\fxc.exe` - full
reflection: cbuffer layouts WITH register defaults, texture bindings, asm.
Variants compile per bool material property; diff the blobs to find what a
flag selects (the volumetric VS blobs alternate fresnel on/off - count
log/exp pairs). cbuffer offset/16 = the cb register in the asm.

### Offline space.bin analysis (Python, no launch needed)

Header table at 0x14: u32 count, then {char4 magic, i32 ver, i64 off, i64
len}. `BWArray` = {u32 item_size, u32 count, payload}. BWST = {u32 entry_len,
u32 count, entries {key, str_off, str_len}, blob at off + 12*count + 12}.
BSMA = materials {effectIndex, propBegin, propEnd, ident} -> FX string table
-> props {name_key, type, value} (types: 1 bool, 2 float-bits, 3 int, 5
index into trailing vec4 table, 6 BWST string). BSMI = transforms(64B) /
chunk_models / vis_masks / model_indexes, translation at matrix bytes 48-59.
Chunk names are `%04x%04xo` (signed 4-hex per axis; the grid overlay's
trailing "o" reads like a zero) plus the chunk's index. Scripts from this
session live in the session scratchpad but are all ~50-liners re-derivable
from this paragraph.

## Engine-wide traps

| trap | detail |
|---|---|
| Clip control | `ZeroToOne` + `DepthClamp` + reversed-Z (`ClearDepth 0`, `Greater`). Any projection you build must be remapped. |
| `gPosition` is VIEW space | Every writer names the varying `worldPosition`; every one stores `view * model * vertex`. (`t_mixer.vert` IS world.) |
| Shader includes | `#include "common.h" //! #include "../common.h"` - the `//!` half is an editor hint. |
| DX index flip | Universal triangle reversal; instance matrices are mirror-conjugated at load (M12/M13/M21/M31/M41 negated) - verified correct, do not "fix". |
| View-ray vs vertical | Water/depth comparisons measure the VERTICAL column, never the view ray. |
| OnUpdateFrame spins ~127k/s | `DELTA_TIME` is render-dt and wrong there by ~600x; time-based update-loop work runs its own Stopwatch (`rot_clock`). |
| GL globals leak between passes | Every pass restores what the next stretch assumes: post-water runs depth-test OFF, blend (SrcAlpha, 1-SrcAlpha). |
| New ImGui windows go in Window.vb's UI pass | modRender HUD windows never appear; always SetNextWindowPos/Size on first open. |
| VRAM pressure eats write-once caches | ~5.6/8 GB triggers eviction; evicted content is UNDEFINED. Minimap re-renders per frame for this reason. |
| **GPU buffer readback demotes buffers** | Reading an SSBO with GetNamedBufferSubData makes the driver move it to host memory (perf warning 131186 spam). Route readbacks through a small ClientStorageBit staging buffer (`parameters_temp` pattern; `indirect_fx_staging`). |
| **MaterialProperties is 288 bytes, exactly** | 10 vec4 + 12 uvec2 + 8 scalars. `alphaFromDiffuse` consumed the old tail padding; the next field GROWS the struct - change common.h AND GLMaterial together (Debug.Assert guards it). |

## FX (volumetric pass - substantially verified against the game this session)

- **Draw order**: cull.comp emits FX draws in atomic-counter order, which is
  nondeterministic and flickered overlaps. `draw_fx` now reads the bucket
  back (via staging), sorts back-to-front by instance origin with a 10 m
  hysteresis (stored distance per candidate; near-ties cannot oscillate while
  the camera orbits), tie-breaks on candidate id, writes it back. Loader
  retains `candidate_origins` / `candidate_model_ids` per candidate draw.
- **Variant selection is real**: `alphaFreshnelEnable` picks between the
  fxo's compiled variants. On = fresnel colour-pull/alpha-thinning in the VS
  plus PS alpha `sat((texA + vertA*fade - 1) * gain)`. Off = no fresnel,
  plain `sat(texA * vertA * fade)` (gain is junk in those materials).
  Unauthored default is True (keeps D-Day as approved) - if D-Day's base
  sheets ever look off, flipping the unauthored default to plain is the
  first experiment.
- `destBlend = 2` (D3DBLEND_ONE) composites additively even without
  `alphaAdditiveEnable` - both end up as output `(rgb*a, 0)` under the
  pass's premultiplied blend.
- **Distance fade**: `fade = sat((viewDist - fadeMin)/(fadeMax - fadeMin))`,
  then `fade = sat(fade + fadeBase)` - the saturate matters (unclamped it
  doubled vertex-alpha weight on every fade=1 material). fadeMin/Max ride
  `g_atlasIndexes.xy`; defaults 0.01/1.0. SmokeBotton-style backdrops author
  150..400 and only exist at range.
- **Lighting is verified faithful**: `max(SH,1e-4)*mul.z + mul.y*sun +
  selfIllum`, times vcol and Tint (nuTerra's shaped ambient stands in for
  SH9). `lightMultipliers.x` is dead in every compiled variant. Fog sheets
  are near-fullbright in the game's own math.
- **Fog fresnel verdict**: shallow-fog sheets vanish at grazing view BY
  DESIGN (up-normals + fresnel thinning, verified instruction-level). The
  eye-level ravine fog the game shows is `.eff` PARTICLES - Stage 1.
- **Heat haze renders as an orange card** and will until backdrop distortion
  exists (copy the composed frame before draw_fx, let distortion materials
  sample it). Top FX item by payoff after outland.
- `softFactor` is [unused] in every compiled variant including hw_ - the
  game does NOT depth-fade these; don't add soft particles chasing it.
- Load log prints every volumetric material's shaping values; unknown-prop
  lines print the VALUE too. Only `texAddressMode = 1` (WRAP - harmless)
  still logs as unknown.

## lightonly / glow family

Split on `alphaTestEnable`: True (env_19_39_BurntGrass) -> deferred cutout
entry (model.frag index 8) with the new `alphaFromDiffuse` flag steering both
depth passes to test diffuse.a instead of the PBS normal-map red channel.
False (`hills_outland_smokes`) -> synthesized volumetric props, drawn as a
static over-blended billboard in the FX pass (plain alpha variant, no scroll,
lighting multipliers borrowed from vista smoke - a stand-in, tune by eye).

## PBS_tiled_global (new, transcribed from the fxo)

Big unique-unwrap rocks (Graf Zeppelin 177 mats, Lost Paradise 125). Tiles at
`TC1 * g_tileUVScale.xy`; global set at TC2: blendMask (A = baked AO),
colorTex GCM (luminance-modulates then chroma-transfers the tiles, per-tile
weights in g_dirtColorParams.yzw / g_tintParams.yzw), globalTex GNM (global
normal from .ag, B*2 = baked shadow). The game's techniques NEVER sample the
authored normal/metallic tiles for this fx - neither do we (VRAM). Subroutine
dispatch grew to `entries[13]`; element 11 (volumetric, never drawn in that
pass) points at FX_unsupported as filler.

## Shadows / Camera / Terrain / Water — unchanged from the previous handoff

Map-wide bake still the only caster; MSM behind the A/B toggle; TARGET_TEXEL
0.25 reclaims ~1.9 GiB if VRAM headroom is needed. Camera damping is the
OrbitControls model on `rot_clock`. Terrain holes remain reverted (drop the
`63 -` X-mirror on re-implementation); VT re-apply list unchanged (MipBias
subtract in TerrainLQ/HQ.frag first). `terrain2/horizonshadows` uncracked.

---

## OUTLAND (complete - owner-verified on prohorovka, dday, lakeville)

Rebuilt the way the game does it. The full fxo recon (both
`PBS_ext_outland.10/.11.dx11.fxo` disassembled) settled everything:

- **The game bakes at load and draws simple.** No baked albedo ships in any
  map package; at runtime the PS samples ONLY baked `albedoSml`, `normalSml`,
  `detailAlbedoSml`. Tier 11 fxo = forward-lit (SH + sun + weak spec + vertex
  fog); tier 10 = the deferred 5-MRT G-buffer write, which is what nuTerra
  transcribes: RT0 = const (59,80,0)/255 + matID 4, RT1 = normal*0.5+0.5,
  RT2 = albedo(+detail combine), RT3 = velocity, RT4 = zeros. The VS samples
  nothing (prebuilt verts; the mesh TBN is provided but the PS ignores it).
- **Tilemap encoding cracked** (verified against dday + prohorovka offline):
  an RGBA4 texel is NOT four weights - nibbles are
  `r = tile index A, g = tile index B, b = weight A, a = weight B`, indices
  into the full `tiles_fnv` list (11 tiles on dday, 8 on prohorovka).
  Blend = (tA*wA + tB*wB)/(wA+wB). Indices must never be filtered.
- **tileScale is metres per tile repeat** (dday 20/20, prohorovka 90 near /
  900 far - the only reading sane at 42 km). Bake tiles at span/tileScale
  repeats.
- **The cascades are RING meshes.** The near ring's hole = playfield chunk
  footprint; the far ring's hole = near cascade's drawn footprint. Without
  the holes the coarse outland surface pokes through the playfield (its
  heightmap tracks the playfield only to ~74 m mean accuracy). This is why
  the game ships prebuilt meshes. The R channel of the normal map is an
  alpha-test cutout in the fx but is ~248 everywhere on dday - the hole is
  in the game's mesh, not the mask.
- **Normal decode** (DXT5, AG pack): X = a*2-1, Z = g*2-1,
  Y = sqrt(1 - min(x^2+z^2, 1)), normalize, world space (no TBN). In
  nuTerra a NORM_SIGN(-1,-1) rides the decode - forced by the UV = -UVs
  placement (documented in outland.frag; flip one sign if slopes shade
  wrong against the sun).
- **Detail combine** (game-exact, wired but neutral):
  `color = albedo.rgb + detail.a - 0.5; color = mix(color, detail.rgb,
  albedo.a);` detailUV = TC * 64 (hardcoded in the game for both cascades).
  What texture the engine binds as detailAlbedoSml is still unknown
  (an authored material property, "*_AM" pattern; TerrainSettings1
  .noise_texture remains the candidate). nuTerra ships a 1x1 neutral
  (0.5,0.5,0.5,0.5), which makes the whole combine an exact no-op; the
  bake writes albedo.a = 0. Wiring a real detail texture is one BindUnit(3).

**Implementation**: `outland_bake_accum/resolve` shaders do the bake - one
additive fullscreen pass per tile into RGBA16F (weight bilinear across
tilemap texels, indices texelFetched per corner), resolve divides by total
weight into mipped RGBA8 2048^2 per cascade (~43 MB both) - per-tile passes
because GLSL cannot index a sampler array with a texture-fetched value.
`MapTerrain.bake_outland_albedo` runs at the end of `create_outland`;
`DUMP_OUTLAND_BAKES = True` writes each bake to `%TEMP%\nuTerra\*.png` at
load for eyeballing. `build_outland_vao` builds the two ring index buffers
(`build_outland_ring_indices`) over the shared vertex buffer. Draw binds
baked albedo(0), height(1), normal(2), neutral detail(3). gGMF =
(0.2314, 0.3137, GFLAG_TERRAIN, 0) - the game's exact RT0 constants; the
old write was (0.2, 0.3) so deferred needed no change. glGetError clean,
94 fps, VRAM 4.5/8 on prohorovka.

**Since the first commit** (f757d11 + d39fc12):

- **THE -UV MIRROR TRAP (the big one).** The mirrored sampling must be
  written as `1-u`, never `fract(-u)`: fract collapses u=0 to 0 instead of
  1, so the sheet's -X/-Z EDGE ROWS sampled the OPPOSITE side of the
  heightmap (REPEAT even blended the two borders 50/50). Invisible on flat
  maps whose sides match; a 400 m one-row wall on exactly two edges of
  lakeville. Fixed in outland.vert/frag, outlandWire.vert and the CPU
  samplers, all with a half-texel clamp. Any NEW outland code must use the
  1-u form.
- **Heightmap data-weld**: `patch_outland_heightmap` (ChunkFunctions)
  rewrites the near cascade's texels in/near the terrain footprint with the
  terrain's own surface (`get_Y_at_XZ_fast` - board cell computed directly,
  ~74k texels in 24 ms; NEVER bulk-call the scanning `get_Y_at_XZ`, it
  froze the load). Flush at the footprint line, tucked with a small lip
  inside, blended out over an ADAPTIVE band (2.5x the audited worst seam
  mismatch, 45..400 m). Values stored +1.5 to cancel the VS sink. Runs
  after MAP_LOADED.
- **Far cascade weld**: same idea one ring out - the far heightmap is
  dragged to the near cascade's rendered heights at the near rect, adaptive
  band 150..1200 m. Lakeville's authored near/far ring mismatch is REAL
  (~180 m mean) even after the mirror fix.
- **OUTLAND_GRID = 1024** (MapTerrain const) drives mesh gen, ring cutter
  and the patch affine together - they must never diverge. ~3.9M outland
  tris, +75 MB VRAM; owner measures 180 fps. 512 is the fallback knob.
- **Outland water** (MapWater.Build): a body touching the terrain footprint
  is OPEN water; a ring of sheets at its level covers the outland, hole-cut
  at the footprint and clipped around body overhangs. Fills dday's Channel
  and prohorovka's river valleys exactly where outland ground sits below
  the level - the game's water clipmap equivalent. Water look fixes in
  water.frag: sky-reflection soft-clamp REFL_MAX 0.8, GLINT_CAP 0.35, and
  the cube lookup clamped to the horizon ring (the env cube's lower half
  carries baked ground that smeared orange across water).
- **Wireframe**: "Draw terrain wire" also draws the outland - near cyan,
  far magenta - the instrument that identified the wall's owner.
- **Load-time audits print on every load**: orientation scores (mirror-both
  must win), per-edge seam stats, chosen weld bands, and the patched
  heightmaps + bakes dump to %TEMP%\nuTerra as PNGs. Deliberately left on.

**Known data facts / open threads**:
- D-Day's cascade-0 normal map genuinely centres at ~56/255 in both AG
  channels (uniform lean; prohorovka ships flat 127/128 dummies, CT's sm24
  ~118). Compare against the game before "fixing" - same data, same decode.
- Lakeville AUTHORS the R-channel cutout mask (5.2% of blocks dark, in
  mountain shapes) but the material's alphaReference is unknown; a guessed
  0.5 threshold punched sky holes, so the cut is REMOVED (comment in
  outland.frag keeps the lead). If the authored reference ever surfaces
  (space.bin material data), restore the discard with it.
- detailAlbedoSml is still the 1x1 neutral stand-in; the real binding is an
  authored material property (*_AM pattern; TerrainSettings1.noise_texture
  is the candidate). One BindUnit(3) to experiment.
- Outland tiles have NO _hd variants anywhere (all 173 are 1024^2 DXT5,
  both clients); the content/Outland/hd_out_* MODEL textures do, and they
  already load through LookupHD.
- BWWa body records are ~70% unparsed: +0x9C centre/size, +0xEC the
  map-wide water bounds (feeds g_worldXZBounds in the water shaders),
  +0xB8 eight authored flag bytes - set on open-water bodies, zero on the
  closed pond, exact meaning undecoded.
- The game's to-horizon sea is `shaders/water/water_clipmap_instanced` -
  camera-following instanced patches + terrain_height_renderer for shore
  culling. The per-map sea level is engine-fed; nuTerra's sheet stands in.

**load_outland was shipped False in App.config/Settings.settings** (outland
never drew at all - the "gray ring" in old screenshots was the skydome).
Both flipped True. The Draw Outland checkbox in Settings still toggles it
live.

**Placement fix (owner-reported seam misalignment)**: the outland used to
centre on the settings-derived terrain centre ((max+min)/2*100 = (-50,-50)
on a -7..6 grid) while the cascades are authored centred on the terrain -
and nuTerra's chunk-Z convention shifts the frame another 50 m. Measured on
both test maps: chunk footprint X -700..700, Z -800..600, so the sheet sat
(50, 50) m out of register - visible as a sky gap plus offset features at
the map edge. create_outland now measures the real chunk footprint and
centres the outland (and the near ring's hole) on it.

**Dead ends already checked - do not re-dig:** BWSG/BSGD + BWS2/BSG2 are a
repacked vertex arena for the REGULAR map models (373/378 overlap with BSMO
renders; the removed 2020 parser is recoverable at `6927c04^` if ever
needed). Out-of-bounds BSMI instances all carry the CTF flag and already
render. `tiles_fnv` entries are macro albedo textures, not model prefabs.
The remaining unparsed space.bin sections (GOBJ, UDOS, CENT, ...) are
placement-sized, not geometry-sized.

## One piece of advice

Five revisions of this file and the rule has only sharpened: every real bug
fell to an instrument, never to an argument. This session's additions: when
the owner says the game does something different, CRACK THE COMPILED SHADER
- the fxo recipe above turned four arguments (invisible smoke, order
flicker, giant white sheets, fog angles) into four one-line facts, two of
which were our bugs and two of which were the game's own design. And when a
"looks wrong" report arrives, get the screenshot before the theory.

The outland session added more: when a rewrite appears to change nothing,
CHECK THE ENABLE FLAG before the code (load_outland was shipped False; an
hour of "why is my bake black" was actually the old build's skydome) - and
when a decode is in doubt, render the reference offline
first: the 60-line Python that decoded the tilemap to flat colours settled
the nibble encoding in one image, before touching a shader.

The wall hunt closed the day with three keepers. The owner's OBSERVATION
was the instrument that mattered ("a wall of the opposite side of the
map" named fract(-u) after four of my measurements circled it); colour
your debug views (near cyan / far magenta answered "whose wall?" in one
frame); and heavy mesh work does not belong in the load path - the QEM
decimator froze the load twice and was reverted whole. Small provable
steps: the day's wins each shipped as one small committed change the owner
could see with his own eyes.
