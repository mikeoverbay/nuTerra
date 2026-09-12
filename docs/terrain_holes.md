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
W                 width  field
H                 height field
1                 version
```

then `W*H/8` bytes of bitmap: **W x H cells, one bit each, 8 cells per byte,
LSB-first**.

**The block is NOT always 64x64.** It was, on every map this was first written
against, and the loader hard coded that until 2026-09-11:

    19_monastery    W 64  H 64    512 bytes
    114_czech       W 16  H 16     32 bytes

See "the size is in the header" below - a 16-wide block read as if it were
64-wide walks four times past the end of its array.

The bit order was settled from the bytes, not assumed. MSB-first shatters the
continuous curve of a cliff edge into an 8-pixel sawtooth; LSB-first renders the
curve intact.

### The size is in the header, and both loop bounds come from it

```vb
Dim w As UInt32 = p_rd.ReadUInt32 / 4     ' W / 4
Dim h As UInt32 = p_rd.ReadUInt32 / 2     ' H / 2
Dim data(w * h) As Byte                   ' W*H/8 bytes - correct for any size
Dim cells_x = CInt(w) * 4                 ' back to W
Dim stride = CInt(w) \ 2                  ' bytes a row = W/8
For z1 = 0 To (h * 2) - 1                 ' H rows
    For x1 = 0 To stride - 1
        For q = 0 To 7                    ' 8 bits per byte
```

`w` and `h` are pre-divided, then multiplied back out by the loop bounds. Neither
holds what its name suggests - at 64x64, `w` is 16 - but `rows * stride` is
`w * h` exactly, which is the buffer already allocated, at every size.

    19_monastery   w 16  h 32   ->  64 x 64,  stride 8,  512 bytes
    114_czech      w  4  h  8   ->  16 x 16,  stride 2,   32 bytes

**The hard coded version cost a crash.** Until 2026-09-11 this read
`Dim stride = 8` and mirrored against `63`, so only two sizes worked: `w = 8`,
returned early as "no holes", and `w = 16`, where the constants happen to be
right. 114_czech is a quarter the width, so the loop walked four times past the
end of a 32-byte array and threw an **IndexOutOfRangeException on the update
thread** - which exits the process with no dialog and no stack. It reads as a
silent crash on load, and only a debugger says where.

The doc used to note the `w = 8` bail as "a silent data-dependent drop, not a
guard", and said nothing hits it today. Something did: a *different*
data-dependent assumption in the same function, and it did not drop silently, it
killed the process. Both bail and stride are derived now.

```vb
If w = 8 Then ' nothing so return empty hole array
```

`w = 8` means a width field of **32**, still returning empty. Whether a 32-wide
hole block really means "no holes" or is simply another size nobody has met is
**not established** - no map checked so far carries one.

**A short-read guard stays in as belt and braces.** With the stride derived it
cannot fire by construction, so if it ever logs, the header and the payload
disagree and the map is saying something not yet understood.

### The per-chunk X mirror

```vb
v.holes((cells_x - 1) - ((x1 * 8) + q), z1) = b
```

`cells_x` is the block's own width (`w * 4`), so this is `63 - x` on a 64-wide
block exactly as before, and `15 - x` on 114_czech's 16-wide one.

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

## Open: a sub-64 block only fills part of the array

`get_holes` always allocates `v.holes(63, 63)` and the terrain samples a 64-wide
grid per chunk. A 64x64 block fills it one-to-one. A **16x16 block fills only the
first 16x16**, and the remaining three quarters keep their default of "no hole".

That is the safe direction to be wrong in - missing holes rather than inventing
them - and 114_czech now loads clean with no warning. But whether those 16 cells
should be stretched across the chunk (one hole cell per 4x4 quads) or land in a
corner as they currently do is **not established**. Nobody has stood on a Pilsen
hole and looked.

Next step: find a chunk on 114_czech whose block is non-empty, and compare where
the hole renders against where the game puts it.
