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
