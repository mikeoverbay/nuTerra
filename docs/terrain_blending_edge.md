# Terrain blending — the game paints terrain ONTO models

Decoded 2026-08-31 from `shaders/post_processing/terrain_blending_edge.10.dx11.fxo`
in `shaders.pkg`, blob 1 of 41, ~180 instructions. Not implemented in nuTerra
yet — this document is the specification.

## The headline

**The game does not punch holes in the terrain.** There is no hole mechanism
behind trenches, foxholes or sunken structures. Instead a fullscreen
post-process **paints terrain albedo and terrain normal over the MODEL's own
G-buffer pixels**, faded in by how close each pixel is to terrain height, with
a triplanar noise dithering the boundary.

The model appears to merge into the ground because its lower edge is literally
rewritten to be ground.

This matters because the obvious approach — masking terrain by a model's
footprint — is wrong in *kind*, not merely in precision. Nothing is removed
from the terrain at all.

It also explains why D-Day's trenches have no authored holes while the cliffs
do: holes and terrain blending are unrelated systems solving different
problems. See `terrain_holes.md` for the hole system, which is real, is
implemented, and is used for cliffs.

## The pass, step by step

```
 1  reconstruct world position from depth (t8) through inverse-viewProj
 2  discard if world XZ falls outside the terrain bounds
 3  read the G-buffer flag byte (t7 alpha * 255, rounded):
       bit 128       selects g_terrainBlendingHeight vs g_disabledBlendHeight
       low 3 bits    == 5  ->  discard        (render-type exclusion)
 4  geometric normal from screen-space derivatives of the world position
       (deriv_rtx / deriv_rty on the reconstructed position, then normalize)
 5  triplanar noise: dominant axis of |normal|, world position * 0.33, sample t0
 6  terrain height: clamp XZ to bounds -> VT indirection (t1) -> height atlas (t2)
 7  blend = saturate( (H + terrainY - pixelY) / H )        H = the height from 3
 8  edge  = (blend - 0.5) * 2 + noise ;   discard if edge < 0.0001
 9  terrain albedo (t5) and terrain normal (t6) through the same VT pages
10  o0.xyz = terrain NORMAL * 0.5 + 0.5      o0.w = edge-weighted alpha
    o1.xyz = terrain ALBEDO                  o1.w = same
```

Two render targets out, so the pass rewrites both the normal and the albedo
G-buffer attachments for the pixels it keeps.

## What each piece is doing

**Height-driven, not mask-driven.** The whole blend is
`saturate((H + terrainY - pixelY) / H)`: 1 where the pixel sits at or below
terrain height, falling to 0 once it is `H` metres above it. There is no
authored mask, no footprint, no per-vertex weight in this pass.

**The noise is what makes it look natural.** A pure height fade gives a
horizontal band. `(blend - 0.5) * 2 + noise` then a discard turns that band
into a dithered, organic boundary — the model's edge dissolves into the ground
in irregular patches rather than along a contour line.

**The flag byte is the material's opt-in.** `g_enableTerrainBlending` on the
material rides into the G-buffer flag channel, and bit 128 there chooses
between `g_terrainBlendingHeight` and `g_disabledBlendHeight`. That is what the
material property is *for* — in nuTerra it is currently declared in
`ModelLoaders/PrimitiveLoader.vb` and never assigned or read.

**Low 3 bits == 5 is excluded.** A render-type code in the same byte. Almost
certainly terrain itself, so the pass does not blend terrain with terrain.
Not confirmed against a type table.

## Not to be confused with g_vertexColorMode

No vertex colour appears anywhere in this shader. The blend factor is height
plus noise, full stop.

`g_vertexColorMode = 2` is a separate mechanism that dissolves the **model's
own lip**, verified separately in the model shader asm. Measured on
`hd_mle_UNI_080_TrenchDday_*`, the vertex colour stream is:

| | Trench_01 | Trench_02 | Trench_04 |
|---|---|---|---|
| distinct values | 34 | 6 | 3 |
| white `0xFFFFFFFF` | 97.2% | 98.0% | 98.0% |
| dark, grey 58-80 | ~2.5% | ~2.0% | ~1.9% |

Always greyscale (R=G=B), alpha always 255. The dark verts sit in a thin band
at the **top rim, at ground level** (height fraction 0.81-0.97 on Trench_01),
and reach further out than the white body. Not baked AO — AO would darken the
enclosed interior, which is pure white here.

## Implementing it in nuTerra

Every input already exists: depth, world-position reconstruction, the terrain
virtual texture with height/albedo/normal pages, and a G-buffer flag channel
(`gGMF.b` carries a render type today).

What is missing:

- `g_enableTerrainBlending` is parsed into a material field that nothing reads.
  It needs to reach the G-buffer flag channel.
- A render-type code space that matches, or a deliberate nuTerra-specific one.
- The post-pass itself, after the G-buffer is complete and before lighting
  resolves — it rewrites albedo and normal, so it must run before anything
  reads them.
- A triplanar noise texture. The game samples `t0` at `world * 0.33`.

## Caveats on this decode

- **One blob of 41.** The others are near-certainly permutations (fog, TAA,
  DRR variants — the constant buffer carries all three). The core arithmetic
  was read from blob 1 only.
- The final alpha term multiplies several values that were re-used registers;
  the `edge` weighting is certain, the exact clamp around it is not.
- The `== 5` render-type exclusion is read correctly from the asm but its
  meaning is inferred.

## Reproducing the decode

```
shaders.pkg -> shaders/post_processing/terrain_blending_edge.10.dx11.fxo
  that .fxo is itself a ZIP; read its 'effect' entry
  scan for DXBC magic, u32 blob size at +24
  fxc /nologo /dumpbin <blob>
```

`fxc.exe` from the Windows 10 SDK. Run it from PowerShell — git-bash rewrites
`/dumpbin` into a path. See `wot-shader-crack-recipe` for the general method.
