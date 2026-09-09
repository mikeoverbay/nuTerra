# Tank module — design paper

2026-09-09. Written before any code, from a read of the two Tank Exporter
loaders and the packages. Status: **proposal, awaiting the owner's go.**

Claims are marked. **VERIFIED** means read off bytes or off the loader source.
**REASONED** means derived. **DEFAULT** means a choice the owner has not made
yet and can overturn.

---

## 1. What it is

A new, isolated subsystem that loads a World of Tanks vehicle straight out of
the game packages and draws it on the map, so the tank shader from Tank
Exporter can be reproduced in nuTerra without touching the core resolve. The
owner spent weeks on the gloss / metal values in that shader; headlights read
as glass, metal and rubber look right, and it renders chrome. Reproducing it
inside the deferred pipeline failed twice on 2026-09-08; this module is the
place to do it on its own terms.

Test vehicle: **A88_M53_55** (USA tier 9 SPG). Two teams, red and green.
Room for more tanks later.

## 2. Isolation rules (non-negotiable)

* **New folder:** `nuTerra\Tanks\` for every `.vb`. **New shader subtree:**
  `nuTerra\shaders\Tanks\`. **Docs:** `docs\Tank Docs\`. The project is
  SDK-style, so new `.vb` files compile without touching the `.vbproj`, and
  `shaders\**` is already a content glob, so new shader files copy to `bin`
  on build (VERIFIED, `nuTerra.vbproj:99`).
* **Two touch points in the core, and only two:** one field on `MapScene`
  (construct / dispose, the pattern `lamp_fog` uses) and one draw call in
  `modRender.draw_scene`. Nothing else in the core changes. No new SSBO, no
  new bucket in the cull shader, no material slot.
* **Read-only use of core utilities**, never modification: `ResMgr.Lookup`
  and `PkgEntry.Extract` for package bytes, `ResMgr.openXML(entry)` for
  packed BigWorld XML (it already reads packed sections — VERIFIED,
  `ResMgr.vb:265`), `TextureMgr.find_and_load_texture_from_pkgs` for DDS
  upload, `GLBuffer` / `GLVertexArray` / `Shader`, the PerView UBO for
  `view` / `projection` / `cameraPos`, and `get_Y_at_XZ` for terrain height.
* **Shader file names must be unique across the whole tree.** The shader
  loader finds files by bare name over `shaders\**` (VERIFIED,
  `ShaderLoader.vb:283`), so `shaders\Tanks\tank_gbuffer.frag`, never
  `model.frag`.

## 3. The data — VERIFIED against the packages

For `vehicles/american/A88_M53_55/`:

| what | where |
|---|---|
| vehicle definition | `scripts/item_defs/vehicles/usa/A88_M53_55.xml` in `scripts.pkg` (BWXML packed, 114 KB decoded) |
| hull | `normal/lod0/Hull.primitives_processed` + `.visual_processed`, `vehicles_level_09-part1.pkg` |
| turret | `normal/lod0/Turret_01.*`, `part2` |
| guns | `normal/lod0/Gun_01.*` (155 mm M46) and `Gun_02.*` (203 mm M47) |
| chassis | `normal/lod0/Chassis.*`, `part2` |
| textures | `M53_55_<part>_01_{AM,ANM,GMM,AO}.dds` beside them, `_hd` variants in the `_hd` packages, shared `Details_map.dds` and `common_ID.dds` under `vehicles/russian/` |

The parts of a tank are split across `part1` and `part2` of the same tier
package; `ResMgr.Lookup` already indexes every package, so the split is
invisible to us.

### 3.1 The vehicle XML gives the assembly

Decoded and read (VERIFIED):

```
hull/models/undamaged            vehicles/american/A88_M53_55/normal/lod0/Hull.model
hull/turretPositions/turret      0.000000 0.368928 -1.2935
chassis/<last child>/models      .../Chassis.model
chassis/.../hullPosition         0.000000 1.24267 0.000000
turrets0/<highest price>/models  .../Turret_01.model
turrets0/.../gunPosition         0.000000 0.829 0.5251
turrets0/.../guns/<last>/models  .../Gun_02.model   (Gun_01 is the other choice)
```

`.model` names map to `.primitives_processed` + `.visual_processed` by
extension swap. Picking rules are the Python loader's (last chassis, highest
priced turret, last gun) — VERIFIED, `loaders.py:2570-2710`. The hull rides at
`hullPosition.y` (1.243 m) above the chassis origin; the Python loader reads
the same height off the chassis visual's `V` node instead. DEFAULT: use
`hullPosition` from the XML, cross-check against the `V` node on load and log
if they disagree.

### 3.2 The visuals give materials and nodes

The hull visual (VERIFIED, decoded): one `renderSet`, node `Scene Root`,
geometry `vertices` / `indices` (bare names), one `primitiveGroup` whose
material is `PBS_tank.fx` with, as `<property>name<Type>value</Type></property>`
children:

```
diffuseMap            ..._hull_01_AM.dds
normalMap             ..._hull_01_ANM.dds
metallicGlossMap      ..._hull_01_GMM.dds
excludeMaskAndAOMap   ..._hull_01_AO.dds
metallicDetailMap     vehicles/russian/Tank_detail/Details_map.dds
colorIdMap            vehicles/russian/common/common_ID.dds
g_detailUVTiling      8.41644 8.41644 0 0
g_detailPowerGloss    0.35      g_detailPowerAlbedo  0.11
g_detailPower         5         g_maskBias           0.22
g_useDetailMetallic   true      g_useNormalPackDXT1  false
alphaReference        64        alphaTestEnable      true
doubleSided           false
```

That is the full input set of the owner's tank shader, including the detail
map the earlier attempts lacked. The hull's hardpoints (`HP_turretJoint` at
exactly the XML's turret position, `HP_Fire_*`, `HP_TrackUp_*`,
`HP_Track_Exhaus_*`) are nodes with `row3` translations.

The chassis visual: **four** render sets — `track_R_Shape`, `track_L_Shape`
(`PBS_tank_uvtransform_skinned_ao.fx`, 12 bones each: `Track_<side><i>_BlendBone`,
`V_BlendBone`, virtual track bones) and `exportChassL1Shape` / `R1Shape`
(`PBS_tank_skinned.fx`, the wheels, 12 bones: `WD_*`, `W_*`, `V_BlendBone`).
102 nodes in all.

### 3.3 The primitives — section tables read off the files

| part | sections | vertex format | verts | indices |
|---|---|---|---|---|
| Hull | bare `vertices` + `indices` | `BPVTxyznuvtb`, secondary `set3/xyznuvtbpc` | 663240 B / 32 = 20726 | 50757, 1 group, `list` (16-bit) |
| Turret_01 | bare pair | same | 19647 | 47076 |
| Gun_01 | `gun_01Shape5.*` | `BPVTxyznuviiiwwtb`, `set3/xyznuviiiwwtbpc` | 45656 B / 40 = 1141 | 3342 |
| Chassis | 4 named pairs + 2 `.uv2` sidecars | `BPVTxyznuviiiwwtb` | tracks 85096 B / 40 = 2127 each; wheels 6625 / 6627 | 6264 / 18132 |

(Vertex counts are section size / stride; the in-section count field reads 0
on these files — VERIFIED — which is the "not trusted" quirk the Python
format doc records. Sizes are the ground truth.)

Layout per the two loaders (VERIFIED, `VISUAL_PROCESSED_FORMAT.md` and
`ModTankLoader.vb`):

* Section table at the end: last 4 bytes = offset back from EOF; entries are
  `size:u32, 16 junk bytes, namelen:u32, name, pad to 4`; bodies start at
  offset 4 and are laid out in table order, each padded to 4.
* BPVT vertex section: 68-byte primary format string, 64-byte secondary,
  `u32` count (untrusted), then the stream.
* Stride tokens: `xyz` 12, packed `n` 4, `uv` 8, `iii` 4, `ww` 4, `tb` 8.
  So `BPVTxyznuvtb` = 32, `BPVTxyznuviiiwwtb` = 40 — matches the sizes above.
* Index section: 64-byte format string, `u32` index count, `u16` group
  count, then `u16` indices for `list`, then per group
  `startIndex, nPrimitives, startVertex, nVertices` as `u32`.
* Packed normal encoding: in BPVT mode the VB loader decodes the **normal as
  8/8/8 unsigned bytes** (`b / 127.5 - 1`) and the **tangent and binormal as
  the signed 11/10/10 packing** (`x` bits 0..10, `y` 11..20, `z` 22..31,
  each `/ 511`). VERIFIED, `ModTankLoader.vb:1111-1114, 1410, 2829-2876`.
* The chassis and gun `iii` bytes are `palette_index * 3` (the engine's
  `vec4 bones[N*3]` convention) — VERIFIED in the Python project's notes; the
  `ww` bytes sum to less than 255 and want normalising in the shader.

## 4. The loader — raw buffers, no triangle build

The owner's instruction: only the raw vertex buffer data and the textures.
Tank Exporter unpacks every vertex into structs and builds triangle lists
because it exports; we draw, so we do not.

`TankPrimitives.Load(entry)`:

1. Read the section table. Group sections by base name into
   `(vertices, indices, uv2?)`.
2. For each group: parse the format string to a stride and a token layout;
   compute the vertex count from **size**, not the count field.
3. **Upload the vertex stream byte-for-byte** into one `GLBuffer` per
   section (`Storage` of a `Byte()` slice). No decoding on the CPU.
4. Build one `GLVertexArray` per section with attribute formats that read
   the raw bytes:
   * position: 3 × float at 0
   * normal: 4 × `UnsignedByte` normalised at 12 (8/8/8) — the fourth byte is
     junk and ignored by the shader
   * uv0: 2 × float at 16
   * bone indices `iii`: 4 × `UnsignedByte` **integer** (`AttribIFormat`) when
     present; weights `ww`: 4 × `UnsignedByte` normalised
   * tangent, binormal: 1 × **`UnsignedInt`** integer each; the vertex
     shader unpacks the 11/10/10 signed fields (a handful of shifts)
   * the `.uv2` sidecar, when present, as a second buffer on binding 1
5. Upload the index stream as-is (`u16`), keep the group records for the
   draw calls.

Every parsing decision above is a per-file fact recorded in
`03_formats_verified.md` as it is hit; anything the format string says that
the table does not cover is logged and the section skipped, never guessed.

`TankVisual.Load(entry)`: `ResMgr.openXML`, walk `renderSet` →
`primitiveGroup` → `material`, read the property pairs into a
`TankMaterial` (five texture paths, the six numbers, the three flags), and
the `node` list with `row3` into a name → position table. Bone palettes per
render set come from the `<node>` children of the render set.

`TankTextures`: the four maps per material through
`find_and_load_texture_from_pkgs`; the HD variant when the `_hd` package
has it (`ResMgr.LookupHD` exists for exactly this).

`TankVehicle`: the XML → part list with offsets (§3.1), each part a
`TankPart` = primitives + visual + textures + the part-local transform.

## 5. Placement and the two teams

A `TankInstance` is `(vehicle, world position, heading, team)`. DEFAULT for
the first test: one instance at the last snapshot's look-at point,
`(72.85, terrain, 47.03)` on `19_monastery`, heading 0, team green. Terrain
height from `get_Y_at_XZ`; the chassis origin sits on it.

Team is a per-instance attribute: green for allies, red for enemies, the
game's own convention. First use is a tint the shader can apply to the ID map
regions the way the game colours a vehicle's team markings; until the shader
exists it is carried and logged only. Instances live in a list on the module
so more tanks and more teams are additions, not changes.

**Handedness is a verification step, not a known.** nuTerra's world is the
game's mirrored in X for display (the map loader negates X on placements,
VERIFIED `MapParticles.vb:104`); the Python viewer flips Z for unskinned
parts and not for skinned ones. Which flip a tank needs here will be settled
by looking at the first draw with a debug normal view, and written down.

## 6. Drawing — milestone by milestone

**Milestone 1, the base loader.** Hull, turret, gun and chassis in **bind
pose**, drawn by `shaders\Tanks\tank_gbuffer.{vert,frag}` into the main
G-buffer with the model shaders' own convention (VERIFIED `model.frag`,
`model.vert`): `gColor` = albedo, `gNormal` = view-space TBN normal `* 0.5 +
0.5` from the ANM's `.ag` channels, `gGMF.rg` = the GMM's `.rg`, `gGMF.b` =
`GFLAG_MODEL`, `gPosition` = view-space position. The deferred pass then
lights, shadows and fogs the tank like any model, so the load can be judged
on screen with no new lighting code. Skinned parts are drawn with the skin
matrices at identity, which is the bind pose; the bone attributes are wired
through but unused. No track segments, per the owner.

**Milestone 2, the tank shader.** `shaders\Tanks\tank_pbr.{vert,frag}`, the
owner's `tank_fragment.glsl` (467 lines, 9 samplers: colour, normal, GMM, AO,
detail, camo, shadow, cube, BRDF LUT; sliders `A_level`, `S_level`,
`T_level`; `use_GMM_Toy`) ported over the same buffers. It lights the tank
itself and writes `gColor` with `GFLAG_UNLIT`, which the resolve passes
through untouched (VERIFIED, `deferred.frag:858-866`). That is the isolation
the whole module exists for: the resolve never touches a tank pixel. Depth
still comes from the shared buffer, so the tank sits in the scene.

**Milestone 3, articulation.** Turret yaw and gun pitch about
`HP_turretJoint` / `gunPosition`; chassis bones from the visual node tree
for wheel spin later. Not in scope until 1 and 2 are judged.

## 7. What is deliberately not done

* No triangle lists, no per-vertex structs, no CPU decode of normals.
* No track segment meshes, no track physics.
* No new material slot, SSBO, cull bucket, or resolve branch.
* No `res_mods` lookup for tank files in the first cut (the existing
  `LookupBySuffix` ignores `res_mods` too).
* Crash models, skins (`_skins/A88_M53_55_NY25_3Dst`) and LOD 1+ are known
  and skipped.

## 8. Files, when the go is given

```
nuTerra\Tanks\TankFormats.vb      format string -> stride / layout table, section table reader
nuTerra\Tanks\TankPrimitives.vb   raw buffer upload, VAO per section, group records
nuTerra\Tanks\TankVisual.vb       openXML walk: materials, nodes, bone palettes
nuTerra\Tanks\TankVehicle.vb      vehicle XML -> parts + offsets; TankInstance list
nuTerra\Tanks\TankRenderer.vb     the two draw entry points; owns the shaders
nuTerra\shaders\Tanks\tank_gbuffer.vert / .frag
docs\Tank Docs\02_loader_notes.md      what each file does, kept current
docs\Tank Docs\03_formats_verified.md  every byte-level fact, with the file it was read from
```

Core touch points: `MapScene.tanks` (construct / dispose) and one line in
`modRender.draw_scene` after `draw_models`.

## 9. Decisions taken (2026-09-09, the owner's answers)

1. **Team colour** is an ID billboard card above the tank carrying the
   player's name - here just the tank's name - coloured by team. Added after
   the loading is settled; `TankInstance.label` and `.team` already carry it.
2. **Parts**: the most expensive is the best in the game, so the priciest
   chassis, turret and gun win. On this vehicle that is the 203 mm `Gun_02`.
3. **Placement**: the centre of base 1, `(-TEAM_1.X, terrain, TEAM_1.Z)`.

## 10. Milestone 1 - landed 2026-09-09

Built, drawn and verified on a still at base 1 (see `02_loader_notes.md`).
One correction to §6 found on the first still: the skinned parts (gun and
chassis) are stored with Z reversed relative to the hull and turret, which
both exporters already knew and which now lives behind `FlipSkinnedZ`. Second
still: the 203 mm barrel runs forward over the engine deck, the spade at the
rear, the sprocket at the front.
