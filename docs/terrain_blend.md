# Terrain layer blending

`shaders/mapMixing/t_mixer.frag` bakes the eight terrain layers into virtual
texture pages. `Terrain_shaders/TerrainLQ`/`HQ` only sample those pages and do
trilinear between two mips - no layer blending happens there.

## The blend

Transcribed from the game's own VT baker, `shaders/terrain/
terrain2_5_virtual_texture.11.dx11.fxo`, blob 13:

```
s  = splat / sum(splat)                      normalise the splat weights first
p  = max(height, 1/255) * s                  contender is the PRODUCT
ma = max over all 8 of p
w  = max(p + blendHeight - ma, 0.0) * s      splat applied a second time
w /= sum(w)
```

Three details that are easy to get wrong, and that a from-scratch
reimplementation will get wrong:

- **Splat is normalised before anything else**, not used raw.
- **The contender is `height * splat`, a product.** `splat + height` gives a
  visibly different curve.
- **Splat multiplies again after the threshold.** That is what keeps an
  unpainted layer out no matter how tall its height map is - no explicit
  `if (splat > 0)` gate is needed, and adding one is a sign of having missed
  this step.

The `max(height, 1/255)` floor is the same constant the outland shader uses, and
is why the long-dead `mth[i] = max(mt[i].w, 0.00392156886)` line sits in
`t_mixer` - someone transcribed it from the game years ago and never wired it up.

Height is the **alpha channel of each layer's AM texture**. Each layer is a four
slice texture array:

```
0  AM   albedo rgb + height a
1  NM   spec r, normal ga, AO b
2  macro AM
3  macro NM
```

`t[i].a` survives the AO and macro passes untouched - those only write `.rgb` -
so it reaches the blend intact.

**Do not scale height by `L.r1[i].x`.** That is the layer's tessellation
displacement height (`TerrainTextureFunctions.vb:246`), and on a map that does
not tessellate it is zero, which silently removes height from the blend
altogether. The original code used it as an additive blend bias, which was the
same confusion in the other direction.

## What was there before

```glsl
Mix[i] *= t[i].a + L.r1[i].x;      // splat x (height + tessellation bias)
Mix[i]  = pow(Mix[i], 1.0 / 0.7);  // sharpen
Mix[i] /= f;                        // normalise
```

A plain weighted average. No maximum, no threshold, nothing ever reaching zero.
Every painted layer contributed in proportion always, so two textures
interpenetrated across the whole splat gradient instead of meeting where their
height maps cross. `pow(x, 1.43)` steepened the curve enough to look vaguely
height-aware without ever producing an edge.

## Parameters

From `space.bin`/BWT2, per map. Abbey:

```
blendMacroInfluence    1.00    in the UBO; the per-layer quad is what the game
                               actually uses (see "Macro, normal and global map")
blendGlobalThreshold   0.30    in the UBO, still unused here
blendHeight            0.30
disabledBlendHeight    0.05
```

`blendHeight` and `disabledBlendHeight` were parsed into BWT2 and never copied
out until this work. Note blob 13 threshold is the literal `l(0.050000)` -
Abbey's **disabledBlendHeight**, not blendHeight. That permutation compiled with
the height blend off, falling back to the disabled constant. The blendHeight
permutation is presumably one of the other 18 blobs.

Settings -> Terrain -> **Blend Height** overrides it live, showing the map's
authored value alongside. The mix is baked into the pages, so it needs
**Rebuild VT** to take effect. It is saved per map like the rest.

## Reading the game's shaders

This has now paid off twice - once for decals, once here. The route:

```
shaders.pkg                        a zip
  -> shaders/<area>/<name>.fxo     also a zip
       -> "effect"                 an ARIEDX11 blob
            -> N x raw DXBC        find them by scanning for the "DXBC" magic,
                                   size is the u32 at +24
