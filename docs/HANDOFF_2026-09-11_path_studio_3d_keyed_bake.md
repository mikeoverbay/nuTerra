# Handoff — Flight Studio: both directions, the 3D view, the 8K bake, the keyed bake

2026-09-10/11, on `master`. Written by Fable, the Flight Studio session
(`Flight Studio Work`). Follows `HANDOFF_2026-09-09_path_studio.md`, whose
section 8 this supersedes. A second session (`nuTerra work`) works the same
checkout on Tanks and owns `nuTerra/`; the two now talk by the desktop app's
session-to-session messages and keep to a file split (section 7).

Claims are marked. **VERIFIED** means measured or run. **REASONED** means
derived and not seen.

---

## 1. What is committed, in order

All by explicit path, `tools/path_studio.py` unless said:

| commit | what |
|---|---|
| `93fe9110` | both directions flown, scored, the loser faint, splits ringed (`+ export_cam_path.py`, `camera_flight_plan.md`) |
| `0e214c9b` `abda2f34` | the 3D view as a numpy ray march, then two-level (superseded) |
| `3d5d6c7a` `d5887ba5` | cubes, then greedy-meshed faces, PIL (kept as the `View3D` fallback) |
| `e1759b73` | **the 3D view on the GPU** (`GLView`): pygame + PyOpenGL, one VBO, one index buffer, one draw |
| `686d4797` `6830e830` | the 2048 grid (the bake itself) as default; grid clamped to the bake |
| `f8893dfb` `89852e9b` | ground blocks dark grey; frame and depth rendered at 4x the pixels |
| `61fcecb2` | **flight bake at 8192 + the models drawn again as lines** (`MapFlightBake.vb`, the one time I touched it), planners work at 2048 (`flight_plan.py`, `radar_commit.py`) |
| `f0a1156f` | `CLAUDE.md`: the file ownership split |
| `9764f763` | flight design doc: why the bake is 8192 and why lines |
| `a22944c4` | **the keyed bake, reading side**: rgba8 / r16 with a kind per texel, boxes and mask coloured by kind, legend |

Not pushed; the owner pushes after a pull. The agent shell has no key.

## 2. Both directions (`Direction: auto / forward / reverse`)

The loop is symmetric, the flight is not: side commitments, lane bends and
backups all depend on which end of a thing the navigator reaches first. On
auto, Generate flies the points in click order and in reverse, scores both
(closed, then clips, then thrash = reversals + detours + backups + boxed,
then furthest off the nominal, then length - `score_key`), keeps the better,
draws the loser faint and dashed, and rings every place the two run more
than 15 m apart (`divergence`). The control switches which is shown - and
which Save publishes - without a re-fly. Each direction's outputs live under
`flight/fwd/` and `flight/rev/`; `export_cam_path.main` takes a `diag_dir`
and returns its numbers instead of leaving them in the log.

**VERIFIED** on the monastery seed trimmed to six points: both close, thrash
0, 6 m and 5 m off course, one 16 m split, 17 s for the pair.

## 3. The 3D view - three renderers in a day, and why it is the GPU one

| renderer | full frame | drag | fate |
|---|---|---|---|
| numpy ray march over the height grid | 1.5 s → 0.5 s two-level | 0.45 / 0.16 s | dropped |
| one PIL box per cell, painter's order | 0.33 s | 0.05 s | dropped |
| greedy-meshed PIL faces (Lysenko, 0fps.net 2012) | 0.22 s | 0.03 s | kept as `View3D`, the fallback without pygame |
| **`GLView`: pygame GL 3.3 window, one VBO + one EBO** | **2 ms at 1024, 3.4 ms at 2048** | same | **current** |

`GLView`: a pygame window pumped from Tk's loop every 16 ms (one thread,
two windows). Every cell's top and every wall that stands more than
`WALL_MIN` 1 m over its neighbour, at 256 / 512 / 1024 / 2048 cells a side
(2048 = the bake, one cell per texel; clamped to the bake), objects above 2 m
flattened to boxes (95th percentile of their own top), in one vertex buffer
(position float32 x3 + colour uint8 x4, 16 B a vertex) and one index buffer,
one `glDrawElements`. Rendered into an offscreen buffer **2x the window each
way** (1920 x 1280 behind 960 x 640) and filtered down: edges smooth, and a
pick reads the full-resolution depth. `hit_at(px, py)` unprojects one depth
value through the inverse MVP; `read_buffers()` does every pixel. The route
at flight height, the other direction, the splits, the points and the lights
are lines and points in a second small buffer, depth-tested. Ground lifted
to dark grey (`GROUND_LIFT`), shade floor 0.55. Keys: **B** boxes, **G**
grid, **R** reset camera; drag orbits, wheel zooms, middle-drag pans.

