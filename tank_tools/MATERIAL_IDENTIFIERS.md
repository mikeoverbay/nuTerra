# Part identifiers in the game files, and what they say about crushability

The owner: *"There is a var in the visual for each model that tells us if it
can be crushed. `<identifier>d_wood0_1</identifier>`. dig in the game files and
look for these identifiers. a dozen of each kind."*

Scanned: `shared_content-part1/2/3.pkg`, all `.visual_processed`, `.havok` and
`.model` entries, from `C:\Games\World_of_Tanks_NA\res\packages`. Read-only.

## The shape of it

`<prefix>_<material><n>_<m>` — a **per-part** name, one per primitive group
inside a visual, not one per model.

| prefix | meaning | where it turns up |
|---|---|---|
| `s_` | static structure — `s_wall_N`, `s_ramp_N`, `s_armor_N` on tanks | everywhere, by far the most common |
| `d_` | destructible | Buildings only |
| `n_` | non-destructible | Buildings and Environment |
| one-offs | `c_`, `mb_verh1_1`, `arm_`, `jug_`, `top_`, `cut_`, `vel_smoke` | a handful each |

## `d_` versus `n_` is NOT destroyed-versus-normal

That was the first guess and the data kills it. Both appear **in the same
file, in the same LOD**, on different parts of one building:

```
hd_bld_AM_025_HousesShanty_01/normal/lod1/…visual_processed
    d_: d_metal1_2, d_metal1_3, d_metal4_1, d_wood1_1, d_wood2_1 …   (14)
    n_: n_metal3_1, n_metal3_2, n_metal5_1, n_stone4_2, n_wood6_1 …  (24)
```

It is a property of the PART. One shed has destructible plank walls and a
non-destructible frame, described in one mesh.

## The collision proxies are what make it usable

Across 526 `.havok` proxies carrying identifiers:

| | `s_` | `n_` | `d_` | other |
|---|---|---|---|---|
| havok collision proxies (526) | 510 | 52 | **1** | 24 |
| visuals | 1729 | 50 | 43 | 1 |

A `d_` part is in the render mesh and **not in the collision hull** — one
exception in 526 (`hd_envAM_032_ConcreteRings01`). So the game itself already
answers "can a tank drive through this part": if it is `d_`, there is nothing
there to hit.

## By category

| category | `s_` | `d_` | `n_` |
|---|---|---|---|
| Buildings (1094) | 564 — wall 405, ramp 159 | 308 — wood 227, metal 66, stone 15 | 222 — wood 132, metal 77, stone 13 |
| Environment (802) | 765 — wall 577, ramp 184 | – | 36 — wood 28, metal 8 |
| GatesAndFences (211) | 211 — wall 127, ramp 84 | – | – |
| MilitaryEnvironment (190) | 190 — wall 137, ramp 53 | – | – |
| Railway (126) | 126 — wall 106, ramp 20 | – | – |
| Decor (14) | 14 — wall 14 | – | – |

Destructible parts are a **buildings-only** idea. Fences, military props and
railway furniture are all `s_wall`/`s_ramp` — whatever the bake decides about
those, this field will not help.

`s_ramp_N` is worth its own look later: 159 building parts and 184 environment
parts are named as ramps, which is drivable geometry, not obstacle.

