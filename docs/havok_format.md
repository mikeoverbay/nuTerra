# The `.havok` files - the game's collision geometry, cracked

World of Tanks ships a `.havok` beside the render model of every collidable
thing: 9,680 of them across the 218 packages on the NA install, one per static
model LOD0 (`content/.../normal/lod0/<name>.havok`) and four per vehicle
(`vehicles/<nation>/<tank>/collision_client/{Hull,Chassis,Turret_NN,Gun_NN}.havok`).
They are **Havok 2020.2 binary tagfiles** and they hold the collision shapes,
the trigger volumes, and - the part that matters for driving - a Wargaming
block naming the **material kind** of every body: armour group, track, gun,
stone, wood, undamaged destructible.

Cracked 2026-09-16 by the Fable AI Navigation Helper session. Reader:
`havok_tools/wot_havok.py`. Every claim below was measured on bytes from the
packages; the checks are listed at the end.

## What is in one

```
hkRootLevelContainer
  namedVariants[0]                 name "default" (or the model's material name on destructibles)
    hkMemoryResourceContainer
      resourceHandles[]
        "Collision Physics Data"   hknpPhysicsSystemData   bodies with hknpCompressedMeshShape
        "HKBodyFlagsData"          HKBodyFlagsData         WG: material kind per body + bounds
        "Physics Physics Data"     hknpPhysicsSystemData   the SAME bodies as compounds of convex hulls
        "Trigger Physics Data"     hknpPhysicsSystemData   vehicles only: hull/turret/gun trigger volumes
        "hkndDestructionSystemData", "hkndIntegrationUtil::MeshInfo"   destructibles only
```

Every collision body is stored **twice**: once as a compressed triangle mesh
(what the game's ray casts hit) and once as a compound of convex hulls (what
rigid bodies collide with). On the AMX 13 FL11 turret the two decodes agree to
under 1 cm on every armour plate. That duplication is the free cross-check on
the mesh decoder - two encodings, one geometry.

A body is `hknpPhysicsSystemData::ExtendedBodyCinfo`: `name`, `shape`,
`position` (vec4), `orientation` (quat), `motionType` (0 = static on every
file read), `collisionFilterInfo` (1 collision, 0 trigger), `materialId`.
Positions and orientations were identity on every body checked; the shapes
carry their own placement.

Body names carry the same prefixes as the render mesh's `<identifier>`:
`s_armor_9`, `s_gun`, `s_leftTrack`, `s_tankEquipment`, `s_s_nd_0_wall`,
`n_n_wood0_1` - the prefix is doubled because the body is named
`<prefix>_<identifier>`.

## HKBodyFlagsData - the game's own answer to "what is this"

Wargaming's block, one `Info` per body:

```
name              the identifier without the leading s_/n_/d_  ("armor_9", "s_nd_0_wall", "wood0_1")
collisionFlags    0 on 96% of bodies; 14, 2, 3, 1, 64, 4, 66, 78 seen (meaning not decoded)
normalMatKind     material kind id, see table
destroyedMatKind  material kind id of the broken state, 0 when there is none
minBounds_ / maxBounds_   AABB of the whole model
```

`normalMatKind` indexes `system/data/material_kinds.xml` in `misc.pkg` (packed
BigWorld XML; the reader decodes it). The ids that matter:

| id | kind | | id | kind |
|---|---|---|---|---|
| 1-20, 51-62 | `armor_1` .. `armor_32` | | 71 | `speedtree_trunk` |
| 21 | `engine` | | 72 | `speedtree_foliage` |
| 22 | `ammoBay` | | 73-86 | `undamaged_1` .. `undamaged_14` (destructible, intact) |
| 23 / 24 | `leftTrack` / `rightTrack` | | 87-100 | `broken_1` .. `broken_14` (destructible, broken) |
| 25 | `gun` | | 101 | `grass` |
| 26 | `turretRotator` | | 104 / 105 / 106 | `forest_ground` / `ground` / `block` |
| 28 | `surveyingDevice` | | 109 / 113 | `firm_road` / `sand_road` |
| 29 / 30 / 31 / 32 | fuel tank / `radio` / `gunBreech` / transmission | | 111 / 112 | `stone` / `metal` |
| 41-48 | crew: commander, driver, radioman, gunner, loader | | 115 / 116 | `oil` / `blacksand` |
| | | | 253 / 254 / 255 | `water` / `wheel` / `smoke` |

