# Handoff — Path Studio: both directions, the 3D view, the 8K bake, the keyed bake

2026-09-10/11, on `master`. Written by Fable, the Path Studio session
(`Path Studio Work`). Follows `HANDOFF_2026-09-09_path_studio.md`, whose
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
the tallest texel in each block, and offer `kind_at(x, z)`. Path Studio
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
| Path Studio | `tools/`, `PathStudio/`, `nuTerra/cam_paths/`, `docs/*path_studio*`, `docs/camera_flight_plan.md`, `docs/bulb_placer.md`, `CLAUDE.md` |
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

## 9. The sun shadow as four tiles (2026-09-11, NOT YET BUILT)

The owner: split the baked sun shadow into four areas in the sun's
projection space, 16k x 16k each, use them all with an on-screen check, as a
SECOND shadow shader so deferred.frag is not overloaded. Built on the nuTerra
side of the split with the other session's agreement (files named to it
first); the agent shell cannot compile, so **the IDE build is the check**.

- `MapSunShadow.TILED` (default True): the fitted box split 2 x 2 in
  light-space XY, each quadrant an ortho render into its own D16 texture of
  `tile_size` a side (`TILE_SIZE` 16384), overlapping its neighbours by
  `TILE_PAD_TEXELS` 2 so the filter taps at a seam are inside. The same three
  draw passes as the single map, the same command array at offset 0 - no
  compaction, so `gl_DrawIDARB` keeps indexing the bake kinds buffer. The
  single map is not baked in tiled mode.
- **VRAM:** four 16k tiles are 2 GiB, to the byte what one 32k map costs.
  All four resident on the owner's instruction (8 GiB card).
  `tile_size_fitting` counts ALL the tiles against `TILES_VRAM_BUDGET` 0.4 of
  total and `TILES_FREE_BUDGET` 0.6 of free, stepping every tile down a
  power of two together - a map that reached 7842 of 8192 MiB with the
  single map gets smaller tiles, not an OOM.
- **The second shader:** `shaders/Final_render/sun_shadow_tiles.{vert,frag}`,
  run by `modRender.render_sun_shadow_tiles` before the deferred pass:
  gPosition -> world -> the FULL box with `sunViewProj` -> quadrant from
  `sp.xy >= 0.5`, local uv through the pad, the same four taps as the single
  map, written to a screen-sized R8 (`sun_shadow_pre`, bound at 13).
  `deferred.frag` gets `has_sun_shadow == 3`: one `texelFetch` and the same
  `shape_penumbra` as the other paths - nothing else in it changed.
- **The on-screen check:** each tile's world box (its light-space quadrant
  across the depth range, back through the light view) is tested against
  the view frustum every frame (`BoxInFrustum`); the mask goes to the shader
  as `tile_mask` and pixels on a tile that is off screen are lit without a
  tap. A change of mask is logged: "sun shadow tiles: N of 4 on screen".

What to look for after the build, on the monastery: the log line
"sun shadow: baked 4 tiles of 16384x16384 16 (2048 MiB together) ..." at
load, then "sun shadow tiles: N of 4 on screen" as the camera moves, and the
shadows themselves - the same as before at a glance, twice as sharp up
close, no seam along the middle of the map in either axis.

Open: the tiles have no MSM path (`MSM_SHADOW_ENABLED` is ignored while
`TILED`); `DebugDraw` shows nothing in tiled mode; `docs/shadows.md` is the
nuTerra session's and has not been told yet (the paragraph above is what it
needs).
