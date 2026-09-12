# The flight bake

A top-down snapshot of a whole map: for every 0.17 m of ground, what is the
highest thing standing there, how high it is, what kind of thing it is, and where
the bare terrain underneath sits.

It is produced by `nuTerra/Scene/MapFlightBake.vb` at map load and written to
disk. **Three sessions read it** - the camera flight planner, Path Studio's
viewer and the tank AI's route resolver - and they read the FILES, not the code,
so what follows is a contract rather than an implementation detail.

Written 2026-09-12. The bake existed long before this document; it got one when
it stopped being one session's private artefact.

**`bake_version` 2, 2026-09-12** - the SOLID bit and the per-object **id layer**.
Both change what the bake contains, so every version-1 bake on disk is rebuilt
once on first run. New in this version: `solid_bit` in the key byte,
`<map>_ids.u32` and `<map>_ids.csv`, and the `arena_*` keys.

---

## The files

Beside each other in `%TEMP%\nuTerra\flight`, one set per map:

| file | what |
|---|---|
| `<map>_top.rgba` | 8192 x 8192 x 4 bytes. The **key** and the **top height** |
| `<map>_floor.r16` | 8192 x 8192 x 2 bytes, uint16 little-endian. The **terrain alone** |
| `<map>_ids.u32` | 8192 x 8192 x 4 bytes, uint32 little-endian. **Which object** is on top |
| `<map>_ids.csv` | id ranges to model names. Small - 248 rows on monastery |
| `<map>_mask.png` | 2048 x 2048, black and white. Obstacle or not - a picture, not data |
| `<map>_meta.txt` | `key=value`, `#` comments. **Read this first; it describes the rest** |
| `<map>_kinds.csv` | only under `kinddump`: every model name and the kind it was given |
| `<map>_trees.csv` | only under `treedump`: every SpeedTree placement |

`%TEMP%` is **per user, not per checkout**. Two builds of nuTerra on one machine
share this folder and overwrite each other's bakes; a session that wants its own
redirects `TMP`/`TEMP` before launching - and must then remember that what it
verified locally is not what the others can see.

## `top.rgba` - one texel, four bytes

```
R   the key byte      kind = R & kind_mask, outland = R & outland_bit,
                      solid = R & solid_bit, trunk = R & trunk_bit
G   height, high byte
B   height, low byte
A   255
```

`height = height_offset + ((G << 8) | B) / height_scale`, both from the meta. At
`height_scale` 64 a step is 1.5 cm and the range is 1024 m.

**The low byte is in B, not alpha.** Alpha is the one channel something
downstream might premultiply, blend or drop on the way through an image tool, and
half a height silently becoming 255 is a hill. Alpha stays 255 so the file also
opens as a sane picture.

`floor.r16` is the same height encoding, little-endian, terrain only - no kind
byte, because the ground is kind 0 by definition and a byte a texel saying so is
67 MB of nothing. Being plain little-endian uint16 means the file **is** the
array: read it with one block copy, not a loop.

A cell at or below the meta's `empty` value means nothing rasterised there.

## Where a texel is

Row 0 is the `wz_max` edge and rows increase toward `wz_min`; column 0 is
`wx_min` and columns increase toward `wx_max`.

```
world_x = wx_min + (col + 0.5) * (wx_max - wx_min) / width
world_z = wz_max - (row + 0.5) * (wz_max - wz_min) / height
```

On a 1400 m map that is **0.1709 m a texel**.

**This mapping is a derivation, and a derivation is not a measurement.**
`verify_against_cpu` probes the floor against `get_Y_at_XZ_fast` - the terrain's
own height function - at 25 asymmetric points, and reports the mean error for the
mapping and for its three reflections:

```
flight bake: probe error as-derived    0.315 m   <- best
flight bake: probe error flip-x        7.332 m
flight bake: probe error flip-z        4.285 m
flight bake: probe error flip-both     6.792 m
```

