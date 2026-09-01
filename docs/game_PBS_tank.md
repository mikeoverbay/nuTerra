# WoT `PBS_tank.fx` — recovered

Decoded 2026-08-31 from `shaders/std_effects/PBS_tank.10.dx11.fxo` in
`shaders.pkg`. This is the shader that draws **tanks**; it is a different
family from the map content covered in `GAME_LIGHTING_MODEL.md`.

**PBS_tank does no lighting.** Every substantive pixel shader writes five
render targets and stops. All shading happens later, in `resolve_lighting`.
So this document is about *material composition and G-buffer packing* — how a
tank's textures and parameters are combined into the buffer the resolve reads.

## Reproducing the carve

```
shaders.pkg -> shaders/std_effects/PBS_tank.10.dx11.fxo   (5.4 MB)
  the .fxo is itself a ZIP; read its 'effect' entry       (22 MB)
  scan for DXBC magic, u32 blob size at +24
  fxc /nologo /dumpbin <blob> /Fc <out>
```

534 DXBC blobs, **216 distinct** after dedupe: 10 VS, 206 PS. Run `fxc` from
PowerShell — git-bash rewrites `/dumpbin` into a path. The `.11` twin is a
36 KB stub set and is useless; the `.10` carries everything inline. Same
recipe as `wot-shader-crack-recipe`.

## Technique and permutation structure

Read out of the container, not guessed from the asm: the effect header carries
a `$Selection` cbuffer of 27 selectors, a CODE section of mixed-radix
permutation records, and DATA index tables.

**5 techniques x 1 pass, all named "Main":** Color, ColorInstanced, EdgeDrawer,
Shadow, ShadowInstanced.

```
534 = 2 ColorVS + 512 ColorPS + 1 ColorInstancedVS
    + 4 ShadowVS + 8 ShadowPS + 4 ShadowInstancedVS
    + 1 EdgeDrawerVS + 2 EdgeDrawerPS
216 distinct = 2 + 198 + 1 + 3 + 6 + 3 + 1 + 2
```

Both totals were measured from the carve *before* the model was derived, and
the model reproduces them exactly.

The 512 main pixel shaders are a 9-bit cross product, `blob = 2 + p`:

| stride | selector | effect |
|---|---|---|
| 1 | `alphaTestEnable` | the top-of-shader discard |
| 2 | `g_enableDirt` | dirt/wet path; **also the only Color VS axis** |
| 4 | `g_gpuDecalsEnable` | the clustered decal pass, +12 resources |
| 8 | `g_isChassis` | swaps height-driven wetness for a constant |
| 16 | `g_useDetailMetallic` | `metallicDetailMap`, ~26 instructions |
| 32 | `g_useGMCamoTexture` | **DEAD** — 253/256 pairs byte-identical |
| 64 | `g_useOldCamoDetail` | **DEAD** — scheduling differences only |
| 128 | `g_useOverheatMechanic` | `g_heatMap` + `g_heatColorGradient` |
| 256 | `g_useRepaint` | `colorIdMap`, `g_repaintColor1..3` |

512 collapsing to 198 distinct programs is caused **entirely** by the two dead
camo axes.

**What is NOT a permutation.** Much of what varies visually is a runtime
`if_nz cb0[...]` branch present in every substantive PS: dissolve, visibility
tunnel, micro-detail, new-wear, normal packing, all three projection decals,
the screen-space decal buffer. There is no alpha-to-coverage, TAA-sampler,
wireframe or `SV_IsFrontFace` axis — every main PS declares the same 12-element
input signature and the same 5 targets. 16 of the 27 selectors never reach
shader code at all; they only index render-state and sampler-state tables.

## The G-buffer

The whole point of the shader. From the tail of the fullest PS, identical
arithmetic across all 198 five-target programs:

