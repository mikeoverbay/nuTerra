# SrtViewer

Standalone viewer for SpeedTree `.srt` files, so tree loading can be worked on
without starting nuTerra and waiting for a whole map to load.

It shares no code with nuTerra on purpose — `SrtFile.vb` here is the reference
implementation of the format, and changes can be proven out in seconds before
being ported into `MapTrees`.

## Running

    SrtViewer.exe                                browse every .srt in the game packages
    SrtViewer.exe path\to\tree.srt               open one file from disk
    SrtViewer.exe vegetation/.../Maple_25m.srt   open one file from the packages
    SrtViewer.exe --filter maple                 only paths containing "maple"
    SrtViewer.exe --game "C:\Games\World_of_Tanks_NA"
    SrtViewer.exe --report                       decode everything, print stats, no window

The game install is auto-detected; without one the viewer still opens loose
`.srt` files, just untextured.

## Controls

| | |
|---|---|
| drag | orbit |
| wheel | zoom |
| left / right | previous / next file |
| up / down | solo one draw call, or back to all |
| `L` | cycle LOD |
| `W` | wireframe |
| `T` | textured / flat colour / UV debug / normals |
| `A` | alpha test on/off |
| `R` | reload |
| `Esc` | quit |

Per-file detail (draw calls, strides, offsets, triangle counts) prints to the
console as each file loads.

## Format notes

`SRT 06.0.0`, reverse engineered:

    0x00  char[16]  "SRT 06.0.0"
    0x10  uint32    flags
    0x14  float[6]  bounding box, min.xyz then max.xyz
    0x30  float[4]  LOD distances
    ...             wind coefficient tables
    strings         NUL terminated, 4 byte padded
    billboard mesh  uint32 nv, uint32 nidx, half[2] uv[nv], uint16 idx[nidx]
    draw calls      N entries of 40 bytes: vertex count at +0, index count at +12,
                    and a geometry type id in the 4 bytes *before* each entry
    geometry        one [vertices][indices] block per draw call, 4 byte aligned

There are two vertex formats. Most assets pack theirs as **half floats**, which
is what made this hard to spot in the first place:

    half[0:3]   position
    half[3]     constant 2.0        (reliable per-vertex marker)
    half[4:6]   texcoord
    half[8:11]  LOD position (morph target)

A handful use **float32** instead and run much wider - 64, 76, 88 and 108 bytes.
It packs as two vec4s, position and normal each carrying one texcoord component
in their w:

    +0   float3 position   +12  float u        vec4(position, u)
    +16  float3 normal     +28  float v        vec4(normal, v)
    +32  float  2.0        +36  float3 tangent the same foliage marker
    +52  float3 LOD position

`u` and `v` have only 36 distinct values across the 4493 vertices of
`linden_regular_tall`'s canopy, which is what an atlas of leaf cards looks like.
`SrtFile` treats any stride of 64 or more as this format.

In the **half float** format normals sit at the *end* of the vertex, three
unsigned bytes at `stride - 8` mapped `(b - 127.5) / 127.5`, with the tangent
four bytes after that and a handedness byte following each. (The float32 format
keeps its normal up front at `+16`, as above.)

    stride-8   ubyte[3]  normal
    stride-5   ubyte     handedness
    stride-4   ubyte[3]  tangent
    stride-1   ubyte     handedness

Three independent checks agree: the triples are unit length on 100% of vertices
(median 0.9998), normal and tangent are perpendicular (median `|dot|` 0.002),
and only the first of the two tracks the surface (trunk 0.837 / 0.980 foliage
correlation against geometric normals, versus 0.297 / 0.092 for the second).
The trunk scores lower because it carries *smoothed* normals -- testing against
the best adjacent face scores worse (0.772) than against the smooth average.

The stride 40 geometry type has no unit length triple at any offset, so its
normals are derived from the triangles instead; the viewer prints `nrm=file` or
`nrm=derived` per draw call and the title bar says which for the soloed part.

Four things are worth knowing:

* **Geometry starts inside the last table entry**, at `last_entry + 28`, not
  after it. That is also why the last entry's type id is unreadable — the
  vertex data has already overwritten it.