## A dozen of each

    d_wood      bld_101_04_VShed01            d_wood1_1
                hd_bld_AM_025_HousesShanty_01 d_wood3_3
                hd_bld_AM_027_WorkBarrack_01  d_wood4_5
                hd_bld_AM_033_ServiceStation  d_wood0_5
                hd_bld_EU_237_FisherHouse     d_wood3_3
                hd_bld_EU_239_FisherHouse     d_wood3_3
                hd_bld_SU_018_Shed_01         d_wood0_5
                hd_bld_SU_201_VHouse          d_wood1_7
                hd_bld_SU_209_VHouse          d_wood0_8
                hd_bld_AM_012_CottageLong     d_wood2_7

    d_metal     hd_bld_AM_025_HousesShanty_01 d_metal4_1
                hd_bld_AM_027_WorkBarrack_01  d_metal3_6
                hd_bld_AM_033_ServiceStation  d_metal4_6
                hd_bld_EU_237_FisherHouse     d_metal1_4
                hd_bld_EU_239_FisherHouse     d_metal0_5
                hd_bld_UNI_008_WorkshopNew    d_metal3_3
                hd_bld_AM_012_CottageLong     d_metal6_7

    d_stone     hd_bld_AM_033_ServiceStation  d_stone2_6
                hd_bld_AM_012_CottageLong     d_stone4_7

    n_wood      bld_101_04_VShed01            n_wood1_2
                hd_bld_AM_025_HousesShanty_01 n_wood4_3
                hd_bld_EU_237_FisherHouse     n_wood4_3
                hd_bld_EU_249_Shed            n_wood5_2
                hd_bld_SU_018_Shed_01         n_wood0_5
                hd_env_UNI_653_OldSailboat    n_wood1_1
                hd_envSU_83_05_Scaffold       n_wood0_2

    n_metal     hd_bld_AM_025_HousesShanty_01 n_metal3_1
                hd_bld_EU_237_FisherHouse     n_metal1_4
                hd_bld_EU_239_FisherHouse     n_metal5_1
                hd_env_UNI_633_WaterPump      n_metal1_1
                hd_bld_UNI_008_WorkshopNew    n_metal2_2

    n_stone     hd_bld_AM_025_HousesShanty_01 n_stone4_2
                hd_bld_AM_033_ServiceStation  n_stone2_6
                hd_bld_EU_239_FisherHouse     n_stone5_6

    s_wall      bld_101_03_Vhouse03, bld_101_04_VShed01,
                hd_bld_AM_030_RailwayStation, hd_bld_AM_036_Elevator,
                hd_bld_ASJP_004_Workshop, hd_bld_EU_006_Ahouse,
                hd_bld_EU_013_RailroadStation, hd_bld_EU_014/015/016/017_Tohouse,
                hd_bld_EU_020_Church                       (all s_wall_0)

    s_ramp      hd_bld_AM_009_MetalDepot_01, hd_bld_AM_013/014/015_TownShop,
                hd_bld_AM_016_Garage, hd_bld_AM_021_WareHouse,
                hd_bld_AM_024_BigCattleShad, hd_bld_AM_025_HousesShanty_01,
                hd_bld_AM_027_WorkBarrack_01, hd_bld_AM_029_Office,
                hd_bld_AM_030_RailwayStation, hd_bld_AM_033_ServiceStation
                                                           (all s_ramp_0)

## The base building does NOT come out crushable

Worth flagging, because it is the thing that started this. The owner: *"we can
run over a base. its crushable."*

`hd_bld_UNI_000_Base`, in `shared_content_sandbox-part2.pkg`:

    normal/lod0/…visual_processed   mb_verh1_1, n_wood0_1..5, s_wall_0
    normal/lod1/…visual_processed   s_wall_0
    normal/lod0/havok/…             no part identifiers

Every part is `n_` or `s_`. Under the `d_` rule this asset is solid, which
contradicts driving over it. Two possibilities and I have not separated them:
the thing you drive over is the capture circle rather than this model, or this
model is an exception the identifier does not describe. Not resolved — do not
special-case the base on the strength of a name until it is.

## What the bake would have to carry

The flag is per PART. The bake's ids are per PLACEMENT — 6,179 model
placements on monastery, one id for a whole building — so an id cannot express
"this wall is destructible and that frame is not".

So it wants a bit, alongside `solid_bit`, set per texel while the model pass
rasterises: **CRUSH_BIT, set when the part being drawn has a `d_` identifier**.
That is `nuTerra/Scene/MapFlightBake.vb`, which belongs to the nuTerra Work
session — a message, not an edit. Consumers then read it from
`<map>_meta.txt` the way they read every other bit.

## Verified: the runtime table carries the same strings

Open question from the engine session — the scan above read FILES offline,
while `cBSMA` is the map's own material table loaded at runtime, and two
sources that ought to agree have burned this project twice today already.

Settled without touching their code: `spaces/19_monastery/space.bin` carries
the identifier strings itself. 78 of them, which `cBWST` resolves by FNV hash:

| prefix | count | on monastery |
|---|---|---|
| `n_` | 38 | `n_stone0_1..7`, `n_stone1_1..4`, `n_metal3_1..2`, … |
| `d_` | 33 | `d_stone0_1..7`, `d_stone1_1..5`, `d_stone2_1..4`, … |
| `s_` | 6 | `s_nd_0`, `s_nd_1`, `s_ramp_0`, `s_ramp_1`, `s_wall_0`, `s_wall_1` |
| `ivy_` | 1 | `ivy_flat_01` |

So the file scan and the runtime table describe the same thing, and the
CRUSH_BIT request does not rest on an unverified link.

## What it is actually worth on monastery — small, both of them

Measured before anyone spends a bit, because "343 parts across the game"
is not the same claim as "343 parts on the map we are testing".

Every model asset on monastery, matched to its visual in the packages
(212 distinct assets, 7,137,362 collide texels; only 3,072 texels — 0.04% —
belong to an asset whose visual could not be found):

| | collide texels | share | assets |
|---|---|---|---|
| assets carrying a `d_` part | 179,022 | **2.5%** | 2 |
| assets carrying an `s_ramp` part | 2,227 | **0.03%** | 12 |

