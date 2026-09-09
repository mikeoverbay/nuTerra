# Tank module — loader notes

What each file does, kept current with the code. Read
`01_tank_module_design.md` for why the module exists and
`03_formats_verified.md` for the byte-level facts.

## Files

| file | does |
|---|---|
| `nuTerra\Tanks\TankFiles.vb` | The module's own package index. The core's `ResMgr` skips every `vehicles_*.pkg` on purpose (`ResMgr.vb:46`), so this walks the same `paths.xml`, indexes only those packages (38 of them, 270 965 entries on this install, once, lazily) and shares the archive handles through `PkgEntry.ArchiveFor`. `Lookup` tries the index then the core; `LookupOwn` only the index; `LoadTexture` tries the `_hd` name first, then the plain one, and feeds the bytes to the core's `TextureMgr.load_dds_image_from_stream`, which caches by name. |
| `TankFormats.vb` | The section table reader and the format-string walker. `TankVertexLayout.Parse` turns `BPVTxyznuviiiwwtb` into offsets and a stride; anything it does not recognise returns `Nothing` and the section is skipped, logged, never guessed. |
| `TankPrimitives.vb` | `LoadMeshes(path)`: groups sections by base name, uploads the vertex stream **byte for byte** and the index stream likewise, builds one VAO per pair with attribute formats that read the raw bytes (packed normal as 4 normalised bytes, packed tangent/binormal as `uint`, bone bytes as integer attributes), keeps the primitive-group records, attaches a `.uv2` sidecar on binding 1 when one ships. No vertex is decoded on the CPU. |
| `TankVisual.vb` | `.visual_processed` through `ResMgr.openXML` (already decodes packed BigWorld XML): render sets with their bone palettes, materials with the five texture paths and the detail constants, the node tree as name → summed `row3`. |
| `TankVehicle.vb` | The vehicle XML → parts. Picks the **priciest** chassis, turret and gun (the owner's rule: the most expensive is the best in the game). Chains the offsets the way the game does: chassis at the origin, hull at `hullPosition`, turret at hull + `turretPositions/turret`, gun at turret + `gunPosition`. `TankInstance` is (vehicle, position, heading, team, label). |
| `TankRenderer.vb` | `MapTanks`: the two hooks' target. Loads lazily on the first `Draw` after the map is up (so no load-order hook in the map loader), draws every instance into the G-buffer after the static models with the model shaders' convention, hands the attachments back. Flags: `Enabled`, `MirrorX`, `NormalMode`, `FlipSkinnedZ`. |
| `shaders\Tanks\tank_gbuffer.vert` | Unpacks the raw attributes: 8/8/8 normal, 11/10/10 tangent and binormal (the exporter's `unpackNormal`, bit for bit), builds a view-space TBN, falls back to a frame from the normal alone when a stream has no tangents. |
| `shaders\Tanks\tank_gbuffer.frag` | Writes `gColor` / `gNormal` / `gGMF` / `gPosition` / `gSurfaceNormals` exactly as `model.frag` does, with `GFLAG_MODEL`, so the deferred pass lights the tank like any model. Alpha test on the diffuse alpha. Milestone 1 only. |

## The two hooks into the core

* `MapScene.tanks` — `Public tanks As New MapTanks(Me)`, disposed with the scene.
* `modRender.draw_scene` — one call after `draw_models`, under the same
  `DONT_BLOCK_MODELS` switch, with its own GPU timer "Tanks".

Nothing else in the core changed. No SSBO, no cull bucket, no material slot,
no resolve branch.

## The test set

One `A88_M53_55`, chassis `Chassis_A88_M53_55_2`, turret `Turret_1_A88_M53_55`,
gun `_203mm_Howitzer_M47`, placed at the centre of base 1 on `19_monastery`:
`(-TEAM_1.X, terrain, TEAM_1.Z)` = `(-20.09, 6.30, -387.84)`, the way the base
ring places itself. Heading 0, team green, label "M53/M55".

## Draw state

After `draw_models`: `attach_CNGP`, depth test Greater (reversed Z), depth
**writes on** (the models had a pre-pass, we do not), culling **off** until
the handedness is settled by eye. Restores `attach_CNGPA`.

## Handedness, as found

* The whole tank takes the world's X mirror (`MirrorX`), like the map's
  placements.
* Skinned parts (gun, chassis) are stored with Z reversed relative to the
  hull and turret; `FlipSkinnedZ` negates Z for any mesh whose stream carries
  bone bytes. First seen as the gun drawn out over the spade.

## Not yet

Turret and gun articulation, bone skinning, track segments (never), the team
label card, the owner's tank shader (milestone 2), a UI panel, persistence of
instances.