* **LODs are found from the type ids, not from counting.** The ids climb through
  a LOD, so a LOD ends wherever the next id fails to beat the last one. Testing
  for an id of zero is not enough - a LOD does not have to start at zero.
  `sunflower_var1` runs `3,5` then `1,3,5` then `0,2,4`: three LODs, only the
  last starting at zero. Reading that as two put two LODs in one bucket and drew
  both at once, one inside the other.

  LODs do *not* have to hold the same number of draw calls: `pheonixpalm_big01`
  has five in LOD0 and four in LOD1, because LOD1 drops a frond type.

* **Matching a part to its other LODs takes an alignment, not an index.** With a
  part dropped, everything after the gap shifts up - `olive_bush` keeps a stride
  40 part in LOD0 that LOD1 does not, so LOD1's third part is LOD0's fourth. The
  ids do not help either; they rise within a LOD but are not shared between LODs
  in any consistent way. What holds is that parts keep their order and their
  stride, so the two lists are aligned on their longest common subsequence of
  strides. Whatever fails to match is left unpaired rather than guessed at.

  The pairing decides which atlas a part is drawn with, so getting it wrong is
  visible: `olive_bush`'s LOD1 trunk classified as Unknown and was painted with
  the leaf atlas until it was paired with the LOD0 trunk.
* **The stride is never stored.** It has to be solved for, one draw call at a
  time (`SrtFile.WalkBlocks`). A candidate stride is kept only when its index
  buffer lands where the stride says and every index addresses a vertex that
  exists, and the positions decode inside the file's own bounding box. The chain
  then has to consume the file exactly. Those three tests together are decisive —
  every file that solves solves uniquely.

  **The position test needs a size check, not just a bounds check.** Index data
  read as float32 comes out as denormals a hair from zero, and zero is inside
  every bounding box, so an index buffer sails through a bounds-only test and a
  whole index region can be mistaken for a run of vertices. It cost an afternoon:
  a 515 KB "array of card placements" in `linden_regular_tall` turned out to be
  index buffers being read as positions. The test now also insists that at least
  half the sampled positions are more than a millimetre from the origin.

  Strides seen, in rough order of frequency: 28 (bones and collision hulls),
  48 (foliage cards), 32 (trunk and branch skin), 40, 36, 24, 20, 16, and for
  the float32 assets 108, 76, 64, 88.

  Around thirty assets give LOD1 a **more compact vertex format than LOD0** -
  `spruce_24m` runs 28/28/32/48 in LOD0 and 20/20/24/28 in LOD1, `maple_5m_dry`
  drops 12 bytes off every one of its. The decode is confirmed by the UV ranges,
  which come out identical between the paired LOD0 and LOD1 draw calls despite
  the different strides. Some of those compact layouts have no normals at
  `stride - 8`; the reader detects that and derives them from the triangles,
  and reports `nrm=derived` for the draw call.

  Note the bounding box at `0x14` stores its z pair the other way round in some
  files, so each axis has to be put back in order before it can be used.

`--report` re-checks all of this. Across the shipped library the type ids always
rise and no part ever pairs up twice, both zero. The 20 files where the LODs
disagree about a part are ones where the stride alignment pairs the wrong two
draw calls; it no longer matters, because the kinds come from the file rather
than from the pairing.

### Finding the geometry when the solver cannot

If the chain will not solve, the index buffers are the way in. A draw call's
index buffer nearly always opens `0, 1, 2, ...`, so searching for the bytes
`00 00 01 00 02 00` finds them: exactly ten hits for the ten draw calls of
`linden_regular_tall`, eight for the eight of `linden_regular_small`. Each hit
is where a vertex block ends, so the strides fall straight out of the gaps and
came back as clean multiples of four with the chain landing exactly on EOF.
That is how the float32 format was found.

## What the header declares

The file says what every draw call is; none of it has to be guessed.

