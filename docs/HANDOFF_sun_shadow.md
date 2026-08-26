# Handoff — renderer state

Supersedes the previous version. Covers everything through the FX pass
(sorting, shader-variant fidelity, new material families), PBS_tiled_global,
and the outland reconnaissance that defines the next feature. Where it says
"was", that is what the code did before and why it changed.

The owner is the sole developer of nuTerra, a VB.NET / .NET 6 / OpenTK offline
World of Tanks map viewer. He guides tightly, tests every build himself, and is
usually right when he says something looks wrong. Believe the screenshot over
the reasoning.

---

## Where this left off

- Everything is committed: `3f086ee` ("FX pipeline: depth-sorted bucket,
  cracked shader variants, new materials") on `master`, which also swept in
  the previous session's uncommitted FX Stage 0 / camera damping / minimap
  work. Push from the owner's terminal - the agent shell has no SSH key.
  `readme_images/test.png` was deliberately left untracked (looked scratch).
- **Next feature: OUTLAND rendering, rebuilt the way the game does it.** The
  recon is complete and the plan is at the bottom of this file. Start there.
- Pending the owner's eyes: `hills_outland_smokes` brightness (stand-in
  lighting multipliers), Abbey/Prokhorovka smoke after the fade fixes, and
  the D-Day base smoke sheets.

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

## NEXT: Outland (recon complete - start here)

The outland is a MESH that surrounds the chunk grid, one per
`OutlandCascade_v1_0_0` (modSpacedBinVars ~line 144: BB min/max, heightmap,
normal map, tile_map, tileScale; two cascades per map; `tiles_fnv` lists the
7-8 `*_macro_AM` tile textures). nuTerra already generates the mesh/VAO and
loads `outland_height` / `outland_tilemap` (TextureMgr); `Draw_outland`
draws it with `Outland_shaders/outland.vert/frag`.

**The game's shader is `shaders/std_effects/PBS_ext_outland.fx`** (1 VS +
2 PS, small). Its PS samples ONLY: `albedoSml`, `normalSml` (with
g_useNormalPackDXT1), `detailAlbedoSml`, plus `g_uvOffsetScale` and
`g_specularParams`. NO tiles, NO tilemap at runtime - **the game bakes the
tile set through the tilemap into a single albedo per cascade**, then draws
simple. That is the plan:

1. Load-time bake per cascade: tilemap weights x tile textures -> one albedo
   (nuTerra has bake infrastructure: atlas rebuild, VT pages, minimap).
2. Replace outland.frag with a transcription of the PBS_ext_outland PS:
   baked albedo x detail overlay, proper AG normal decode (kills the current
   sign hacks - note `sqrt(1 - x*x + z*z)` in write_normal has a sign bug),
   G-buffer output.
3. Transcription unknowns (all answerable from the 3 small blobs): what
   feeds detailAlbedo (bet: TerrainSettings1.noise_texture), the exact
   combine, and whether the VS samples the heightmap or trusts prebuilt
   verts.

Current outland.frag defects for reference: only 4 of 7-8 tiles (c_tiles[4]),
RGBA4 weight blending, mip bias -2 hack, baked-shadow term commented out.

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
