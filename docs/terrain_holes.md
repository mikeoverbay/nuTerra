# Terrain holes

Holes are authored per chunk and punch terrain away so sub-terrain geometry -
tunnels, cave mouths, the gaps behind big rock cliffs - is not buried under the
heightfield.

Nothing is removed from the mesh. The vertices are still built, still uploaded
and still drawn; the fragment shader **discards before it writes the G-buffers**,
which is where the game masks too. So a hole costs a fragment, not a vertex, and
the terrain index buffer is identical with or without holes.

## Where the data lives

Each `.chunk` carries an optional zlib-compressed hole block, decoded in
`MapLoader/ChunkFunctions.vb`, `get_holes`. After inflation the payload starts
with a four-word header:

```
'hol' + NUL       magic
64                width  field
64                height field
1                 version
```

then 512 bytes of bitmap: **64x64 cells, one bit each, 8 cells per byte,
LSB-first**.

The bit order was settled from the bytes, not assumed. MSB-first shatters the
continuous curve of a cliff edge into an 8-pixel sawtooth; LSB-first renders the
curve intact.

### The header arithmetic reaches 64x64 by cancellation

```vb
Dim w As UInt32 = p_rd.ReadUInt32 / 4     ' 64 / 4 = 16
Dim h As UInt32 = p_rd.ReadUInt32 / 2     ' 64 / 2 = 32
Dim data(w * h) As Byte                   ' 16 * 32 = 512 bytes - correct
Dim stride = 8
For z1 = 0 To (h * 2) - 1                 ' 0..63 rows
    For x1 = 0 To stride - 1              ' 8 bytes per row
        For q = 0 To 7                    ' 8 bits per byte
```

`w` and `h` are pre-divided, then multiplied back out by the loop bounds
(`h * 2`) and by `stride`. The totals are right, but neither variable holds what
its name suggests: `w` is 16, not 64.

**Trap.** The bail is written against the divided value:

```vb
If w = 8 Then ' nothing so return empty hole array
```

`w = 8` means a width field of **32**. On a 32-wide hole map that returns an
empty array silently - no log, no assert. Every hole-bearing chunk checked on
D-Day is 64x64, so nothing hits it today, but it is a silent data-dependent
drop, not a guard.

### The per-chunk X mirror

```vb
v.holes(63 - ((x1 * 8) + q), z1) = b
```

X is mirrored within the chunk. Z is written straight - there is **no** Z flip,
whatever any surviving comment elsewhere may suggest.

This line is original code. It was removed once during this work, on the strength
of a side-by-side against `global_AM.dds` - but that comparison image was itself
rendered mirrored, because `global_AM` maps to world through a **negative affine
on both axes** - see the `world_from_uv` uniform in `Scene/MapTerrain.vb`.
Judged against a
correctly oriented render, the mirror is right, and it was restored.

If you ever suspect it again, re-derive the orientation from the terrain itself.
Do not judge it against a picture of `global_AM`.

## The mesh side

A chunk is **65x65 vertices** - 64 quads plus one duplicated seam row and column
so neighbouring chunks share an edge. UVs use `uvScale = 1/64`, so the chunk's UV
span is exactly 64 cells wide and the 64 hole texels land one-to-one on the 64
quads.

`get_terrain_mesh` samples the hole array per vertex into `h_buff`:

```vb
topleft.hole    = v_data.holes(topleft.uv.X    * hole_size, topleft.uv.Y    * hole_size)
bottomleft.hole = v_data.holes(bottomleft.uv.X * hole_size, bottomleft.uv.Y * hole_size)
```

The second line was a copy-paste of the first, sampling `topleft.uv` twice.
`bottomleft.hole` was therefore never assigned anywhere in the program - it is a
field on a module-level struct, so it kept its default `False` and every `j+1`
row wrote `hole = 0`.

That bug was invisible because **`h_buff` is consumed by nothing.** It is packed
into the W of the vertex normal:

```vb
vertices(j).packed_noraml = pack_2_10_10_10(.n_buff(j), .h_buff(j))
```

and no shader reads that W. The pack is vestigial. It is left in place because
removing it changes the vertex format, and the mask below does the actual work.

## The map-wide mask

Built in `write_terrain_buffers`, in the same loop that fills
`terrainMatrices`, because that is where a chunk's holes and its UV offset are
both in scope.

- **R8**, `chunks.X * 64` by `chunks.Y * 64` texels - one texel per hole cell.
- `0` = solid, `255` = hole.
- Each chunk is stamped at an origin derived from **its own
  `terrainMatrices(i).g_uv_offset`**, scaled by the mask size. This is the point
  of the design: the shader looks the mask up with that same UV, so any error in
  the chunk grid maths cancels instead of drifting the mask off the terrain.
- **NEAREST** min and mag. A hole is a hard boolean; linear filtering would
  feather every edge across a full terrain cell.
- `ClampToEdge`, and `UnpackAlignment = 1` because rows are not multiples of 4.

Load prints the result - D-Day, 14x14 chunks:

```
terrain hole mask: 896x896, 4221 hole cells set
```

## The lookup

`TerrainLQ.frag` and `TerrainHQ.frag`, first statement of `main()`:

```glsl
layout(binding = 4) uniform sampler2D HoleMask;
...
if (texture(HoleMask, fs_in.Global_UV).r > 0.5) discard;
```

`Global_UV` is the map-wide terrain UV the virtual texture already uses, so the
mask needs no UV of its own.

Discarding first means no G-buffer attachment is written for that fragment -
albedo, normal, position and depth all stay untouched, and everything downstream
(lighting, decals, SSAO) sees the hole as empty space rather than as dark
terrain.

### The dummy fallback is required, not defensive

`Scene/MapTerrain.vb`:

```vb
If HOLE_MASK_ID IsNot Nothing Then
    HOLE_MASK_ID.BindUnit(4)
Else
    DUMMY_TEXTURE_ID.BindUnit(4)
End If
```

Both fragment shaders declare and sample unit 4 **unconditionally**. A map with
no holes would otherwise leave whatever the previous pass parked on unit 4, and
the terrain would sample it as a hole mask. That is not hypothetical - it is
exactly how `SunShadowDepth` ended up being read by the particle shader: a depth
texture with comparison enabled, sampled through a plain `sampler2D`, reported by
the driver as undefined behaviour and appearing or vanishing with camera angle.

`DUMMY_TEXTURE_ID` is 2x2 RGBA8 cleared to zero (`TextureMgr.make_dummy_texture`),
so `.r = 0` everywhere and nothing is ever cut.

## What holes do not do

**The D-Day trenches have no authored holes.** The cliffs do. Whatever masks the
terrain inside those trenches is a separate mechanism - the trench model's own
`g_vertexColorMode = 2` dissolves the *model's* lip, not the terrain, and
`g_enableTerrainBlending` is declared in `ModelLoaders/PrimitiveLoader.vb` and
never assigned or read - the only other occurrences of the name in the source
are string literals in `Space.bin/modSpaceBin.vb`'s property tables. That is
still open.
