# Handoff 2026-09-06 (evening) - terrain mixer, building dirt, model AO, Light Bulb Placer

For the next agent. Written at `ff055fe5` on `master`. **Read `docs/bulb_placer.md`
before touching the placer**, and `docs/README.md` for the rest of the docs.
Everything below is working and built; what is left is tweaks the owner will ask
for by looking at it.

## How to work here (non-negotiable, learned the hard way)

- **Work in `C:\nuTerra` on `master`.** No worktrees, no side branches. The IDE
  builds `nuTerra\bin\Debug\net8.0-windows`; commit there so the owner's F5 has
  your change. The owner pushes; you never push.
- **Build the `.sln`, never the `.vbproj`, never `-p:Platform=x64`:**
  `"C:/Program Files/Microsoft Visual Studio/18/Community/MSBuild/Current/Bin/MSBuild.exe" C:/nuTerra/nuTerra.sln -v:q -nologo`
  Kill `nuTerra.exe` first or the copy step fails (MSB3027).
- **After every built change, relaunch nuTerra on `19_monastery`** so the owner
  can look: `nuTerra.exe 19_monastery` (add `placer=1` to open the Bulb Placer
  once the map is up; add `cam=x,y,z,yaw,pitch,roll` from his snapshot to start at
  a view). Capture stdout to a file to read the log - `LogThis` goes to the
  console, not to a file, unless redirected.
- **Do not touch:** `%TEMP%\nuTerra\MapSettings\19_monastery.txt` (his live
  look; the still runner `tools/still_from_snapshot.py` REWRITES it - do not run
  it unless he asks), `%TEMP%\nuTerra\snapshot.txt` (never `snap`/`snapquit`),
  `nuTerra\cam_paths\19_monastery.campath` and the `VM_FOG_Curve_*.png` beside
  it (his uncommitted work), `docs/fog-cube-01.jpg`.
- **Do not drive the mouse or take screenshots while he is at the desk** - a
  synthetic click landed in his browser once. Read the stdout log instead; the
  placer logs every model load and every save.
- **Edits:** `tools/vbsplice.py` (exactly-once anchors; refuses on 0 or 2+),
  Python driver scripts written with the Write tool. VB doc comments (`'''`) end
  a Python triple-quoted string - put VB payloads in `.txt` files. Bash heredocs
  with apostrophes fail. From VB, `ImGui.Selectable` is ambiguous with more than
  one argument - single-argument only; mark selection in the text.
- **No multi-agent workflows.** Two of them burned the owner's usage window.

## What landed today (this session, newest first)

| commit | what |
|---|---|
| `ff055fe5` | placer view is an invisible button - drags no longer move the panel |
| `986f04cb` | placer crosshair/sphere/cone depth tested |
| `32f901ee` | Frame button gone; a model with no lights shows one default light |
| `b45369e4` | placer mouse: LMB orbit in 3D / drag marker in a plane view, MMB zoom, RMB move or pan, `3D` button returns |
| `4de2b2b9` | placer loader fix: fresh render sets need a `primitiveGroups` dictionary |
| `a8c91957` | bulbs become lights: one per instance, nearest 32 lit, cone / inverse cone masks in `deferred.frag` and `lamp_fog.frag` |
| `ce01078c` | the Light Bulb Placer itself (`BulbPlacer.vb`) |
| `564c6055` | campath bulb table (`cam_path.py`, `MapCamPath.vb`) |
| `327b56e1` | model AO through the resolve + the game's fake self-shadow; sliders under Surfaces (defaults 1/1 are a guess) |
| `7a20a287` | buildings: the game's dirt curve on all three tiled entries, GCM on PBS_tiled |
| `3bac45b3` | Height Contrast slider floor 0.01, log scale |
| `c28cf7af` | terrain mixer: the game's macro combine - the smeared rock fix |

Docs written: `bulb_placer.md`, `game_PBS_tiled.md`, `terrain_blend.md` section
"Macro, normal and global map", `HANDOFF_2026-09-06_fog.md` sections 6-8.

## The Light Bulb Placer - state

Works end to end: list of the map's fire/lamp models (7 on monastery), the
model loads from the pkg on click (standalone loader, verts + normals), one 3D
view with crosshair, right pane with types and sliders, **Save to campath**
writes the bulb table, `ExpandBulbs` places a light on every instance, cone and
inverse cone shade correctly in surfaces and shafts. The owner has been clicking
through models; nothing saved yet as of this handoff (the campath log line says
`0 bulb(s)`).

**Tweaks he is likely to ask for next** (guesses, ask him):
- Feel of the drags: speeds are constants in `draw_view` (`0.008` orbit,
  `0.01` middle zoom, `0.15` wheel). Marker moves in pixels-to-metres at the
  target distance (`move_marker`).
- The default light on a model with none is unsaved and appears at the top of
  the box; he may want it not to exist until "Add light".
- Crosshair size scales with range (`cl = max(0.15, r*0.08)`); the range
  sphere at 20 m dwarfs a 3 m lamp - maybe draw it only when selected or at
  reduced alpha.
- Snap views: Top uses up=+Z, Front looks along +Z from -Z, Side looks along -X
  from +X. He may want them mirrored to match his mental map.
- Shadow cubes are baked for **map lights only**; bulb lights are unshadowed.
  A map with 145 street lamps cannot carry 145 cubes; the honest fix is cubes
  for the nearest N with a rebake on set change (see `bulb_placer.md`).
- Only the 32 nearest bulb lights are lit (`visible_lights`). Fine for a
  camera on the ground; a high overview shows lamps switching.
- Sanity check the X sign of `ExpandBulbs`: instance matrices are the ones the
  renderer uses, so it should be right, but nobody has yet saved a bulb and
  looked at a lit lamp. First save on monastery will tell.

## Other open items

- **Model AO defaults.** `MODEL_AO_POWER` / `MODEL_AO_SUN` default 1 / 1. The
  game's `bakedAOPower` / `bakedAOToShadowsMult` were not found in the shader
  headers; they may live in the space or environment settings. If buildings read
  dark, 2-3 / 0.5 is likely closer.
- **Fake self-shadow push direction** in `model.frag fake_shadow_push` follows
  the game's sign; unverified visually against the game.
- **`GL Error InvalidValue` every frame** once the VT page atlas fills
  (`Atlas is Full, using LRU`). Predates today; not chased.
- **The "nuTerra is the old version" report** from earlier in the day was never
  explained; the tree, exe and frames all checked out current.
- Terrain audit leftovers, Python terrain bake model boxes, DotNetZip already
  removed - see `open_threads.md`.

## Where the day's reference material is

Game shader disassemblies used today are in the session scratchpad only (not
committed): `terrain2_5_virtual_texture` blob 13, `terrain2_5` blob 05,
`PBS_tiled_atlas` and `PBS_tiled` `.10` blob 01. The route to regenerate them is
in `terrain_blend.md`, "Reading the game's shaders": fxo is a zip, `effect` holds
DXBC blobs, `fxc /dumpbin` from the Windows SDK disassembles them. The `.11`
builds of the model shaders hold only forward last-LOD variants; use the `.10`
files for the deferred pixel shaders.
