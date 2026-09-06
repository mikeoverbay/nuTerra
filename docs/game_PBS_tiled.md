# The game's PBS_tiled family: dirt and the GCM

Source: `res/packages/shaders.pkg`, `shaders/std_effects/PBS_tiled.10.dx11.fxo`
blob 01 (198 instructions, the deferred pixel shader) and
`PBS_tiled_atlas.10.dx11.fxo` blob 01 (109). The `.11` builds of these carry
only forward-lit last-LOD variants; the `.10` builds hold the deferred pass.
Route for reading them: [terrain_blend.md](terrain_blend.md), "Reading the
game's shaders".

Transcribed 2026-09-06 after the monastery church rendered spotless while the
game shows grime streaks down every wall. `model.frag`'s `FX_PBS_tiled_entry`
loaded the dirt map into `maps[10]` and never read it; the two tiled-atlas
entries mixed dirt at a flat `dirtLevel * 0.35`, or had the line commented out.

## Dirt

Identical in `PBS_tiled` and `PBS_tiled_atlas`:

```
h    = height (albedo alpha, tint alpha included) of the DOMINANT tile
d    = blendMask.b * 2 - 1                signed dirt mask, on UV2
t    = saturate(d * 10) * (2h - 1) + (1 - h)
dirt = saturate((d*d - t) * strength * 6 + d*d) * dirtMap.a
albedo = lerp(albedo, dirtMap.rgb, dirt)
gloss  = lerp(gloss, gloss * glossUnderDirt, dirt)
```

The dirt map is sampled on UV1, the tile UV, so it repeats with the stone. The
curve is height-aware: where the mask is positive the dirt settles into the low
parts of the tile first, and `d*d` alone carries it once the mask is strong.

Which constant is which, per fx (the material names differ):

| | strength | gloss under dirt | GCM weight on the dirt |
|---|---|---|---|
| `PBS_tiled` | `g_dirtColor.w` | `g_dirtColorParams.x` | `g_dirtColor.x` |
| `PBS_tiled_atlas` | `g_dirtColor.w` | `g_dirtParams.x` | - |

`g_dirtColor.rgb` is never read by either. `g_envDirtColor` and
`g_staticDirtNormalMap` appear in the parameter block and are unused in every
permutation of these two.

## GCM (`PBS_tiled` only)

`colorTex` is a per-object colour map on UV2, pushed by the tile normal:

```
uv     = UV2 + normal.yx * g_fakeShadowsAndDetailParams.w
w      = dot(blendWeights, g_dirtColorParams.yzw)     per-tile weights
albedo *= 1 + w * (colorTex.rgb * 2 - 1)
dirt_rgb = dirtMap.rgb * (1 + w * g_dirtColor.x * (colorTex.rgb * 2 - 1))
```

so a mid-grey colour map is neutral and the per-tile weights say how much each
tile takes the object's colour. `PBS_tiled_global` has the fuller two-step
version (luminance then chroma), already transcribed in
`FX_PBS_tiled_global_entry`.

## Also in blob 01, not transcribed

- **Fake shadows.** The dominant tile's height is sampled again at UV1 pushed
  by the tangent-space sun direction times `g_fakeShadowsAndDetailParams.x`;
  `saturate((h_pushed - h) / params.y)` goes to the G-buffer, gated on
  `params.z > 0`. A cheap self-shadow from the height map.
- **AO** is written to the G-buffer as `1 - blendMask.a * MAO.g`, not
  multiplied into the albedo. nuTerra multiplies it in; the G-buffer has no slot.
- **Terrain blending** flag `g_enableTerrainBlending`, see
  [terrain_blending_edge.md](terrain_blending_edge.md).

## Loader note

`MapLoader` copied `g_tile2Tint` into both tile 1 and tile 2 for the two
tiled-atlas cases. Tile 1 now gets its own tint.
