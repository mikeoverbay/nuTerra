# WoT `.vfxbin` particle format - what is decoded so far

Reverse engineered from `particles.pkg`, using the four
`Bld_19_01_Vhouse_05_Smoke_*` effects (the burning house on Abbey) as the
sample. `tools/vfxbin_dump.py` reproduces every number below.

nuTerra implements card particles on branch `particles-cardtest` (see
`nuTerra/Scene/MapParticles.vb` and `docs/PARTICLES_HANDOFF.md`). This document
is the format reference behind that. Particles are the ONLY route to the smoke
rising from a burning building - that smoke is not geometry, so no
volumetric-shader work can produce it.

## Container

Little-endian throughout. Records nest and are sized:

| offset | meaning |
|--------|---------|
| +0 | record id |
| +4 | record size in bytes, **including** the header |
| +8.. | 5 more u32 of header (contents vary by id) |
| name | 64-byte NUL-padded ASCII, at +28 or +32 depending on id |
| then | a fixed parameter block, then child records |

Record ids seen:

| id | meaning |
|----|---------|
| 1004 | root, `lod_effect_f`; +4 is the whole file length |
| 1003 | a LOD level, named `Lod_0-500` / `Lod_0-200` / `Lod_0-50`. The distance in the name appears as a float in its block (500.0 at +96 of the block) |
| 1001 | an emitter, named (`smoke_Big`, `sparksBig`, `Fire_2`, `AshSmall`) |
| 1000 | the emitter/source sub-block |
| 999 | the particle/renderer sub-block |

Nesting checks out by arithmetic: in Smoke_Small the 1003 at 80 has size 2004
and the 1001 at 240 has size 1844, and 80+2004 == 240+1844 == 2084.

## Emitter block (id 1000), offsets relative to the block

| offset | meaning | confidence |
|--------|---------|------------|
| +36 | emission rate, particles/second | high - medians by category rank as expected: sparks 12/s, debris 6, dust 4, smoke 3, fire 2 |
| +44,+48,+52 | emitter box half-extents (x, y, z) in metres | probable |
| +64,+68 | a symmetric angular pair, radians (`+-0.34907` = +-20 deg) | probable, but the median is 0 for most categories, so either most emitters author no spread or this is something else |
| +84,+88 | a small paired value, ~0.005 to 0.15 | UNIDENTIFIED. Was called "initial speed" in the first pass; that is wrong. It reads 0 for fire, debris, dirt and 3d meshes, and debris cannot have zero launch speed. Non-zero mainly for dust, smoke, water and sparks - the things that drift - so buoyancy or wind sensitivity is a guess, not a finding. |

## Particle block (id 999), offsets relative to the block

| offset | meaning |
|--------|---------|
| +48.. | diffuse texture path, NUL-terminated |
| +176,+180 | **size min/max, metres** |
| +184,+188 | **lifetime min/max, seconds** |
| +192..+204 | sub-rect into the atlas, 4 floats, ordered **(v_max, u_min, v_min, u_max)**. All multiples of 1/8 for an 8x8. The ordering was settled after this table was first written - see `modParticles.ParseEmitter` |
| +216 | a bitfield of SHADER FEATURE SELECTORS, not a rank. 64 distinct values, live bits only {0,1,2,3,5,6,7,8,26,27}. Bit 26 is set on 100% of `ps_long.fx` emitters and 0% of every other pixel shader; bit 3 on 100% of `ps_water_displacement.fx` and `ps_foam.fx`; bit 6 is strictly nested inside bit 0. It was previously nominated here as the likely home of a sort order - it is not, see "Render order" below |
| +220,+224 | **atlas rows, then columns** (stored as u32). This table said columns-then-rows in the first pass. The swap was settled over 3348 emitters by matching each region's aspect to its grid assuming square cells, and `modParticles` reads it rows-first. Invisible on the square 8x8 smoke sheets; wrong on `Fire_2` and `AshSmall` |
| +228 | atlas animation rate, fps |
| +272.. | pixel shader path (`\data\shaders\ps.fx`) |

## Keyframe tracks

The tail of the 999 block is a list of animation tracks:

```
[u32 count]['PPPP' = 0x50505050][count floats: times][count*stride floats: values]
```