**String table.** A count, that many 8-byte slots with the length in the
*second* dword, then the NUL-terminated blobs padded to those lengths. Reading
the slots as plain uint64 lengths lands the blobs four bytes late. Entry 0 is a
four-byte empty string and that is the one that matters — a texture index of 0
means the part has no texture at all.

    [ 0] ''
    [ 1] 'COLLISION'
    [ 2] 'Bamboo_cluster_AM.dds'
    [ 3] 'Bamboo_cluster_NM.dds'
    [ 4] 'Bamboo_cluster_SM.dds'
    [ 5] 'Atlas'
    [ 6] 'Bamboo_cluster_Bark_AM.dds'
    ...
    [10] 'NOCOLLIDE'
    [11] 'Bamboo_cluster_Billboards_AM.dds'

**Render states.** 680-byte records sitting just before the draw call table, one
per type id plus usually a trailing one for the billboard. The type id stored in
the four bytes before each table entry indexes them. Inside a record the three
texture layers are string indices:

    +0x04  diffuse   X_AM.dds
    +0x0c  normal    X_NM.dds
    +0x24  specular  X_SM.dds

So `bamboo_cluster_02` reads straight out:

    dc[0] tid=1  nv=29    -> ''                            the collision hull
    dc[1] tid=3  nv=2721  -> 'Bamboo_cluster_AM.dds'       leaves
    dc[2] tid=5  nv=726   -> 'Bamboo_cluster_Bark_AM.dds'  canes

The gap before the table is 68 bytes on most assets and 92 on some, so the array
is searched for rather than computed. Four checks pin the offset down: every id
must index a string that exists, every name must be blank or end `_AM.dds` with
a matching `_NM.dds` beside it, none may be the billboard atlas, and at least
one part must be drawn. The layer triple is what stops the search settling on an
offset shifted by one field, and preferring the billboard-present layout is what
stops it settling one whole record high.

900 of the 901 decodable assets resolve this way.

## Telling the parts apart

This is the fallback for assets whose render states cannot be located.


Foliage is the reliable one: its vertex declaration carries a constant 2.0 in
slot 3, so it is checked first. Bark and leaves are then told apart by **vertex
width**, which names the declaration outright:

| stride | foliage parts | bark parts |
|---|---|---|
| 32, 36, 40 | 0 | 464 |
| 48 | 1075 | 0 |

The skin declaration is position, a copy of it as the LOD morph target,
texcoord, then normal and tangent. The card declaration is wider and carries the
marker. Neither is ever used for the other job anywhere in the library, so the
stride settles it even for the 144 cards that do not carry the marker.

Judging bark by UV range instead — on the grounds that bark tiles along a branch
and runs outside 0..1 — fails on trunks that barely tile. `pheonixpalm_medium02`
reaches only 2.43, so its trunk came out unclassified and was painted with the
leaf atlas. UV range is still the fallback for the compact declarations at 28
bytes and under, which are not distinctive enough to read from the stride.

Collision hulls need two tests together, not one. A hull has to be too coarse to
be a surface — `sunflower_var1` opens with six distinct points and eight
triangles, a three sided tube up the stalk — **and** it has to be the same size
in every LOD, because a hull is authored once and shared while real geometry
always decimates. `drytree_01` is why the second half is needed: its stride 40
part drops from 19 vertices to 8 between LODs, so the LOD1 copy looks like a
hull on its own and is nothing of the sort. Getting this wrong is visible —
left as a surface, a capsule gets drawn with the leaf atlas, which is what put
orange slivers through the sunflower.

## Coverage

901 of 906 shipped `.srt` files decode (99%), 1,390,655 triangles. Run
`--report` to re-measure.

The five that still fail are all of `vegetation/outland/bb_tree_19_monastery/`:

    bb_tree_19_monastery_bush_small    bb_tree_19_monastery_oliva
    bb_tree_19_monastery_bush          bb_tree_19_monastery_tree
    bb_tree_19_monastery_cypress

They are billboard imposters, not trees. Each is 3,320 bytes holding a single
draw call of four vertices and six indices — one quad, indices `0 1 2 1 0 3` —
and the only textures named are `bb_tree_19_monastery_AM/NM/SM`. At stride 32
the chain lands exactly on EOF, so the layout is not in doubt; what stops them
is that **all four positions are zero**. The quad is sized from the bounding box
at runtime rather than stored.

So they are rejected by the size half of the position test, the same check that
keeps index data from being read as vertices. That is the right trade: decoding
them would yield a degenerate quad at the origin that draws nothing anyway.