If a reflection wins, the orientation is wrong and the log says which way. If all
four are large, something further up is wrong and no flipping will fix it. The
SPREAD is the point: a bake with the wrong offset or orientation cannot produce
it. It runs on the loaded path as well as the baked one, for the reason in
"checking a loaded bake" below.

## The key byte

Four fields in one byte. A reader that knows only the kind will see keys of
128..135 and fall off the end of its table, so read it with the meta's masks.

| field | mask | meaning |
|---|---|---|
| kind | `kind_mask` = 7 | 0 terrain, 1 building, 2 fence, 3 tree, 4 rock, 5 prop, 6 water, 7 other |
| outland | `outland_bit` = 16 | the scenery ring outside the playable area |
| solid | `solid_bit` = 32 | terrain-borne geometry over `obstacle_min_h` stands here, trees excluded |
| trunk | `trunk_bit` = 128 | a tree TRUNK stands here, whatever won the surface above |

**outland** is real geometry, but nothing should ever route into it, and almost
everything very tall on a map is out there - monastery's outland reaches 164 m.

**trunk** exists because the canopy owns the height. A ground vehicle should
treat the trunk bit as solid and the canopy as passable; a camera should do the
opposite and use the height. Without that separation every wood on the map is a
solid block and the tanks stay on the roads.

**solid** is the answer to canopy over rock, below. It is read from the depth
buffer at the one instant in the pass where the models are in and the trees are
not, so a texel that later keys `tree` because a canopy closed over it still
says whether something solid stands under that canopy. **A ground vehicle that
crushes foliage must test `kind = tree AND NOT solid`, never `kind` alone.**

Measured on monastery: **7,414,185 texels, 11.0% of the map**, of which
**317,776 also key as tree**. It is never set on a `terrain` texel - zero of
them - which is the invariant you would expect, since anything solid standing at
a texel is also the thing that keys it. 148,370 water texels carry it: the wall
or rock the water was raised over is still there.

Note the outland bit does not narrow that tree figure: outland is stamped per
model DRAW and the tree shader writes a constant kind with no place bit, so no
tree-keyed texel anywhere carries `outland_bit`. Do not read "inland" off it.

## `kind` is a substring race, and the order is load-bearing

`kind_of` lower-cases the model path and takes the **first** match:

```
fence, zabor, ograda, rail, hedge, gate, wire, palisade      -> fence
tree, bush, foliage, vine                                    -> tree
rock, stone, cliff, boulder                                  -> rock
building, house, church, barn, ruin, industrial, _bld_       -> building
vehicle, wreck, tank, car, truck, prop, misc, barrel, crate  -> prop
otherwise                                                    -> other
```

**Rock is tested before building, so "stone" beats "building".** On 19_monastery
nine of the seventeen names in the rock bin are not rock: `env_19_01_stonestairs`
x2, `env_19_23_StoneSteps` x4, `env_19_17_Gravestones` x2. A kind name cannot be
taken at face value.

**And the match is a plain substring, not a word.** `env_19_08_StreetLamp01`
and `02` keyed as **tree**, because "S`tree`tLamp" contains `tree`. Found by
cross-tabulating the key channel against the id layer: 3,046 monastery texels
key `tree` while carrying a MODEL id, and all of them were those two lamps plus
`hd_env_EU_456_BurnedTrees`, which is genuinely a tree.

**Fixed in `bake_version` 3** by testing `lamp` before `tree` and returning
`prop`. The fix is deliberately that narrow, because the obvious one is a
regression. Requiring a word boundary around every keyword would break fifteen
correct answers to fix one wrong one - a survey of all 212 monastery names found
**23 with a keyword buried inside a longer word, and most of them are right**:

