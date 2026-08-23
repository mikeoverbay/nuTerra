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
blendMacroInfluence    1.00    in the UBO; the per-layer array is what the game
                               actually uses, the global one is unused there too
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