| target | channels |
|---|---|
| `o0` | **metallic**, **gloss**, `0`, materialID/255 |
| `o1` | world normal `*0.5+0.5`, **`1 - AO`** |
| `o2` | **albedo**, `0` |
| `o3` | TAA velocity `.xy`, `0`, `0` |
| `o4` | **emission** `min(e,5) * 0.2`, `0` |

Encodings are minimal and worth stating because they are *absent* where you
might expect them:

- The normal is a plain `n*0.5+0.5` **world-space** remap. No octahedral pack,
  no z-drop, no view space, no sqrt.
- Albedo, gloss and metal are written **raw and linear** — no gamma, no pow, no
  sqrt anywhere between the final value and the output.
- **AO is stored inverted** — `o1.w` is an occlusion *amount*, not a factor.
- Emission is range-compressed `[0..5] -> [0..1]` with a hard clamp; the
  consumer decodes `o4.rgb * 5`.
- `materialID = 4 static / 5 dynamic, +128 when applyOverlay`, written `/255`.
  Low bits are the kind, bit 7 is a flag — the same shape nuTerra uses in
  `gGMF.b`.

`bakedAOPower` / `bakedAOToShadowsMult` are **not applied here**; their whole
containing struct is `[unused]`. They belong to the resolve.

## Naming traps

Every one of these was proven from arithmetic, and each contradicts the name:

- **`metallicGlossMap` is (GLOSS, METALLIC, mask, unused)** — not the order the
  name implies. Proven by the repaint constants: `g_repaintGloss` is added to
  the `.x` lineage and `g_repaintMetallic` to the `.y` lineage.
- **`g_glossMin` / `g_glossMax` are not a gloss remap.** They are the gloss
  *window over which micro-detail fades in*, and appear at only three
  instructions, all inside the micro-detail block.
- **The alpha test reads `normalMap`, not `diffuseMap`.** Source is
  `g_useNormalPackDXT1 ? normalMap.z : normalMap.x`. `diffuseMap.a` is never
  read at all.
- **`g_repaintMetallic.w` is not a metallic value** — it is the global wear
  amount. **`g_repaintGloss.w` is not gloss** — it is a colour-blend weight.
- **`g_dirtLevel` is not part of the dirt block.** It appears at exactly one
  instruction, inside micro-detail selection.
- **`applyOverlay` never touches albedo.** It only adds 128 to the material ID.
- **`normalMap.b` doubles as the micro-detail atlas slice index**,
  `uint(min((b + 0.0625) * 8, 7))` — and in DXT1 pack mode it is simultaneously
  the alpha-test channel.
- **Repaint gloss/metal are deltas against hardcoded references**, not lerps:
  `gloss += mask * (g_repaintGloss[region] - 0.509)`,
  `metal += mask * (g_repaintMetallic[region] - 0.230)`.

## Vertex pipeline

Vertex format is `POSITION` float3, then `NORMAL`/`BINORMAL`/`TANGENT` each as
**a single uint32 of packed bytes**, `TEXCOORD0` float2, `COLOR0` float4.

- Packed decode is `byte * (1/127) - 1`, identical for all three vectors.
- The TBN lands in **world** space, each vector rotated by the `g_world` rows
  and normalized **independently** — no Gram-Schmidt, no cross product, no
  handedness bit. The binormal ships explicitly per vertex.
- `COLOR0` is declared in every VS in the file and **read by none**.
- `SV_Position` uses `g_jitteredViewProjMat` — the main pass is TAA-jittered.

Interpolators: uv0, world N/T/B, `worldPos + tank-height`, prev-frame position
for velocity, and three projection-decal coordinate sets. `TEXCOORD8` is
written zero and never read.

**`TEXCOORD4.w` is a tank-local height gradient**,
`saturate((objY + g_tankSize.z - g_tankSize.x) / (g_tankSize.y - g_tankSize.x))`
— i.e. Offset/Min/Max, matching the UI labels. It drives height-dependent dirt
and wetness. The dirt-off VS variant carries view depth in that slot instead.

## Material composite order

A re-implementation must preserve this order — several stages read the running
values rather than the source textures.