| name | matched | verdict |
|---|---|---|
| `WoodFence`, `StoneFence`, `ForgedFence`, `GrapevineFence`, `RabitzFence` | `fence` | correct |
| `ItalyOutlandHousesCluster` | `house` | correct |
| `VendorCart`, `WoodenCart` | `car` | correct - a cart IS a prop |
| `Gravestones01`, `Gravestones03` | `stone` | wrong, harmless: solid either way |
| `BurnedTrees` | `tree` | correct |
| `StreetLamp01`, `StreetLamp02` | `tree` | **wrong, and the one that mattered** |

`lamp` appears in exactly two names on this map and both are lamps; no name
contains `light` or `lantern`, so neither was added on speculation. Add a keyword
when a map produces a collision, not before.

**`other` is a genuine bin** - 134 of monastery's 212 names. Mostly things a tank
flattens (petunias, clay jugs, milk cans, baskets, sidewalks, canisters), but it
also holds `env_19_12_RoadWall_*`, `env_19_18_Crypt`, a fountain, a well, a pier,
scaffolding, three wrecked vehicles the `tank`/`car` keywords missed
(`Pz_IV_AusfG`, `M4_Sherman`, `Dodge_WC54`), the map border pieces and the
outland mountains.

Run with `kinddump` for `<map>_kinds.csv` - every name and what it was classified
as - before trusting a bin.

**By area it is not close.** Inland, over 1 m above the floor, on monastery:

| kind | cells | share | median | max |
|---|---|---|---|---|
| tree | 4,183,578 | 45.8% | 5.20 m | 25.7 m |
| rock | 4,150,003 | 45.4% | 2.45 m | 29.1 m |
| building | 724,116 | 7.9% | 9.02 m | 56.0 m |
| other | 43,422 | 0.5% | 2.28 m | 59.3 m |
| fence | 23,803 | 0.3% | 1.62 m | 31.5 m |
| prop | 6,107 | 0.1% | 1.41 m | 28.4 m |

Rock is half of everything standing. Anything that drops or dims it drops half
the map.

## The colours

The kind palette lives **here**, in `KIND_RGB`, and is written into the meta as
`kind_<n>_rgb=r,g,b` so every renderer reads one table and the legend cannot
drift between two views of the same map.

| kind | rgb | |
|---|---|---|
| 1 building | `205,150,40` | amber |
| 2 fence | `230,90,40` | orange-red |
| 3 tree | `70,160,70` | green |
| 4 rock | `120,130,150` | blue-grey |
| 5 prop | `170,110,200` | purple |
| 6 water | `50,110,200` | blue |
| 7 other | `200,200,200` | light grey |

No key for kind 0: terrain is the ground, not a thing standing on it.

## The meta

Everything a reader needs, so nothing has to be assumed:

```
map, bake_version, game_version
exe, built, commit, written          provenance
width, height                        8192 x 8192
wx_min wx_max wz_min wz_max          the bake's world box
empty                                at or below this, nothing rasterised
obstacle_min_h                       1.0 m - what counts as an obstacle
format, height_scale, height_offset  the height encoding
kind_0..kind_7                       the names
kind_1_rgb..kind_7_rgb               the colours
kind_mask, outland_bit,              the key byte's fields
  solid_bit, trunk_bit
trunk_radius                         0.6 m
id_layer, id_names, id_format        the id layer and its sidecar
id_model_count, id_tree_base,        where the id space splits
  id_tree_count
arena_x0 arena_x1 arena_z0 arena_z1  the playable box - absent if unknown
```

`height_offset` is a whole number below the map's lowest point, **written rather
than agreed**: a map with a deeper pit than this one would otherwise encode
negative and clamp silently at zero, and a whole quarry would read as flat.

`written` is when the BYTES were baked, not when the meta file was last touched -
a bake loaded from the saved copy keeps its original stamp. `exe` is a full path
on purpose: several checkouts build this app on one machine and share one temp
folder, so the useful answer to "where did this bake come from" is which tree,
not `nuTerra.exe`.

### The bake box is not the arena box