Both are UPPER bounds — the count is the whole asset, `n_` and `s_` parts
included, so the destructible and drivable share is smaller still.

The two destructible assets are the village houses, `bld_19_01_Vhouse_03`
(139,166 texels, 5 `d_` parts) and `bld_19_01_Vhouse_01` (39,856, 4 `d_`).
The ramps are flowerbeds, a well, a fountain, an arch, a Dodge WC54 and a
track decal — kerbs you drive over, semantically real and geometrically tiny.

### And the hull-growth defence does not rescue them

The fair objection to a raw texel count is that the planner sees the map grown
by half a hull, so small clutter becomes big no-go discs and a few hundred
texels could matter far more than they look. Tested by rebuilding
`collide_hull` with those assets deleted and counting cells that change from
blocked to free:

| removing | frees | of the map | of the free ground |
|---|---|---|---|
| every `s_ramp` asset | 10,295 cells | 0.02% | +0.02% |
| every destructible asset, whole | 209,312 cells | 0.31% | +0.43% |

Against 49,118,301 free cells to begin with. So on THIS map the crush bit buys
under half a percent of extra ground and the ramps buy nothing measurable, and
neither would show up in the 9% of tangent rings that turn at a real obstacle.

That is not an argument against the bit. It is an argument that monastery is
the wrong map to justify it on: the assets that are full of `d_` parts are the
shanties and work barracks (`hd_bld_AM_025_HousesShanty_01` alone carries 14),
and a map built from those would answer differently. Measure there first.

## Which map to settle it on — and monastery is not as wrong as I said

I told the engine session monastery was "the wrong map to judge on" because it
has none of the shanties that carry `d_` parts in bulk. That was asserted, not
measured, so here it is measured: every map package's `space.bin`, counting
distinct identifiers in its own material table (69 maps, 74 s).

| map | `d_` | `n_` | `s_ramp` |
|---|---|---|---|
| 47_canada_a | 96 | 120 | 2 |
| 252_br_battle_city4 | 95 | 113 | 3 |
| 217_er_alaska | 87 | 109 | 3 |
| 45_north_america | 87 | 108 | 3 |
| 59_asia_great_wall | 83 | 80 | 1 |
| 44_north_america | 80 | 99 | 1 |
| 06_ensk | 58 | 56 | 1 |
| … | | | |
| **19_monastery** | **33** | 38 | 2 |
| median of 69 maps | 18 | 21 | 1 |

Monastery is ABOVE the median, not an outlier at the bottom. So the excuse I
handed over — "measure it somewhere the assets actually live" — is weaker than
it sounded: the richest map has about three times monastery's variety, not ten.

Two cautions on reading this table, both of which cut the same way. A distinct
identifier is a MATERIAL, not a part and not an area: monastery's 33 `d_`
identifiers all belong to just two assets. And `s_ramp` barely varies at all —
one or two per map, everywhere — so nothing in this ranking rescues the ramp
argument.

`47_canada_a` is the candidate if anyone wants the area question settled
properly. That needs a flight bake of that map, which is a nuTerra run, not a
package scan — so it is a decision to take rather than something to slip in.

## Settled on 47_canada_a — and both hypotheses were wrong