**Albedo:**

1. `diffuseMap.rgb`, linear, no in-shader decode (sRGB comes from the view).
2. `metallicDetailMap` perturbation — **unconditional**, before any branch.
3. Repaint: `colorIdMap.r` **point-sampled**, matched against 0 / 64 / 128 of
   255 with 0.1 tolerance, picking `g_repaintColor1/2/3`.
4. Micro-detail atlas, **overlay blend**, gated on `g_useDetail` and distance.
5. GPU decals, group A (under).
6. Dirt: `lerp(albedo, g_envDirtColor.rgb, dirt)`, then `*= (1 - 0.3*wet)`.
7. Dissolve edge colour override.
8. Projection decals, screen-space decal buffer, heat gradient.
9. GPU decals, group B (over).

**Normal:** base from `normalMap` with `z = sqrt(1 - min(x²+y², 1))`, then a
detail-normal blend that is a **custom approximate-basis rotation — not RNM,
UDN or whiteout** — then the dirt normal, which *is* textbook whiteout. Final
transform is two-sided:
`worldN = normalize(n.x*T + n.y*B + n.z*(frontFacing ? 1 : -1)*N)`.

**Gloss/metal:** `metallicGlossMap` → `metallicDetailMap` → micro-detail
overlay (gloss only) → repaint delta → GPU decals → projection decals →
screen-space decals → dirt → wetness. Dirt drives both toward fixed constants
(gloss 0.114, metal 0.231373); wetness overrides them last.

## Decals — three separate systems

**Projection decals (3).** Placement is in the **vertex** shader: a rotation
matrix expanded from a quaternion builds three object-space planes, emitting
`(u, v, depth, angleMask)` per decal. Angle rejection is per-vertex and binary.
The PS gates on the box test plus a per-pixel opacity, lerps albedo, and
**smoothsteps** gloss/metal rather than using the opacity directly. A decal with
emission disabled *erases* the emission underneath it.

**Clustered GPU decals.** 32x32 pixel tiles by 16 log-depth slices, 64 slots
per cluster packed two 16-bit indices per uint. Six per-decal structured
buffers. Per decal: angle cutoff, box or cylindrical projection, optional POM
against the gloss/metal atlas alpha, flipbook animation, five atlas samples
through per-decal rects. A decal can modify exactly six things — albedo,
normal, gloss, metal, AO, emission — selected by mask bits. Accumulates into
**two independent sets** applied at different points in the frame.

**Screen-space decal buffer.** Point-loads at integer `SV_Position`, accepts
only when the buffer's block index matches the object's.

## Other systems

**Overheat.** `heat = g_heatMap.r * g_heatPercentage`, used as the U of a 1-D
ramp; `albedo = lerp(albedo, grad.rgb, heat)` and
`emission += grad.rgb * heat * g_heatEmissionCoefficient`.

**Dissolve** uses **no texture** — procedural 2D simplex noise (standard
Ashima/Gustavson) over the mesh UV, discarding below the threshold and painting
an edge band above it.

**Visibility tunnel** is live and runs immediately after the alpha test, using a
temporally rotated interleaved-gradient dither.

**Dirt darkens emission too** — mud over an emissive decal pulls it toward
`g_envDirtColor`.

## Caveats

- Read from the base `PBS_tank`. The 21 sibling effects (`_skinned`, `_crash`,
  `_colourised`, `_fade`, `_tracks`, …) were not decoded.
- Two claims were **refuted during verification** and are corrected above:
  `o1.w` is `1 - AO`, not a wetness term (a register-reuse error, exactly the
  trap this kind of decode invites); and `metallicGlossMap.z` is read in more
  places than a first pass suggested.
- `g_isChassis`, `g_useGMCamoTexture` and `g_useOldCamoDetail` were previously
  unnamed axes; the first is real, the other two are dead.
- Meanings inferred from variable names are marked as such in the source
  analysis; the arithmetic itself is transcribed, not guessed.