`wx_min..wz_max` is the **terrain chunk footprint** (`100 * b_x_min` and
friends). The **arena** box is a different thing, read from
`scripts/arena_defs/<map>.xml`, and it is where the playable area is. The two are
independently derived, and the arena has fallen inside the bake on every map
checked - but that is empirical, not structural. Do not assume it.

**Both are now in the meta.** `arena_x0/x1/z0/z1` since `bake_version` 2, so a
reader no longer has to infer the playable area or parse the arena XML itself.
On monastery the arena is x -500..500, z -500..500 inside a bake box of
-700..700 both ways.

Two things about those keys. They are **absent, not zero**, when the arena XML
could not be read - four zeros presented as a playable area would have a reader
clip the whole map away, so the bake writes nothing and logs it. And the
containment is **checked at every bake**, not assumed: if the arena box ever
falls outside the bake box the log says so loudly, which is also what would
happen if the two were ever in different X frames. It has not fired.

## The id layer - which object, not just which kind

`<map>_ids.u32` answers a question the key channel cannot: **which** building,
not that a building is there. Two adjacent houses are one blob of key 1, and a
reader trying to say "this route passes that rock on the left" has to invent
objects by splitting a mask into connected components. This layer hands them the
objects.

```
0                          nothing: bare terrain, or nothing rasterised
1 .. id_model_count        a model placement
id_tree_base and up        a tree placement
```

Ids are **biased by one** so zero can mean nothing - the same bias the model
picker already uses. `<map>_ids.csv` names every range:

```
first_id,count,source,name
4,293,model,content\Environment\hd_env_EU_003_Cliff_rocks\...Cliff_rock_02.primitives
```

A row covers `first_id .. first_id + count - 1`; an id belongs to the row with
the greatest `first_id` not above it. **Ranges, not one row per object** - a
hedge is forty placements of one file, and monastery's 14,163 objects compress
to 248 rows and 24 KB.

**There is no kind column, deliberately.** The kind of what stands at a texel is
in the key channel, written by the same fragment that wrote the id, and a second
copy derived from a different string could disagree with it. `source` says which
half of the id space a row is in, which is structural.

**The id and the key at a texel always describe the same surface**, because one
fragment writes both and one depth test decides which fragment that is. Measured
on monastery, every kind against the half of the id space its ids came from:

| key | texels | id 0 | model id | tree id |
|---|---|---|---|---|
| terrain | 49,418,943 | **all** | 0 | 0 |
| building | 750,224 | 0 | **all** | 0 |
| fence | 31,545 | 0 | **all** | 0 |
| rock | 5,950,844 | 0 | **all** | 0 |
| prop | 9,704 | 0 | **all** | 0 |
| other | 2,180,848 | 0 | **all** | 0 |
| tree | 4,530,949 | 0 | 3,046 | 4,527,903 |
| water | 4,235,807 | 4,004,144 | 231,552 | 111 |

The two rows that are not clean are both correct. **tree** carries 3,046 model
ids because three static models classify as tree - the two street lamps above
and a burned-tree mesh. **water** is painted on the CPU after the passes, by
`add_water` raising a surface, so its ids are whatever was under the water: zero
over open water, a model where a jetty or a rock stands in the shallows.

**uint32, not uint16.** 134 MB was what the planners were told to expect, and
monastery's id space is 14,163 - a fifth of the 65,535 a u16 would allow. But
neither the model count nor the tree count is bounded by anything, and a map
that crossed the ceiling would wrap silently and hand back ids naming the wrong
objects. The count is logged at every bake, so this can be narrowed later on
evidence from a spread of maps rather than on one map's headroom.

## It is kept between runs

The bake is saved and reused rather than rebuilt every launch. Measured on
monastery: **6485 ms to build, 1126 ms to load.**

A saved bake is rejected, loudly and with a reason, when:

- `bake_version` differs - bumped **by hand** whenever what the bake *draws*
  changes (the trunk pass, the foliage alpha cut, the water raise, the despike).
  This is the one check no arithmetic can do for you: such a change leaves every
  number in the meta identical and the contents different. **If you change what
  the bake contains, bump it**, or the next run loads the bake from before your
  change and you will measure the old one - which looks exactly like your change
  having no effect.