The engine session baked `47_canada_a` (the top map in the table above, 96 `d_`
identifiers against monastery's 33) into its own sandbox, so the area question
could be finished without a baking programme. Read in place from
`C:\nuTerra_opus\_tmp\nuTerra\flight`. Same method both maps: match every model
asset to its visual, then rebuild `collide_hull` with those assets deleted and
count cells that flip from blocked to free.

| | 19_monastery | 47_canada_a |
|---|---|---|
| `d_` identifiers in `space.bin` | 33 | 96 |
| model collide texels | 7,137,362 | 13,231,727 |
| **`d_` assets** | 2.5% (2 assets) | **0.4% (4 assets)** |
| `d_` hull test | 0.31% of map, +0.43% free | **0.08%, +0.16%** |
| **`s_ramp` assets** | 0.03% (12 assets) | **1.6% (24 assets)** |
| `s_ramp` hull test | 0.02% of map, +0.02% free | **0.50%, +0.96%** |

**1. Identifier variety does not predict area, at all.** canada_a has three
times monastery's destructible material variety and FOUR TIMES LESS
destructible area. The 69-map ranking is therefore useless as a proxy for
where the crush bit pays, and so was my "measure it on a richer map" — the
richer map is the weaker one. A material is not a part and a part is not an
area, and here that stops being a caution and becomes the result.

**2. The ramps flip, and the engine session was right after all.** I told them
their `s_ramp` promotion was unsupported. On monastery it was: 0.02%. On
canada_a removing the ramp assets frees +0.96% of the free ground — 48 times
monastery's figure, and six times canada_a's own crush bit. Ramps are the
larger of the two effects on this map, which is exactly what they claimed and
I dismissed on one map's evidence.

Both remain under 1% of free ground, so neither is transformative anywhere yet
measured. What changes is the ORDER, and it changes per map.

### The measurement is bounded, and only the bit unbinds it

Every number here removes a WHOLE ASSET — its walls included — because
whole-asset attribution is all the bake's per-placement ids can express. So
+0.96% is an upper bound that mostly credits the ramp asset's walls to its
ramp, and canada_a's ramp assets include large buildings.

Which is the chicken and egg worth naming: the value of PER-PART information
cannot be measured with per-asset attribution. Every figure in this document
is an upper bound of unknown tightness, and the only way to a real number is
the CRUSH_BIT itself. That is an honest argument for building it cheaply and
measuring after, not for measuring harder first.

## The ask, restated: one lookup, one masked field — not two bits

Two corrections to what I asked for, both from the engine session, both right.

**Ramp and destructible are the same lookup.** Both are the prefix on the same
per-part identifier, reached the same way at the same place in MapLoader. So
"if only one thing gets done, do the ramps" gave up the crush flag for no
saving at all. There is one piece of work here, not two.

**And the key byte is nearly full.** Taken: kind `0x07`, outland `0x10`, solid
`0x20`, trunk `0x80`. Free: `0x08` and `0x40`, and then it is finished.
Verified rather than believed — the census below is the fraction of texels with
each bit set, over every bake on this machine:

| bake | `0x08` | `0x10` | `0x20` | `0x40` | `0x80` |
|---|---|---|---|---|---|
| 19_monastery | 0.00% | 2.96% | 11.05% | 0.00% | 0.09% |
| 114_czech | 0.00% | 2.52% | 11.78% | 0.00% | 0.03% |
| 47_canada_a | 0.00% | 8.10% | 21.98% | 0.00% | 0.10% |

`0x08` and `0x40` are genuinely unused, and there are exactly two of them.

### So spend them as one masked field with named constants

**Corrected.** An earlier version of this section argued that two independent
booleans "would consume the whole remaining byte and leave nothing, while a
field costs the same and keeps a spare". That is wrong arithmetic: two
booleans and a two-valued field both use bits 3 and 6 and leave the byte
equally full. The bit cost is identical. Caught by the engine session, and
worth leaving visible - a right design arriving with a wrong justification is
how a reviewer talks themselves out of it.

What the field actually buys is smaller, and still worth having:

* a **third mutually exclusive category for no extra bit**, held in reserve
* **`unstated` as a value rather than an absence**

The second is the one to defend. Plenty of visuals carry no identifier at all,
and missing is not the same as answered-no; two loose flags encode both as
"neither" and no reader can tell them apart. That is the same failure class as
StreetLamp-as-tree - not a wrong value, an unstated one that reads as a stated
one.

Mutual exclusivity holds by construction, checked rather than assumed: the key
comes from the fragment that won the depth test - one draw, one primitive
group, one material, one identifier - and the later passes that OR into the
byte only set the trunk bit or rewrite the kind, so neither can manufacture a
combination.

**And it must NOT be specified as a shiftable 2-bit field, because `0x08` and
`0x40` are not adjacent.** Writing it as values 00/01/10/11 invites
`((R & 0x08) >> 3) | ((R & 0x40) >> 5)`, which will be written correctly once
and copied wrong forever. A mask and three constants instead:

    crush_mask      = 0x48
    crush_unstated  = 0x00
    crush_destruct  = 0x08      d_       destructible
    crush_ramp      = 0x40      s_ramp   drivable, currently read as wall
    (0x48           = reserved, a future third category)

Test `R & crush_mask` against a constant. Nobody shifts, non-adjacency stops
mattering, and it reads like every other field in the meta. Readers on this
side will be written that way from the start.

After this the byte is finished. The next flag is a second byte or another
layer - a contract change, not a bit - so the reserved value should not be
spent casually.

**One implementation trap, from the engine session's own scar:** `add_water`
rewrites the kind on the CPU and preserves flags through an explicit OR mask.
`SOLID_BIT` had to be added to that mask or it silently dropped the bit on
148,370 monastery texels. Whatever this field ends up called has to go in the
same mask, and nothing will look wrong if it does not.

Name and values to be agreed with nuTerra Work in writing and declared in
`<map>_meta.txt`. Nothing is hardcoded on this side.

### A map worth keeping for rule checks

`47_canada_a` has no static tree bin at all — every plant on it is SpeedTree —
so its 1,489,171 solid-under-canopy texels are canopy over built or rocky
ground with nothing else mixed in. That makes it the clean map for sanity
checking a crushable rule, and it is 4.7x monastery on the very bit this
planner already reads. Monastery is not the worst case for foliage and should
stop being treated as typical.
