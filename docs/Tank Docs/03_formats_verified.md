# Tank files — byte-level facts, verified

Every rule the loader relies on, with where it was read and what it was
checked against. Add to this when a new file shape turns up; never widen the
loader on a guess.

## `.primitives_processed`

Read off `ModTankLoader.vb` (VB Tank Exporter) and
`VISUAL_PROCESSED_FORMAT.md` (Tank Exporter PY), checked against every
section of A88_M53_55's four parts by size arithmetic.

* **Section table at the end.** Last 4 bytes: offset back from EOF to the
  table. Entry: `size:u32`, 16 unused bytes, `namelen:u32`, name, pad to 4.
  Bodies start at file offset 4, in table order, each padded to 4. No entry
  carries an offset.
* **Vertex section preamble.** 64-byte format string. When it starts with
  `BPVT`: a 64-byte secondary string follows at +68 (`set3/xyznuvtbpc`,
  `set3/xyznuviiiwwtbpc`), then `u32` count at +132, **body at +136**. Plain
  formats: `u32` count at +64, body at +68. The count field reads **0** on
  every A88_M53_55 section - derive the count from `(size - body) / stride`.
  Checks: Hull `(663240 - 136) / 32 = 20722` exactly; track `(85096 - 136) /
  40 = 2124`; wheels `(265016 - 136) / 40 = 6622`; gun `(45656 - 136) / 40 =
  1138`. With a 68-byte preamble none of these divide.
* **Stride tokens.** `xyz` 12, `n` 4 (12 only for the exact string
  `xyznuv`), `uv` 8 each, `iii` 4, `ww` 4, `tb` 8. So `BPVTxyznuvtb` = 32 and
  `BPVTxyznuviiiwwtb` = 40; offsets: pos 0, normal 12, uv0 16, then either
  tangent 24 / binormal 28, or bones 24 / weights 28 / tangent 32 / binormal 36.
* **Index section.** 64-byte format string (`list` = 16-bit, `list32` =
  32-bit), `u32` index count at +64, `u16` group count at +68, indices from
  +72, then per group `startIndex, nPrimitives, startVertex, nVertices` as
  `u32`. Check: Hull `72 + 50757 × 2 + 1 × 16 = 101602` = the section size.
* **Packed normal, BPVT streams: 8/8/8 unsigned bytes**, `b / 127.5 - 1`,
  fourth byte unused (`unpackNormal_8_8_8`, VB, used when `BPVT_mode`).
* **Packed tangent and binormal: signed 11/10/10**, x bits 0..10, y 11..20,
  z 22..31, each sign-extended and divided by 511 (`unpackNormal`, VB, used
  for `t` and `bn` in every mode). The vertex shader reproduces the VB shifts
  exactly: `(p << 21) >> 22`, `(p << 11) >> 22`, `p >> 22`.
* **Bone bytes** are `palette_index × 3` (the engine uploads bones as
  `vec4[N*3]`); weights are bytes that can sum below 255. From the Python
  project's notes; unused so far (bind pose).
* **`.uv2` sidecar**: parallel to the vertex stream, 8 bytes per vertex,
  same preamble shape as the vertex section (136 bytes when BPVT). Present on
  the two track sections of the chassis: `17128 - 136 = 16992 = 2124 × 8`.

## A88_M53_55, normal LOD 0

| part | package | sections | format | verts | indices |
|---|---|---|---|---|---|
| Hull | `vehicles_level_09-part1.pkg` | bare `vertices` + `indices` | `BPVTxyznuvtb` (32) | 20722 | 50757 |
| Turret_01 | `part2` | bare pair | same | 19643 | 47076 |
| Gun_02 | `part1` | `gun_02Shape5.*` | `BPVTxyznuviiiwwtb` (40) | 1147 | 3258 |
| Chassis | `part2` | `track_R_Shape`, `track_L_Shape` (+`.uv2`), `exportChassL1Shape`, `exportChassR1Shape` | 40 | 2124 / 2124 / 6622 / 6624 | 6264 / 6264 / 18132 / 18132 |

Every section has one primitive group. Bounding boxes from the visuals:
hull Z −4.63..3.91, turret Z −2.08..1.47, gun Z −0.11..4.42 (the barrel runs
+Z from the pivot), chassis Z −2.89..3.60.

## The vehicle XML (`scripts/item_defs/vehicles/usa/A88_M53_55.xml`)

BWXML packed; `ResMgr.openXML` decodes it. Read off the decoded file:

* `hull/models/undamaged` = `.../normal/lod0/Hull.model`; `.model` →
  `.primitives_processed` / `.visual_processed` by extension swap.
* `hull/turretPositions/turret` = `0 0.368928 -1.2935` (hull-local).
* `chassis`: two children, both `Chassis.model`, `hullPosition` =
  `0 1.24267 0`. The priciest is `Chassis_A88_M53_55_2`.
* `turrets0/Turret_1_A88_M53_55`: `price 100`, `gunPosition` = `0 0.829 0.5251`,
  guns `_155mm_Gun_M46` (Gun_01) and `_203mm_Howitzer_M47` (Gun_02); the
  priciest is the 203 mm.
* Skins live under `hull/models/sets/A88_M53_55_NY25_3Dst`; not loaded.

## The visuals

* Hull: one render set on `Scene Root`, geometry `vertices` / `indices`,
  material `tank_hull_01` / `PBS_tank.fx` with `diffuseMap`, `normalMap`,
  `metallicGlossMap`, `excludeMaskAndAOMap`, `metallicDetailMap`
  (`vehicles/russian/Tank_detail/Details_map.dds`), `colorIdMap`,
  `g_detailUVTiling 8.41644 8.41644 0 0`, `g_detailPower 5`,
  `g_detailPowerGloss 0.35`, `g_detailPowerAlbedo 0.11`, `g_maskBias 0.22`,
  `g_useDetailMetallic true`, `g_useNormalPackDXT1 false`, `alphaReference 64`,
  `alphaTestEnable true`, `doubleSided false`. Properties are
  `<property>name<Type>value</Type></property>` - text then one child element.
* Chassis: four render sets - two tracks (`PBS_tank_uvtransform_skinned_ao.fx`,
  12 bones each) and two wheel sets (`PBS_tank_skinned.fx`, 12 bones each);
  102 nodes. The track materials ship no AO map.
* Textures: every part's four maps resolve, and the `_hd` variants exist in
  `vehicles_level_09_hd-part*.pkg` for all but the shared detail map.

## Frames

* nuTerra's world is the game's mirrored in X (the map loader negates X on
  placements); the base-1 ring is drawn at `(-TEAM_1.X, ., TEAM_1.Z)`.
* Skinned parts (gun, chassis) are stored with Z reversed relative to the
  hull and turret. Seen on the first still: the 203 mm barrel drawn out over
  the recoil spade at the rear. Both exporters negate Z for exactly these
  parts. `FlipSkinnedZ` applies it; the still after it is the verification.