So a monastery arch wall is kind 111 `stone`; a wood fence gate is 73
`undamaged_1` with a destruction system attached; an armour plate is its group
number. **This is the classification the bake's `kind_of` substring race has
been guessing at** (`flight_bake.md`): the game names it per body, in data.

## Shapes

| type | what | decode |
|---|---|---|
| `hknpCompressedMeshShape` | triangle/quad mesh in a `hkcdStaticMeshTree` | below |
| `hknpCompoundShape` | instances (rotation, translation, scale, shape) of the shapes below | apply the transform per instance |
| `hknpConvexShape` | `hknpConvexHull`: `vertices` (hkFloat3), `faces` (firstIndex, numIndices), `indices` | fan each face |
| `hknpTriangleShape` | a convex hull with 3 (triangle) or 4 (quad) vertices and a degenerate face table | take the vertices |
| `hknpBoxShape`, `hknpCylinderShape` | convex hulls with a full face table (a cylinder is 128 vertices) plus the box's `obb` / the cylinder's `a`, `b` axis points | fan the faces |
| convex hull with **no** face table | a flat plate or a point cloud (triggers, thin parts) | build the hull; a flat set gets a 2-D hull in its plane |

### The compressed mesh

`hknpCompressedMeshShapeData.meshTree` is a `hkcdStaticMeshTree`:

```
domain                 hkAabb of the whole tree
sharedVertices[]       uint64: x 21 bits | y 21 bits | z 22 bits, unit-scaled over domain
sharedVerticesIndex[]  uint16 into sharedVertices
packedVertices[]       uint32: x 11 bits | y 11 bits | z 10 bits, scaled by the SECTION's codecParms
primitives[]           4 x uint8 local indices (a, b, c, d); a quad when d != c
sections[]             codecParms[6] = offset xyz, scale xyz
                       firstPackedVertexIndex, numPackedVertices
                       firstSharedVertexIndex, firstPrimitiveIndex, numPrimitives
primitiveStoresIsFlatConvex   255 = quads/triangles, 0 = convex pieces (below)
```

A primitive's local index `i` is a packed vertex when `i < numPackedVertices`
(`packedVertices[firstPackedVertexIndex + i]`), otherwise a shared one
(`sharedVertices[sharedVerticesIndex[firstSharedVertexIndex + i - numPackedVertices]]`).
Triangle `(a, b, c)`, and `(a, c, d)` when `d != c`.

```
shared:  v = domain.min + bits * (domain.max - domain.min) / (2^bits - 1)
packed:  v = codecParms[0..2] + bits * codecParms[3..5]
```

Verified: every mesh's decoded bounds equal its `domain` to float precision;
the monastery arch's collision bounds sit 1-2 cm inside its render mesh's;
the AMX turret meshes match their convex-hull twins.

**Convex pieces** (`numTriangles == 0` and `numConvexShapes > 0` on the
SHAPE - not the tree's `primitiveStoresIsFlatConvex`, which is 0 on ordinary
rock meshes too): the mesh holds convex hulls instead of quads. Two places:

- `sharedVerticesIndex` becomes a list of piece headers `(info, firstVertex)`
  with extra words by flag: `info & 0x40` adds one word, `info & 0x80` adds two
  and marks the piece as EXTERNAL. A non-external piece is the shared vertices
  from its `firstVertex` up to the next piece's, hulled.
- external pieces are `hknpCompressedMeshShape.externShapes[]`, ordinary
  `hknpShapeInstance`s (rotation, translation, scale, a convex shape). The
  Tiger I hull is 247 of them under one mesh body; the reader draws every
  instance in that list.

Vehicle trigger volumes and a few hulls use this; every quad mesh has
`numConvexShapes == 0`.

A primitive whose indices repeat (`222, 173, 222, 173`) is an unused slot, not
a triangle. Skipping those made the decoded triangle count equal the shape's
`numTriangles` on every file checked.

## The tagfile container (the part that took the day)

Sections are `u32 big-endian size (top 2 bits flags, includes the 8-byte
header)` + 4-char tag, nested:

```
TAG0 { SDKV "20200200"; DATA; TYPE { TST1 TNA1 FST1 TBDY TPAD }; INDX { ITEM } }
```