```

Then `fxc /dumpbin blob.dxbc` disassembles each one. `fxc.exe` is in the Windows
SDK under `bin\<version>\x64`. From Git Bash it needs running through PowerShell,
or MSYS mangles `/dumpbin` into a path.

The header region before the first DXBC holds the parameter and technique names,
so grepping it for readable strings tells you which blob is worth disassembling
before you read any assembly.

See also [game_deferred_decal.md](game_deferred_decal.md) for the same treatment
of the decal effect.

## Macro, normal and global map

Transcribed 2026-09-06 from the same VT baker (blob 13) and from the near-field
pass `shaders/terrain/terrain2_5.10.dx11.fxo`, blob 05 (the eight-layer,
shadowed permutation, 389 instructions). The two agree on the albedo path; the
normal and gloss path lives only in the near-field pass, because the game does
per-pixel layer blending at screen resolution under the camera and uses the VT
for the distance.

### The layer record's last three quads

`terrain2/layers` per layer, after the two projection vectors and the flags
word: three zero floats, then three float4s. The loader names them `r1`, `r2`,
`scale`; the game's constant buffer names them

```
r1     microDisplacement   (scale, offset, gamma, 1)   tessellation remap
r2     macroDisplacement   (scale, offset, gamma, 1)   the macro's remap
scale  blendMacroInfluence (albedo, normal, gloss, 1)  0..1, no negatives
```

Across all 1289 monastery records the third quad never goes negative and never
exceeds 1; the first two carry the negative offsets a displacement needs. The
mixer had been reading `r2.x` - a displacement scale - as the macro influence,
and never read `L.s` at all, though the loader has uploaded it all along.

### Albedo

```
m          = page mip fade toward macro (g_vtTileParams.w; ours: page_mip * macro_fade)
macro_term = lerp(m * macro, saturate(macro - tileMacroColor * (1 - m)), influence.x)
albedo     = (1 - m) * micro + macro_term
```

Close up, `m = 0`: `micro + influence.x * max(macro - mean, 0)`. The macro adds
only its variation above its own average. Every texel of micro detail survives,
whatever the influence. `tileMacroColor` is a per-layer constant the game feeds
in; the 1x1 mip of the macro AM stands in for it here (the tile border is a wrap
copy, so it does not skew the mean).

What was there before, `mix(micro, macro, influence)`, replaced the micro with
the macro in proportion. Rock_4 is authored at influence 1.0 on Abbey and its
macro tiles at eight times the micro, 56 m per repeat, 5 cm per texel: the rock
was mostly a blurred macro, in colour and in normal. That was the smeared rock.

### Normal and gloss

Winner-take-all, as before, then

```
micro  = decode(microNM.ag)                 fades to (0,0,1) with m
k      = lerp(min(influence.y, 1), 1, m)
macro  = lerp((0,0,1), decode(macroNM.ag), k)
n      = normalize(micro.xy + macro.xy, micro.z * macro.z)

gloss  = saturate(micro.r + influence.z * (macro.r - tileMacroColor.w))
         then toward macro.r with m
```

The macro normal is added as detail on top of the micro, never lerped over it.

### Global map

```
g      = saturate((blendGlobalThreshold - h_win) / blendGlobalThreshold) + m
albedo += g * (global - tileColorAvg * (1 - m))
```

`h_win` is the winning layer's (micro/macro lerped) height, `tileColorAvg` the
splat-weighted per-layer average colour (1x1 mip of the micro AM). Under the
camera the global map only shows where the relief is low - the crevices - and
only as its deviation from the tile colour. It is not mixed in. The old
`(base * c_l + global * g_l) / 1.8` laid the 34 cm per texel global over every
page at every distance.

### Still ours, not the game's

- AO is multiplied into the micro albedo at bake. The game carries it to the
  G-buffer and applies it at lighting; this G-buffer has no slot for it.
- The game's near-field pass blends per pixel with anisotropic `sample_d`; we
  bake once into 5 mm VT texels, which is sharp enough under the camera.
- Rock_4 authors 0.3 to 0.8 m of micro displacement. With tessellation off none
  of the game's relief on that rock can appear.