Times are normalised 0..1 and always start at 0. `stride` is 1 for scalar
tracks and **4 for colour tracks (RGBA per key)**; it is resolved by checking
that the computed end lands on the next count+marker pair, or exactly on the
end of the block. Getting this wrong - assuming stride 1 everywhere - is what
produced impossible negative values in the first pass.

**Every emitter in all 4285 effects in particles.pkg has exactly 8 tracks**, so
the schema is fixed and the slot index is the property.

| track | role | evidence |
|-------|------|----------|
| 0 | **scale over life** - SETTLED, see below | rises in 4017 of 5785 emitters, falls in 380; median 0.66 -> 3.07 |
| 1 | unused | empty in all but 6 of 5785 |
| 2 | usually constant 1 | |
| 3 | **speed over life, with drag** | **falls in 5574 of 5785 (96%)**, median 4.81 -> 0.67. The best candidate for actual velocity, since the +84/+88 pair is zero for categories that must move |
| 4 | unidentified | flat in 3305, falling in 2231, median 0 -> 0. Goes negative. Emitters *named* `_rotation` all carry the same (0, 0.444) as unrelated ones, so rotation is NOT supported |
| 5 | a tool default - SETTLED, see below | flat in 4322 of 5785, and byte-identical across every emitter checked |
| 6 | **colour + alpha over life**, RGBA per key | unambiguous: sparks run warm white -> orange, fire white -> dark red, smoke grey -> blue, all with alpha rising from 0 and returning to 0 |
| 7 | a tool default, not authored | rises in 5484 of 5785 and ends on the *same* 3.467 nearly everywhere, so its meaning cannot be inferred from variation |

**Track 0 is the size track. Settled.** `modParticles.vb` used track 5 for a
while, on the argument that "every emitter's curve ends within a per cent of its
own maximum". That argument is circular - dump all eight tracks for the seven
smoke emitters of the three burning-house effects and tracks 2, 4, 5 and 7 come
back byte-identical every time:

```
track 2   t = 0, 1                      v = 1, 1
track 4   t = 0, 0.252, 1               v = 0.4462, 0.4781, 0.5737
track 5   t = 0, 0.27, 0.666, 1         v = 0.1576, 0.4524, 0.8714, 0.9938
track 7   t = 0, 0.0547, 1              v = 0, 0, 3.467
```

Track 5 always ends at 0.9938, so `0.9938 * sizeMax` is 99% of `sizeMax` for
every emitter you test - the observation carries no information. Track 0 by
contrast varies in values, key count AND knot times per emitter, which is what
authored data looks like:

| emitter | keys | knots | start -> end |
|---|---|---|---|
| Big/`smoke_Slow` | 5 | 0, .021, .124, .367, 1 | 0.572 -> 7.173 |
| Big/`smoke_Fast` | 4 | 0, .121, .475, 1 | 0.774 -> 2.740 |
| Med/`smoke_Slow` | 5 | 0, .076, .407, .813, 1 | 0.572 -> 7.172 |
| Small/`smoke_Fast` | 4 | 0, .236, .699, 1 | 0.774 -> 2.740 |

It is read **raw**, not normalised: the authored size range is the size at
track 0 = 1, and the curve carries the card past it. An A/B on a fixed camera
settles that too - normalising leaves separated puffs, raw closes them into the
game's continuous column.

For `smoke_Big`:

```
t = 0, 0.021, 0.124, 0.367, 1.0
v = 0.572, 1.650, 3.840, 5.703, 7.173
```

so a particle starts at 0.57x its base size and expands to 7.17x - a 12.5x
growth over its life. `smoke_Slow` shares that curve; `smoke_Fast` uses a
gentler 0.774 -> 2.74.

Colour tracks read cleanly, e.g. `smoke_Big`:

```
t    = 0,     0.198,               0.651,               1.0
rgba = (.651 .651 .651 0.000), (.529 .592 .651 .522), (.525 .655 .780 .286), (.553 .678 .882 0.000)
```

grey fading in to 52% opacity, drifting blue, fading back out.

## Decoded parameters, Abbey's burning house