Integers in the TYPE section are packed: lead byte `0xxxxxxx` 1 byte,
`10xxxxxx` 2, `110xxxxx` 3, `1110xxxx` 4 (27 bits), and **`0xE8` means "a
big-endian 32-bit value follows"** - seen for `INT_MAX` and `-1` template
arguments and in no reference parser (they stop at 27 bits).

- `TST1` / `FST1` - NUL-separated type and field name strings.
- `TNA1` - count, then per type: name index, template-arg count, `(name, value)`
  pairs; a `t`-named arg's value is a type index, a `v`-named one an integer.
- `TBDY` - per type: index (0 = skip), parent, flags, then by flag bit:
  `0x01` subtype, `0x02` pointer target (only when subtype kind >= 6), `0x04`
  version, `0x08` size + alignment, `0x10` abstract value, `0x20` members
  (count with **flag bits above bit 16 - mask to 16 bits**; each member
  name, flags, offset, type), `0x40` interfaces. Subtype kinds: 2 bool,
  3 string (also 0x83), 4 int (0x2000/0x4000/0x8000/0x10000 = 1/2/4/8 bytes,
  0x200 signed), 5 float, 6 pointer, 7 record, 8 array, 0x28 tuple (count in
  bits 8+).
- `ITEM` - 12 bytes each: `u32 type (low 24 bits) | flags (0x10 pointer, 0x20
  array)`, `u32 offset into DATA`, `u32 count`. Item 0 is null.

**Every pointer, array, string, `hkRelPtr`, `hkRelArray` and `hkRelArrayView`
in DATA is a 4-byte item index.** Type sizes in `TBDY` are the serialised
sizes, not native ones - a pointer is 4 and an `hkArray` is 4 - so the offsets
can be used as written. `hkHalf16` is the top 16 bits of a float32.

## Two things worth knowing before using it

- **The `.hkt` files are not the collision.** `lod0/havok/*.hkt.primitives_processed`
  (format `BPVTxyznuvitb`, stride 36) exist only for destructibles - 2 of
  monastery's 36 collidable models - and are the fracture render pieces. The
  `.havok` is the collision for everything. `open_threads.md` §0b calls them
  "the havok collision proxy format"; they are the destructible's pieces, and
  the collision hull is in the tagfile.
- **The frame is the game's.** No X negation was needed to match the render
  mesh; whatever the map loader does to a model's X, do to its collision.

## Checks that were run

1. `TNA1` and `TBDY` consumed to the exact end of their sections (only zero
   padding left) on every file read.
2. Decoded mesh bounds == tree `domain` on every mesh checked.
3. Compressed mesh vs convex-hull twin: same bounds to < 1 cm, 8 of 8 armour
   plates on the AMX 13 FL11 turret, 9 of 9 on the M53/55 hull.
4. Collision vs render mesh: monastery arch, collision 1-2 cm inside.
5. Full-install census: the table below.

## Census (all 9,680 files)

Every one of the 9,680 files parses and every body decodes to triangles:
`ok 9680, fail 0`, SDK `20200200` throughout, the same five TYPE sub-sections
in all of them. Totals: 2,770,396 packed and 2,922,714 shared vertices,
18,676,612 triangles.

| shape | bodies |
|---|---|
| `hknpCompressedMeshShape` | 67,387 |
| `hknpCompoundShape` | 54,044 |
| `hknpConvexShape` | 949 |
| `hknpTriangleShape` | 24 |
| `hknpBoxShape` | 18 |
| `hknpCylinderShape` | 9 |
| no shape (a body record without geometry) | 5,350 |

Material kinds over every `HKBodyFlagsData::Info` (one per body):

| kind | bodies |
|---|---|
| armour groups (1-32) | 42,679 |
| gun (25) | 1,912 |
| surveyingDevice (28) | 1,886 |
| leftTrack / rightTrack (23 / 24) | 2,128 |
| gunBreech (31) | 173 |
| stone (111) | 2,076 |
| metal (112) | 740 |
| undamaged destructible (73-86) | 2,342 |
| broken destructible (87-100) | 167 |
| wheel (254) | 64 |
| ground family (101-110) | 493 |
| kind 0 (unset) | 631 |

`collisionFlags`: 0 on 53,356, 14 on 1,129, 2 on 513, 3 on 250, 1 on 39, 64 on 18, 4 on 5, 66 on 5, 78 on 3. `destroyedMatKind` is 0 on every body.