**VERIFIED, RTX 2070, monastery:** 2048 grid = 8.7M triangles, 384 MB on
the GPU, built and uploaded in 1.6 s, 3.4 ms a frame at 4x pixels; a picked
centre pixel lands on the grid height to 0.00 m.

Not done: text labels in the GL view (target numbers, split distances);
`View3D` fallback does not colour by kind; `PathStudio/Program.vb`'s
preflight does not check pygame (it is optional - the view falls back).

## 4. The bake at 8192, and the line pass

The owner: "I am trying to get it to hit fences and it's missing all of
them." **VERIFIED by reasoning and then by the owner's eye:** a fence is a
vertical plane; from straight above it has no area, so the depth fill pass
never wrote a texel for one at any resolution. `MapFlightBake.draw_models`
now draws the models a second time in `PolygonMode.Line`; every edge
rasterises along its length at its own depth, so the top map carries a rail
at rail height, one texel wide. `SIZE` went 2048 → **8192** as asked (0.15 m
a texel, 268 MB per `.r32`, 256 MB depth texture); the mask PNG is written
block-any at `MASK_SIZE` 2048. The planners **do not** work at 8192:
`flight_plan.Bake` and `radar_commit.Bake` bring any bake down to `WORK_RES`
2048 on load, block MAX for the top (a one-texel fence survives), block MEAN
for the floor. Owner, after a rebuild and a reload: "that map is much
better. we have some fences and all the grape vine rails."

Cost to know about: the first load of each map now writes ~540 MB to
`%TEMP%\nuTerra\flight`; old 2048 bakes of other maps are stale until the
map is opened again.

## 5. The keyed bake (in flight)

The owner's idea: collision needs no 32-bit height - 16 is enough - so put
the height in G/A and use R as a **key** for what the thing is, by the folder
its model came from, 8 kinds at most, one colour each.

**The spec, agreed by message with `nuTerra work` (who writes it):**