| effect | emitter | rate /s | box (m) | spread | size (m) | life (s) | atlas | fps |
|--------|---------|---------|---------|--------|----------|----------|-------|-----|
| Big | smoke_Slow | 1 | 0.5^3 | +-20 | 7 .. 10 | 3 .. 4 | 8x8 | 2 |
| Big | smoke_Fast | 1 | 0.5^3 | +-20 | 7 .. 10 | 4 .. 6 | 8x8 | 16 |
| Big | smoke_Big | 5 | 0.5^3 | +-20 | 2 .. 4 | 3 .. 4 | 8x8 | 15 |
| Big | sparksBig | 20 | 1.7,1.2,1.7 | +-20 | 2 .. 3 | 2 .. 7 | 1x1 | 2 |
| Big | Fire_2 | 5 | 2,0,2 | +-50 | 4.97 .. 9.56 | 0 .. 1 | 8x3 | 12.5 |
| Small | smoke_Fast | 2 | 0.5^3 | +-20 | 4 .. 5.5 | 3 .. 4 | 8x8 | 16 |
| Ash_black | AshSmall | 30 | 5^3 | +-180 | 0.05 .. 0.2 | 1 .. 2 | 4x1 | 2 |

All emitters point at `particles/content_deferred/PFX_textures/eff_tex.dds`
and `\data\shaders\ps.fx`.

Sanity checks that raise confidence in the decode: `Fire_2`'s first track is a
clean monotone alpha fade `0.995 -> 0`, which is what a flame should do;
`AshSmall` emits 30/s of 0.05..0.2 m particles over +-180 degrees, which is what
ash should do; `sparksBig` has a 1x1 atlas, i.e. no sub-UV animation, which is
what sparks should have.

## Cross-validation

Medians by emitter-name category over 400 randomly sampled effects, which is
independent of the house that was used to derive the offsets:

| kind | rate /s | size (m) | lifetime (s) |
|------|---------|----------|--------------|
| spark | 12 | 0.015 .. 0.032 | 4 .. 10 |
| debris | 6 | 0.2 .. 0.35 | 2 .. 3 |
| dirt | 5 | 1 .. 1.55 | 1.5 .. 3.1 |
| dust | 4 | 0.55 .. 0.92 | 0.5 .. 1.5 |
| smoke | 3 | 1.13 .. 1.8 | 0.8 .. 2 |
| fire | 2 | 2 .. 3.5 | 0.7 .. 1.8 |

Sparks come out as 1.5-3 cm objects living 4-10 seconds, dust as sub-metre
puffs gone in under a second, fire at 2-3.5 m. Every category lands where
physical sense puts it, which is good evidence the size, lifetime and rate
offsets are right.

## Not yet decoded / uncertain

- The header u32s at +8..+27 of each record.
- Tracks 4 and 7, and the +84/+88 pair in the emitter block (see above).
- Gravity and rotation are not identified. Track 4 goes negative and is the
  only plausible home for rotation, but the naming evidence contradicts it.
- Emitter block +8/+12 hold `0x47435000` twice; purpose unknown.
- World-vs-local space is not located in the file. The engine has shader-side
  selectors for it - `g_localSpaceParticles` in `render_billboards_r` and
  `g_particleTransformMode` in `default_particles` - but neither appears in any
  `.vfxbin`.

## Render order - there isn't one, and that is the answer

Searched exhaustively and **there is no authored draw order, sort key, priority,
layer or sort-mode enum for FX or particles anywhere in the shipped data.** Do
not go looking again; this section is here to stop that.

What was checked and came back empty: every 4-byte slot of block 999's fixed
1280-byte parameter area and block 1000's fixed 148 bytes, read as u32/i32/f32
across all emitter records in `particles.pkg`; the record headers of ids 999,
1000, 1001, 1003 and 1004; the LOD record; the named property table of the
GPU-particle branch (all 271 distinct property names); the `.effbin` wrappers;
and the FX material definitions.

Two candidates that look right and are not:

- `+216`, covered in the table above. Flag lattice, not a rank. Its sibling
  values inside `Bld_19_01_Vhouse_05_Smoke_Big.vfxbin` are 0x02/0x01/0x41/0x02/
  0x41 - duplicated and non-dense, which no sort key is.
