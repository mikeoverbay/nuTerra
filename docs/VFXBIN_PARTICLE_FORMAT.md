# WoT `.vfxbin` particle format - what is decoded so far

Reverse engineered from `particles.pkg`, using the four
`Bld_19_01_Vhouse_05_Smoke_*` effects (the burning house on Abbey) as the
sample. `tools/vfxbin_dump.py` reproduces every number below.

nuTerra does not implement particles. This is the data a first implementation
would need, and is the ONLY route to the smoke rising from a burning building -
that smoke is not geometry, so no volumetric-shader work can produce it.

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
| +192 | **u_max** of the sprite sheet's region in the atlas |
| +196 | **v_min** |
| +200 | **u_min** |
| +204 | **v_max** |
| +220 | **rows** (u32) |
| +224 | **cols** (u32) |
| +228 | atlas animation rate, fps |

### The atlas region - SOLVED

The four floats are a rect, but the ordering is **(u_max, v_min, u_min, v_max)**
- right, top, left, bottom - which is why every reading of them as (x, y, w, h)
or (u0, v0, u1, v1) produced nonsense. The tell is that across the 309 distinct
quads in a 350-file sample, the first value is ALWAYS greater than the third and
the second ALWAYS less than the fourth. Scoring candidate rects against atlas
content had already ruled the obvious orderings out: only 32-58 of 300 quads
even formed a valid rect under them.

Read correctly the regions are clean squares on the sheet:

| emitter | u | v | size |
|---------|---|---|------|
| smoke_Slow | 0.125 .. 0.375 | 0.50 .. 0.75 | 0.25 x 0.25 |
| smoke_Big | 0.75 .. 1.00 | 0.25 .. 0.50 | 0.25 x 0.25 |
| commonest quad in the game | 0.562 .. 0.625 | 0.00 .. 0.062 | 1/16 x 1/16 |

and cropping smoke_Big's region out of `eff_tex.dds` gives exactly an 8x8 grid
of smoke puffs, matching its declared grid.

**+220 is ROWS and +224 is COLS**, not the other way round. Fire_2 breaks the
tie: its region is 96 x 256 px (aspect 3/8) with a declared "8, 3", so the grid
must be 3 wide by 8 tall. Checked across 3348 emitters by comparing each
region's aspect ratio against the grid on the assumption of square cells:

    +220=rows, +224=cols    median error 0.0000   87% within 2%
    +220=cols, +224=rows    median error 0.0161   53% within 2%

`eff_tex.dds` is a shared 4096x4096 sheet and is not the only one - a 350-file
sample also uses `eff_tex_long.dds`, `eff_tex_distortion.dds`, `eff_dirt.dds`,
`eff_tex_water.dds` and others, 82 distinct textures in all. Grids vary widely:
4x4, 1x1, 8x8, 8x4, 2x1, 2x2, 1x2, 8x2, 4x3, 4x2.
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
| 0 | **scale over life** | rises in 4017 of 5785 emitters, falls in 380; median 0.66 -> 3.07 |
| 1 | unused | empty in all but 6 of 5785 |
| 2 | usually constant 1 | |
| 3 | **speed over life, with drag** | **falls in 5574 of 5785 (96%)**, median 4.81 -> 0.67. The best candidate for actual velocity, since the +84/+88 pair is zero for categories that must move |
| 4 | unidentified | flat in 3305, falling in 2231, median 0 -> 0. Goes negative. Emitters *named* `_rotation` all carry the same (0, 0.444) as unrelated ones, so rotation is NOT supported |
| 5 | usually constant 1 | flat in 4322 of 5785 |
| 6 | **colour + alpha over life**, RGBA per key | unambiguous: sparks run warm white -> orange, fire white -> dark red, smoke grey -> blue, all with alpha rising from 0 and returning to 0 |
| 7 | a tool default, not authored | rises in 5484 of 5785 and ends on the *same* 3.467 nearly everywhere, so its meaning cannot be inferred from variation |

The **scale-over-life** track is the one that answers "start size / end scale".
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
- `999+216` is a bitfield, not a count or an index: a 350-file sample gives 47
  distinct values including 0, 1, 2, 3, 5..8, 65, 67, 69 and 0x04000002 /
  0x0C000002. Flags, meaning unknown.
- Gravity and rotation are not identified. Track 4 goes negative and is the
  only plausible home for rotation, but the naming evidence contradicts it.
- Emitter block +8/+12 hold `0x47435000` twice; purpose unknown.
- Blend mode, sort order and world-vs-local space are not located at all.
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


## The engine's own particle shaders

`shaders/gpu_particles/` holds 50 shaders that ARE the particle system, with
suffixes `_u` update, `_s` source/shape, `_r` renderer:

```
basic_update_u  physical_movement_u  external_force_u  wind_impulse_u
initial_direction_property_u  rotation_u  noise_u  vector_field_u
cone_s  cuboid_s  disk_s  ellipsoid_s  hemisphere_s  line_s  point_s
render_billboards_r  six_way_r  heat_haze_r  emissive_r  resolve_oit
terrain_collision_u  depth_collision_u  kill_by_condition_u
```

Their reflection data names the whole property model. Every "over lifetime"
property is a single float - an index into `g_particleCurves`, a float4
texture2darray - gated by a matching `g_useXxx` bool:

```
sizeOverLifetime      colorOverLifetime     animationOverLifetime
velocityOverLifetime  dragOverLifetime      rotationOverLifetime
gravityOverLifetime   noiseOverLifetime     forceOverLifetime
spiralOverLifetime    heightOverLifetime    accelerationOverLifetime
```

plus `g_flipbookParams0`, `g_uvFlip`, `g_stretchParams`, `g_velocityToLength`,
`g_lengthFromVelocity`, `g_rotationParams0..2`, `g_pivotOffset`, `g_tintColor`,
`g_dragCoefficient`, `g_applyGravity`, `g_accelerate`.

That is the vocabulary our 8-track schema is drawn from, and it gives much
better candidates for the unidentified slots than guessing: track 4 (often 0,
sometimes negative) fits rotation; track 7 (0 -> 3.467 nearly everywhere) fits
animation progress.

**Caveat:** this is the NEWER GPU system. Our .vfxbin names
`\data\shaders\ps.fx`, the older CPU path, whose renderer is
`shaders/wg_particles/default_particles` and `std_effects/sprite_particle`.
That older vertex shader takes POSITION, COLOR, TEXCOORD and passes UV straight
through, so in the old system the atlas rect and frame are computed host-side -
which is why the region had to be decoded from the .vfxbin rather than read out
of a shader constant.