- `<map>_top.rgba`: raw RGBA8, row-major, row 0 = north. R = kind key, G =
  height high byte, **B = height low byte**, A = 255 (the writer chose G/B
  over the spec's G/A; the file is the authority, the reader follows it);
  `height16 = round((y - height_offset) * height_scale)`.
- `<map>_floor.r16`: raw uint16, same encoding.
- meta adds `format=rgba8`, `height_scale=64`, `height_offset=<whole number
  under the map minimum>`, and `kind_<n>=<name>` per key emitted.
- Keys: 0 terrain, 1 building, 2 fence / rail / gate / wire, 3 tree / bush /
  vine, 4 rock / cliff, 5 vehicle / wreck / prop, 6 water, 7 other. Colours:
  amber, orange, green, grey-blue, purple, blue, white; 0 keeps the map
  colour. `BAKE_KIND_RGB` / `BAKE_KIND_NAMES` in `path_studio.py`.
- The cheap way to write it: give the top pass a colour attachment, have the
  depth shaders write the kind as colour from a per-draw key, and the depth
  test leaves the kind of the topmost thing at each texel.

**Reading side, done (`a22944c4`):** both `Bake` classes take either format
(`_load_layers`), carry the kind through the 2048 downsample as the kind of
the tallest texel in each block, and offer `kind_at(x, z)`. Flight Studio
colours the obstacle cells of the 2D mask and the 3D boxes by kind, names
from the meta, legend bottom-left of the canvas. **VERIFIED** on a synthetic
keyed bake re-encoded from the real 8192 monastery: heights back within
0.008 m, kinds carried, mask and boxes coloured.

**The writer landed** the same morning: `bc4e8f2c` (nuTerra work) - a
colour attachment on the top pass, `sun_depth_model.frag` writing the kind
from a per-draw key, `kind_of(path)` by folder substring, `read_kinds`,
`write_top_rgba` / `write_floor_r16`, the meta keys. First real keyed bake
of the monastery verified through the reader; colours on the mask and the
boxes.

## 6. Talking to the other session

`ListAgents` / `mcp__ccd_session_mgmt__list_sessions` names the sessions;
`send_message(session_id, text)` delivers as a user turn there. Used twice
today: the bake heads-up and build request, then the keyed-bake spec. A
reply arrives here the same way. The owner does not want file-access racing,
hence section 7.

## 7. File ownership (in `CLAUDE.md`, committed `f0a1156f`)

| session | owns |
|---|---|
| Flight Studio | `tools/`, `PathStudio/`, `nuTerra/cam_paths/`, `docs/*path_studio*`, `docs/camera_flight_plan.md`, `docs/bulb_placer.md`, `CLAUDE.md` |
| nuTerra | everything under `nuTerra/` except `cam_paths/`, the rest of `docs/` |

`MapFlightBake.vb` is theirs from here; my one edit is `61fcecb2`. Crossing
the line is done by message. Never edit a file that is modified and not
yours; commit by explicit path. Compiling from the agent shell does not work
on this machine (`dotnet build` cannot evaluate `nuTerraCPP.vcxproj`;
`MSBuild.exe` dies on a `System.Collections.Immutable` binding failure) -
the IDE build, or the other session, is the check.

## 8. Open, ranked

1. **The keyed-bake writer** (section 5) - theirs; then reload a map and
   check the colours and the meta names.
2. **`MapCamPath.vb` reader for the 72-byte light record** (09-09 handoff
   §4) - theirs now too; the light shapes do nothing in the scene until it
   lands.
3. Text labels in the GL view; kind colours in the `View3D` fallback;
   pygame in the launcher preflight (optional).
4. From 09-09: the 26 m hairpin deviation on the shipped route; lane course
   3 (tree cluster) never closes; the A* still routes around lanes.
5. Trees / leaf cards batching in nuTerra - the owner parked it ("not now").

## 9. The sun shadow as four tiles (2026-09-11) - BUILT AND VERIFIED

The owner: split the baked sun shadow into four areas in the sun space,
16k x 16k each, use them all with an on-screen check, as a SECOND shadow
shader so deferred.frag is not overloaded. Built on the nuTerra side of the
split with the other session named on the files first; that session compiled
and ran every step - this shell had not yet found the dotnet recipe
(`CLAUDE.md` has it now; the C++ DLL is reused as built). Commits `4aee4494`, `09eb3c06`,
`bd4e2d61`, `88bbe553`. The authoritative write-up is now
`docs/shadows.md` (theirs, `3386fa1e`); this is the session record.

- `MapSunShadow.TILED` (default True): the fitted box split 2 x 2 in
  light-space XY, each quadrant an ortho render into its own D16 texture of
  `tile_edge` a side (`TILE_SIZE` 16384), overlapping its neighbours by
  `TILE_PAD_TEXELS` 2. The same three draw passes as the single map, the
  same command array at offset 0 - **never compacted**, so `gl_DrawIDARB`
  keeps indexing the bake kinds buffer. Depth-only FBO.
- **VRAM:** four 16k tiles are 2 GiB, to the byte what one 32k map costs -
  the win is sharpness, not memory. All four resident on the owner's
  instruction. `tile_size_fitting` counts ALL the tiles (`depth_bytes(s) *
  n`) against `TILES_VRAM_BUDGET` 0.4 of total and `TILES_FREE_BUDGET` 0.6
  of free. Monastery: 5393 MiB used, 2614 free, with the tiles resident.
- **A small single map is still baked** in tiled mode - `FORWARD_MAP_SIZE`
  8192, 128 MiB, 0.24 m a texel - because the WATER shader samples
  `sun_shadow_map` itself in its own forward pass with the full-box matrix,
  and because everything that reads `ready` wants a map. Without it the water
  ran with nothing on its shadow unit and the driver logged undefined
  behaviour ~10,000 times a run (the other session counted it: 0 before,
  10,349 after, 0 again). GL validates EVERY declared sampler on every draw,
  taken branch or not. Binding tile0 there instead would have shadowed the
  water against a quadrant matrix - a silent wrong result.
- **The second shader:** `shaders/Final_render/sun_shadow_tiles.{vert,frag}`
  (with `#define USE_PERVIEW_UBO` before the include, or `invView` does not
  exist and the stage fails to compile - that was `bd4e2d61`), run by
  `modRender.render_sun_shadow_tiles` before the deferred pass; quadrant from
  `sp.xy >= 0.5`, local uv through the pad, the four taps, a screen-sized R8
  at binding 13. `deferred.frag` gets `has_sun_shadow == 3`: one
  `texelFetch` and the same `shape_penumbra`.
- **The on-screen check:** each tile world box against the view frustum per
  frame; `tile_mask` to the shader; a mask change is logged.

**VERIFIED (the other session, clean build, 19_monastery):** five bake pairs
in order - four tiles, then the forward map - each `419 of 425 commands,
6 outland skipped`; `baked 4 tiles of 16384x16384 16 (2048 MiB together) ...
0.059 m per texel`; `baked 8192x8192 16 (128 MiB) ... 0.237 m per texel`;
`tiles: 4 of 4 on screen (mask 15)`; GL errors none. The single map depth
diagnostic (mean 0.67 vs ~0.5) predates the tiles - the owner's 06:50
snapshot read the same - and is the single map box fit, unchased.

Lessons that cost a build each: VB is case blind (`tile_size` collided with
`TILE_SIZE`); a shader that includes `common.h` gets nothing from the
PerView block without the define.

**The one that cost a look (`b0f07ce5`):** the tile pass ended with
`Enable(DepthTest)` + `DepthMask(True)` - a state of its own, not the
caller's. The caller had turned both OFF for the deferred quad that follows,
with `DepthFunc(Less)` still set from the decals; the quad sits at window
depth 0.5 (z=0 in `Ortho_main`'s +-30000 box), so it failed on the far half
of the frame and wrote a flat 0.5 over the near half. Symptoms the owner
saw: bulb glow through walls (`lamp_bulb.frag` reads that depth for its own
occlusion test) and the lamp reflections gone from pooled water (those
pixels kept stale C2 content). A full-screen pass inserted mid-frame saves
the depth test and mask it finds and puts them back - never "restores" to a
state it assumed.

Open: no MSM path for the tiles; `DebugDraw` shows the forward map only; the
owner had not yet judged the 0.059 m step by eye at the time of writing.

## 10. The keyed bake, as it ended the day

The writer landed (`bc4e8f2c`), then grew: the key byte is `kind = R & 7`,
`trunk = R & 128` (a tree trunk at this texel, `046d1d06`), `outland = R &
16` (the scenery backdrop, a bit rather than a ninth key so a backdrop cliff
stays a rock, `65536467`), all named in the meta as `kind_mask`,
`trunk_bit`, `outland_bit`, `trunk_radius`. Both readers take those from
the meta (`203a2d43`, `bd4e2d61`, `7b0498ba`) and expose `kind`, `trunk`,
`outland`, carried through the 2048 downsample as kind-of-tallest / any /
any. The four needles are gone (`65536467`): tallest interior thing 59 m,
the outland exempt. Roses and the grapevine DO write a trunk (woody stems);
ivy and wild bushes do not. The camera planner keeps the canopy; the trunk
and outland bits are unused by it so far.

Measured on the monastery, full res 8192: outland 1,988,780 texels (2.96%),
trunk 61,455 (0.0916%), both 79. Through the 2048 block-any downsample:
outland 2.99% (one solid mass, barely moves) but trunk 0.30% - 3.3x, because
a trunk is a ~5 texel dot and block-any at 68 cm turns a 0.27 m radius into a
0.68 m cell. That is the safe direction for a planner. If trunk clearance is
ever tuned, tune it against the 8192 figure and let block-any BE the margin;
do not stack a second one on top and then wonder why a genuinely open gap
between two trees will not thread.

The kind gate (`81f065cb`): `KIND_MIN_H = {tree: 3.0}` - a tree under 3 m
is a bush and is flown over; the mask draws it as low grey.

## 11. Trees and bushes: what the data can and cannot say

The owner: "find a way to know if a tree is a tree or a bush, and why some
bushes show as tall as a tree next to it". Measured on the monastery bake
(8192, 0.171 m texels), both sessions, no changes at first:

- **The bake tells the truth.** Every SpeedTree is drawn at its real LOD0
  with the leaf atlas alpha-tested; nine of the map's sixteen trunk-less
  species are modelled at 3 m or more (`ivy_ground_up_v1` 6.83 m,
  `Bush_Wild_5m` 5.93 - taller than `Olive_01` at 5.74 with bark). 27% of
  trunk-less foliage at 3 m+ matches the asset library.
- **Nothing sorts them botanically.** The tree list is transform, species
  hash, seed, shadow flags. The trunk stamp is bark within 0.6 m of the
  axis - misfiles both ways as a species test (a barked 1.9 m rose, a
  bark-less 2.2 m cypress). Names are no better (`Olive_bush` 6.43 m).
- **Foliage bakes as dots.** 156,500 tree-kind blobs over 0.5 m, median
  footprint 0 m2 - a leaf sets a texel only where it covers the centre. A
  bush beside a tree interleaves with it.
- **The 2048 downsample lifts.** Block MAX: 194,705 cells read as a >=3 m
  tree, 24% of them from fewer than a quarter of their 16 texels; 14% have no
  trunk within 4 m at all. Then `CANOPY_H` 3.0 pads a 3.1 m bush as canopy.
  Picture at world (323, 94): a trunk-less clump at ~3 m beside two stamped
  trees.

The owner's call: isolate foliage by type, coloured, so a tank drives
THROUGH bushes and not through trees. The question is "does it stop a
hull", and on that exam a bark-less sapling is rightly drivable. The method
(the nuTerra session's): no species table - threshold each trunk stamp by
its own connected size. On the monastery: 4,840 components, bimodal - 2,005
single texels (41% of components, 3.3% of the stamped area) and the real
trunks at 7-8 texels. `stem_min_m` 0.25 drops exactly the singles, which are
unresolved rather than measured thin. The writer sets `solid_bit` 32 on
the survivors, `trunk_bit` 128 stays raw; both named in the meta with the
threshold.

My half, landed (this commit): readers carry `solid` (block-any through the
downsample) and `stem_min_m`; `foliage_state` per blob (TREE / STEM /
BUSH); `gated_obstacle` keeps a TREE blob at every height; the Studio
paints three states plus the stamp cells, and lists them. Without the key:
`solid = None`, the old colour and the old gate. Tested on a synthetic
keyed bake with a tree, a rose and a bush through both readers, the gate,
the mask and the 3D view. The writer's half is queued behind the tile
tint work; the halves land in either order.

**The bake against the placements (later the same day).** The nuTerra
session's `treedump` writes `<flight_bake>/19_monastery_trees.csv` - one row
per SpeedTree placement: species, x z y, declared_h (the .srt box, minY to
maxY), scale xyz, has_bark, above_pivot_h (maxY alone: every species has a
negative minY - roots or a base plate under the pivot - 0.03 m on a grapevine,
2.02 m on a tall linden). 79% of the 7,984 placements carry a scale, 0.70 to
1.30 - so `Olive_bush` (6.43 declared) stands 4.5-8.4 m and
`Linden_Regular_Small` (10.64) 7.5-13.8 m, and a big olive beside a small
linden reads the same height because it IS the same height. Joined to the
bake (`tree_join.py` in the session scratchpad: tallest tree-kind
top-minus-floor within 2.5 m of the base, against above_pivot_h * scale_y),
median ratio per species: lindens 0.95 / 0.96, stone pine 0.94, wild bush
0.94, poplar 0.94 - the bake reads ~94% of a placed crown, about what
clipping the wispiest texels costs. Under that: `Olive_bush` 0.83, tall
cypress 0.86 (n=574), `Unknown_Bush_Big_Bald` 0.80, `Olive_01` 0.69 - the
dense-crowned olives are cropped MORE than the airy lindens, so a plain
alpha-cut story does not fit (the bake tests `albedo.a < 0.5` fixed where
the beauty pass uses `0.5 / (1 + mip * 0.55)`; that mismatch is real and
unexplained by this). Nobody has yet looked at an olive in the viewer beside
its bake reading. No fixed per-species height table could have classified
any of this; the component-size threshold for the solid bit stands.

**The olive fault, found and fixed (16:08 bake).** With the `.srt` boxes
beside the bake footprints, the olive was not trimmed, it was MISSING:
`Olive_bush` (6.7 x 8.8 m box, 964 placed) came through as a 1.9 m core -
24% of its footprint, 9% of the texels within 2.5 m of the base - and
`Cypress_regular_small`, the most placed species on the map (1,386 saplings),
was at 8% / 1%. Lindens were at 87% coverage. Cause: `sun_depth_tree.frag`
alpha-tested at a fixed 0.5 while the beauty pass uses the mip-aware
`0.5 / (1 + mip * 0.55)`; the top-down bake samples every leaf card near
mip 7, where a sparse silvery atlas averages under 0.5 everywhere. Fix (the
nuTerra session): the beauty pass's cutoff in the bake pass, one line.
Verified per species with `verify_bake.py` (scratchpad; needs
`<map>_trees.csv` and `<map>_tree_boxes.csv` from nuTerra's `treedump`):

    species                height / footprint / cover   before  ->  after
    Olive_bush               0.81 / 0.24 /  9%   ->  0.99 / 0.92 / 91%
    Olive_01                 0.69 / 0.43 /  8%   ->  0.87 / 1.06 / 73%
    Cypress_regular_small    1.13 / 0.08 /  1%   ->  1.08 / 0.93 / 14%
    GrapeVine_01             0.78 / 0.75 /  8%   ->  0.80 / 0.97 / 12%
    Bush_Wild_5m             0.91 / 0.85 / 31%   ->  1.03 / 0.97 / 88%
    Linden_Regular_Small     0.96 / 1.11 / 87%   ->  0.97 / 1.26 / 100%
    Linden_Regular_Tall      0.95 / 1.32 / 88%   ->  0.97 / 1.45 / 99%

Whole map: tree texels 3.49% -> 6.75% (+94%), trunk stamps unchanged
(bark was never alpha-tested - the control), 4,805 stamps that sat on
terrain-keyed texels now sit under canopy. Leaf-card orientation was ruled
out on the geometry (olive and linden authored alike, no billboarding in
either pass). The navigator's gate still holds (bush open, tree blocked);
the A* grid went from 24.1% to 21.3% blocked with the gate on.

**Consequence for the shipped route:** `19_monastery.campath` (01:20, flown
on the old bake) now has 11 of 2,110 points under the 0.5 m margin in 6
stretches, worst 4.8 m short at (154, 113) under a 7.4 m top - foliage the
old bake could not see. It needs regenerating in the Studio on this bake
before it is flown again.

## 12. Model shading: the tank's material on the milk cans (evening)

The owner: "get the Gloss and Metal on the buildings to look like the tanks
rendering", reference two tin milk cans (`hd_env_EU_040_MilkCans`) on a wood
table by a stone wall, `cam=-4.2564,5.2212,-0.3624,70.5601,0,48.1163`.
The 09-08 pass had tried this reference and was reverted. Built this time as
**Tank material (models)** in the deferred PBR path (`TANK_MAT`, off by
default, bit-identical off), with the material block of `tank_gbuffer.frag`
ported and measured with the 09-08 still protocol. The owner: "rendering
is better" - committed as `48c8b096`. Measured negatives worth keeping:

- **The tanks do not go through the resolve.** `tank_gbuffer.frag` lights
  in linear space under three camera-following lights, ACES, gamma, and
  writes `GFLAG_UNLIT`; its `gGMF.rg` is a by-product. The tank LOOK is that
  rig, not its material model.
- **The Tank Exporter curves black out the cans.** The cans' body carries
  G 0.45 in the G-buffer; `pow(G/0.5, 5) * 1.5` reads that as 0.79 metal,
  the diffuse goes, and the tank's `NdotV x gloss` IBL weighting gives a
  rough metal nothing back: in the wall's shade the body fell 37 -> 11
  levels (-70%), the wood table -47%. Under three lights the tank rig hides
  this; one sun does not.
- **The tank's gloss-gated sun lobe removes the game's sheen.** Gating by
  raw gloss x 6 x curved gloss is x0.035 on these maps (R ~0.3); the game's
  GGX low-gloss floor is a broad sheen every sunlit model has. Kept the
  game's lobe.
- **The game's decode is the safe reading.** `pow(x, 2.2)` on the bytes:
  0.45 -> 0.17 metal, energy term `1 - min(m^2 * 3.2, 1)` keeps 91% of the
  body's diffuse and takes all of the rim's (G 0.83 -> 0.66). Off -> on at
  the camera: cans -3, wall 0, table -2, ground 0.
- **The environment is the aluminium lever.** World-space R (x flipped),
  mip by roughness over 4 levels, split-sum LUT, weighted NdotV x gloss for
  a dielectric (the tank's - no grazing flare) and full for a metal (the
  game's specAmbient), cube decoded as the PMREM it is (4x the sRGB read):
  wall +3, cans +2 at gain 1. Exposed as `Env specular` 0..4.

Controls: `tank_mat`, `gmm_curve` (0 raw / 1 Tank Exporter / 2 game),
`tank_env`, `env_pmrem` - all persisted per map.

**Zoning (evening, the owner's idea):** "draw rings and find areas as large
as we can that the ring fits without hitting something; that whole area is
safe; no collision checks, only zone radius checks." The Tank AI session
builds the machinery (distance transform of the free mask, maximal discs
greedily largest-first, overlap graph, A* on the graph) and Flight Studio
READS its zone map, the way it reads the bake - the owner: "wait for path
AI to kick out the zone map". The contract asked for: one file per MASK
beside the bake (tank and camera masks are deliberately separate - a camera
flies over what a tank cannot drive through), per disc id / centre x z /
radius / ground y, explicit adjacency, and a header with the mask, its
rule, the cell size, the bake's `written` time and the count. The camera's
free mask is `radar_commit.build_world(...)`'s `raw` with the gate applied.
A radius field amplifies mask holes (one wrongly free cell inflates a disc
through a wall and the planner PREFERS the wide corridor), which is why
the canopy-over-rock hole is being fixed in the writer first.

The Tank AI session's extraction rules, which its file is held to and
which `radar_commit` should match if it ever cuts its own: **half a cell
back** off every radius (`distance_transform_edt` measures to the blocked
cell's CENTRE; the cell is solid from half a cell nearer - `sqrt(dt) - 0.5`
before scaling); **cover fraction 0.6** on the greedy widest-first claim;
**links walked, not overlapped** (a straight line between centres with full
clearance the whole way - overlap alone lies in a corridor). The first rule
applies to `build_world`'s `dist_m` today: the navigator's standoff reads a
field that is optimistic by 0.34 m at 2048. Not changed - the owner's routes
are tuned against it - and to be taken out together with a re-measure of the
shipped route, never slipped in. Their monastery numbers, for shape only
(tank mask, 4.5 m hull, 1024): 10,334 zones, 64,429 links, widest 57.6 m,
mean 8.1 m, 103 isolated, 722 ms.

The reader is in: `tools/zones.py` (`Zones`, `load_zones`, `zones_path`),
`bake.meta` on both Bake classes, the **Zones** checkbox in the Studio
(discs and walked links over the mask, rings on the ground in 3D, islands
filled). Tested on a synthetic bake with a synthetic zone file in the exact
spelling: the three checks, the provenance warning, a broken file (dense
ids, radius order, one-sided links, header counts), the canvas and the 3D
view. The real file appears in the flight folder once the writer is on
master and the owner builds.

**The real file, read (18:30):** the Tank AI session's monastery zone map
through this reader with no warnings but the provenance one (cut 67 min from
the shared meta's mtime - true, it came from their clone): 10,334 discs,
64,429 links, 103 islands (1.0%), widest 57.6 m, mean 8.1 m, smallest 4.79 m
against a 4.5 m hull. Zone 0 at (-112.8, -204.4) is the open field south of
the walls - the biggest open space on that side - and against the bake only
0.2% of it stands over 1 m: 14 fence texels and 31 trunk-less tree texels,
both exempt under the tank rule; zones 1, 2, 5 hold trunk-less foliage
only. The nearest thing over 1 m to zone 0's centre is 13.3 m away and is
foliage a tank drives through - the CAMERA's field would cut the same disc
at 13 m. That one number is the whole case for two masks off one height map.
CAVEAT: that map was cut from a mask master does not have - their branch
exempts fence and prop kinds from the tank's height test (the owner: "a
fence or curb is not going to stop a tank"), 2,028 cells freed on the
monastery; on master those 14 fence texels block and zone 0 is smaller.
Their six files (zone map, CSV export, route catalogue, nav frame accessors,
two gun fixes) wait on the owner's commit.

**Flying by the zone map - measured, and not yet a win.** The owner: "we
can move that way if the next move point is in a zone ring." Built as a
fast path in `bearing_ok` (`radar_commit.zone_step_ok`): a bearing whose
next 2 m step lands inside a disc, with `BODY_R` kept from the rim and the
step itself still probed on the camera's own mask, skips the 9 m near
probe, the 22 m trap probe and the bend search. `zones.Zones.clearance`
(KD-tree over the centres, ball query to the widest radius) does the disc
test in ~65 us. On the shipped monastery plan with the TANK's zone map:

    zones off   2283 pts  4571 m  13 s   backups 42   reverses 0   trap: object 27, terrain 67416, bend 217
    zones on    2449 pts  4903 m  37 s   backups 182  reverses 186 boxed 31   zone accepts 247,427
    (unguarded  2983 pts  5964 m  27 s   backups 1006 reverses 237 - the fast path walked the camera into cells its own radar refused)

Two reasons, in order of weight. The disc says the ground AROUND the step
is free; it says nothing about whether that ground leads anywhere, and the
far probe it replaces is exactly the test that keeps the camera out of
pockets - inside a courtyard-sized disc the camera is happily accepted
straight into the courtyard. And this is the tank's map: fences and
trunk-less foliage are exempt in it, so its discs cover ground the camera
must fly round. **`Fly by zones` therefore defaults OFF** and the hook
stays. What would make the zone map help the camera is its GRAPH, not its
discs: route disc-to-disc toward the next target over the walked links (A*
on a few thousand nodes, sub-millisecond), then fly the chain with the
radar - "the next move point is in a zone ring" where the ring is the NEXT
disc on the route, not any disc. That needs a zone map cut from the
camera's mask (`build_world`'s blocked mask, exported for the same
extractor) and it is the next piece, not built.

**The graph, looked at (19:40).** Connected components of the walked links
(`zone_graph_pic.py` in the session scratchpad draws it over the mask, one
colour per component): 251 - one sheet of 6,696 zones (64.8%) covering the
arena's interior with every ring road, lane and gap connected (the Tank AI
session's worry that a straight-line link test splits bending corridors
does not show); 977 and 748 on the west shore (x -487..-334), 223 and 174 on
the east (x 409..487), all INSIDE the arena and genuinely cut off - the
closest pair across is 87.5 m of ravine, water on the line and a 5.8 m rock
wall; 89 inside the monastery walls, a real courtyard; the other 243 are
pockets between the rock bands and the islands, 1-5 zones each. A first
reading of the strips as "outside the arena square" was wrong and is
withdrawn: the straight edge in the picture was the sheet's own edge at the
rock band, and the base-ring test (nearest disc 1.6 m from the team-1 base)
shows the frames agree with no mirror. Their offmap rule is right.

**Verdict (19:50).** The owner: "if the discs do not aid AI or path
creation, we can toss them." On the camera side they do not, so the
navigator hook (`set_zones` / `zone_step_ok`, the `Fly by zones` switch) is
REMOVED; `bearing_ok` is exactly what it was before the experiment. Then the
overlay and its checkbox went too, at the owner's word. The tank side then measured the same way (their numbers: the radius
test 9.3x faster than CanStand but rejecting 44% of drivable ground, 1.35x
once made correct; zone A* 1.5 ms against grid 9.2 ms behind a 770 ms
graph build, for six searches a load) and the writer is being removed, so
`tools/zones.py` went with it - a reader for a format nothing emits. The
contract and the checks are recorded above if a zone map ever returns; the
pictures in this section came from the session scratchpad's
`zone_graph_pic.py`. Whether the zone graph earns its
place for the TANKS - faster or better drives than the grid A*, with a
number - is that session's to show the owner.

## 13. The height map watcher (evening)

The owner: "keep Path Studio open but put a file watcher on our height  [Path Studio is now Flight Studio]
map." Every two seconds (`Studio.WATCH_MS`) the Studio stamps the loaded
map's bake files (`_meta.txt`, `_top.rgba`/`_floor.r16` or the `.r32` pair)
and compares them with what it loaded; a change that then holds still for
one more poll - nuTerra takes seconds over the 256 MB top layer - calls
`reload_bake()`: the bake is re-read, the mask re-rendered, the 3D surface
rebuilt with the camera where it was, the radar world follows on the next
generate. The route, targets and lights are untouched. Never while a
generate is running or the Studio is busy. The status line says "height map
changing on disk..." while the writer is at it and "height map reloaded at
HH:MM:SS (written=..., commit=...)" after, with the provenance keys when
the writer carries them. Tested on a synthetic bake rewritten under a
running Studio with a new building: reloaded within two polls, the target
and the 3D camera kept.

## 14. Point-to-point routing, measured for the Tank AI session (night)

The owner, to both sessions: "look at how Path Studio searches paths; it is  [Path Studio is now Flight Studio]
point to point; we have 2; the algo should still apply." `radar_tangent.py`
(direct / acceptance rings / fan tangents / bounded A*) measured tonight on
the monastery: on the shipped 1,068-waypoint plan the deciding layer is
direct 93%, ring 7%, search 0.3%, tangent NEVER - the A* course already
avoids everything, so the walker only fine-tunes. Base to base as two
points (-20,-388 -> +20,+388), camera mask: search decides 92-100% of
moves, the walker takes 163-364 s and never arrives (438 m and 357 m short)
- the bounded A* re-runs every step and the taut aim returns 0 m, so it
crawls a cell at a time. Verdict handed over: do not port the layers. The
tank side's own grid A* (9 ms) plus a string-pull (Theta* / Lazy Theta*: the
parent pointer skips to the furthest ancestor still in line of sight) is
the planner; the owner's 3 m ray is the DRIVER - two jobs. Their ring walk
and my fan silhouette are both TangentBug (Kamon, Rimon, Rivlin, IJRR 1998),
which exists for a robot that cannot see the map; we can. The two "rings"
are different objects: mine an acceptance tolerance round the target
(asin(R/d) cone), theirs an obstacle-rounding circle at the hit point - the
owner should be shown both before either side builds further on his word.
`flight_plan.astar(cost, start, goal, stats)` is self-contained (60 lines,
2-D cost array, inf blocked, 8-neighbour, admissible) and importable.

The tank side built it the same night (`tank_tools/ray_studio.py`, their
lane): on a hull-grown 1.37 m grid, A* + string-pull 829 / 826 m in 0.3 s;
Lazy Theta* 813 / 815 m but MORE waypoints (19 against 7 - it leaves
collinear runs down a straight corridor, taut but not minimal); Lazy Theta*
+ string-pull 812 / 814 m, 13 / 11 points - taken for their route
catalogue. Twelve routes asked for, eight found both ways: eight is the
map, a proof, not a budget. 0 of 344,557 samples in solid. Against their
ray/ring walk: 939 m, 45-84 s, not repeatable. The line put to the owner,
jointly: plan with the search, drive with the rays - his 3 m ray is the
right instinct at the driving layer. The ring resolver stays drawn in
their viewer until he says which ring he meant.

A lesson from their side worth keeping for ours: their ray resolver
validated a centre line through single 17 cm texels and every "0 samples
in solid" check tested the same line, so a 4.5 m hull overlapped solid on
12% of a drive (distance transform along the route: min 0.17 m against a
2.25 m radius) and nothing caught it - the check and the flaw shared an
assumption. The camera side does not have that hole: `Radar.march` walks
`plan`, the blocked set dilated by `BODY_R` (0.5 m) and `TERRAIN_R` (3 m)
in `build_world`, so the body is in the mask before any ray is cast. The
general rule: a validation must sample something the planner did not plan
on - the eroded mask is the planner's most important input, more than the
search algorithm.

Not done, and measured above for whoever does it: the block-max lift and the
canopy threshold. A tree-cell rule that needs a SHARE of the block tall,
and a `CANOPY_H` above the bush band or tied to the solid bit, are the
levers - measure the open ground they give back before trusting either.