- `game_version` differs - the res_mods folder the game itself is using. The map
  does not change unless a major release does.
- the file sizes, the map extent, the height scale, `obstacle_min_h`,
  `trunk_radius` or the key bits disagree.

`rebake` on the command line ignores the saved copy; the "Force rebuild
everything" button sets the same flag and reloads the map.

**The meta is refreshed on the loaded path too, and written only if it would
differ.** `export()` does not run on a cache hit, so without the refresh a saved
bake would keep its original meta for ever and a reader waiting on a new key
would wait for a rebake that has no reason to happen. And Path Studio stamps
these files every two seconds and reloads on a change that holds still, so
rewriting an identical meta each launch would cost it a reload carrying nothing.

### Checking a loaded bake

`report_coverage` prints the blocked share, the water and the tallest obstacle on
both paths, so a load and a bake can be read against each other - on monastery,
`16.5% blocked, 0.0% no terrain, tallest obstacle 164.1 m` either way.

**It is not sufficient on its own, and the reason generalises.** It measures
`top_m - floor_m`, a DIFFERENCE, and `height_offset` cancels out of a difference.
An offset read back wrong would shift every absolute height on the map and every
one of those numbers would be identical. The check and the thing it checks would
share the assumption. That is why `verify_against_cpu` runs on the loaded path
too: it asks the terrain's own height function, which knows nothing about the
bake or its offset.

The same trap caught the tank AI session from the other side - their route
validator sampled the centre line exactly as their planner did, so routes
measured clean while 12% of a finished drive had a hull overlapping solid
geometry. **A validator derived from the same assumption as the thing it
validates cannot see the error they share.**

## Two things a reader should not be surprised by

**Canopy over rock - now answered by the solid bit, and it was bigger than we
thought.** The top map is a SINGLE LAYER: one surface per texel, and the canopy
wins the depth test, so a rock under a bush keys as `tree` and a rule of the form
"a tank crushes trees" drives through it. Since `bake_version` 2 the depth buffer
is read once more between the models and the trees, and every texel with
terrain-borne geometry over `obstacle_min_h` carries `solid_bit`. **Read
`kind = tree AND NOT solid`, not `kind = tree`.**

The count that fix covers is **317,776 tree-keyed texels on monastery**, not the
**2,581 cells** this document carried before. The two are different measurements
and the new one supersedes nothing - it counts something wider. The old figure
came from an offline pass over an earlier bake with its own definition of what
counted; the new one is every texel where the in-engine solid pass stands more
than 1 m over the floor and the final key is `tree`, which includes buildings,
walls and fence edges under a canopy as well as rock, and counts at the bake's
own 0.17 m texel rather than a downsampled grid. Nobody has reconciled them
line by line, so treat 2,581 as retired rather than as confirmed.

**What it cost.** Two extra full-map readbacks - the solid depth and the id
layer - took monastery's bake from **6,485 ms to 8,786 ms**. The load path is
unchanged: the id layer is checked for size and never read into memory, because
nothing in the app consumes it.

**A bush under a tree reads at the tree's height.** Height is therefore not a
property of a plant: 136 monastery olives with a linden within 7 m read 9.8 m,
while the 828 standing alone read 4.8 m. Use the trunk bit for "does it stop a
hull", never the height.

## Tools

| argument | writes |
|---|---|
| `kinddump` | `<map>_kinds.csv` - every model name and its kind |
| `treedump` | `<map>_trees.csv` - every SpeedTree placement, with scale and bark |
| `treetrace` | per-species draw-call verdicts at load |
| `rebake` | ignore any saved bake and build it fresh |

## See also

`terrain_holes.md` for the hole block, `shadows.md` for the sun bake that shares
this draw path, `camera_flight_plan.md` and the Tank AI handoff for what reads
this data and why.