- `1000+40`, a 0..6 integer that varies between emitters. Killed by the
  rank-density test: it is all-distinct within a file in only 176 of 3670
  multi-emitter effects, and its commonest tuples are (1,1,1,1,1,1) and (0,0).

The reason none exists: **the game composites particles with order-independent
transparency** - moment-based OIT on the high quality preset, weighted-blended
on the low one - selected by a global preset, not per effect. Its GPU cull
appends visible particles with an atomic counter, which scrambles order
outright. A per-effect priority would have nothing to act on.

Blend mode IS in the file, but carries no information: `srcBlend` = 5 and
`destBlend` = 6 in every instance of the GPU branch. The only authored value in
FX content that constrains compositing order is the per-material blend pair in
`.visual_processed`, where `destBlend` runs [(2,718),(6,15),(7,8),(9,1),(3,1)] -
`destBlend=2` is additive and order-free, so the large majority of FX geometry
is order-independent by construction.

Authored render order does exist in this engine, just not for FX: GPU decals
carry `sortOrder` in their `.prefab` (`BW::GpuDecalComponent`), and space.bin's
WGSD decal record carries a priority u32. BigWorld's material schema also has a
`Sorted` bool, which survives on exactly two shipped materials - `footprint.mfm`
and `splodge.mfm` - and on no FX or particle material.

nuTerra therefore sorts by distance as a substitute for OIT, not as a port of
anything: `MapStaticModels.sort_fx_draws` for the FX meshes and
`MapParticles.BuildInstances` for the cards.
- `content_forward` copies exist alongside `content_deferred` and were not
  compared.


## Where particle effects are PLACED: the `BWPs` section of `space.bin`

The `.model` for a burning building is a `nodelessVisual` and carries no effect
reference, and the `.effbin` is only a wrapper naming the forward/deferred
`.vfx` paths. Placement comes from the map.

`space.bin`'s section table starts at offset **0** (not 0x14), with 24-byte
entries: magic[4], version u32, offset u64, length u32, extra u32. Abbey has 29
sections. The one that matters here is **`BWPs`** - BigWorld Particles:

```
[u32 record_size = 80][u32 count][count * 80-byte records]
```

80 * 98 + 8 = 7848 = the section length, so the layout is exact.

Each record is:

| offset | meaning |
|--------|---------|
| +0 | 4x4 world transform, row-major, 16 floats. Translation is the last row. The upper 3x3 is orthonormal (row 0 length 0.9999), confirming it is a rotation |
| +64 | u32 effect id - see below |
| +68 | i32, almost always 8 |
| +72 | i32, always -1 |
| +76 | f32, always 0.1 |

Abbey has **98 particle placements** referencing **14 distinct effects**.
Four of them sit on the burning house (game space negates X against nuTerra's,
so nuTerra's 146.3, -1, 10.9 is game space -146.3):

```
(-147.30,  4.96, 12.46)  id 72e5163f
(-146.67,  4.98, 15.68)  id 838db887
(-148.27, 10.93, 13.79)  id a0554f16
(-136.80, 19.07,  8.35)  id 223b6cd9
```

Rising heights - 4.96, 4.98, 10.93, 19.07 - i.e. a smoke column stacked up the
building, matching the four `Bld_19_01_Vhouse_05_Smoke_*` effects that ship in
that model's folder.

### The effect id is NOT cracked

The u32 at +64 is not a hash of the effect path under any algorithm tried:
fnv1a, fnv1, djb2, djb2-xor, sdbm, one-at-a-time, crc32, adler32, murmur2 and
murmur3, each against 11777 package paths and the 7262 canonical `.vfx` strings
embedded in the `.effbin` files, in full-path / stem / basename form, with both
separators and three cases. No match on any of the 14 ids.

`space.bin` contains no effect name strings at all (searched the whole 55 MB for
`Smoke`, `Vhouse_05_Smoke`, `effbin`, `.vfx`, `PFX`), so the id must resolve
against a global resource table built elsewhere. Cracking it is a prerequisite
for placing effects **generally**; it is NOT a prerequisite for the burning
house, where the four ids can be matched to the four effects in that building's
folder by elimination.
