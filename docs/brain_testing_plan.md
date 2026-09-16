# Brain Testing - the plan

A second app in the repo root, beside `nuTerra/`, `PathStudio/` and
`Exporter_studio/` (renamed from `Slicer/` 2026-09-16):
the smallest thing that can open a WoT map and drive tanks on it, so the AI can
be worked on without the renderer in the way.

The owner, 2026-09-15:

> We are making a new app. It will be in the nuTerra root and it is called
> Brain Testing. Call the app that to. I want to take the basics of what we
> need to open a map like nuTerra. map loader screen is needed and all the same
> cmd line args. It all is simple. we need the buildings. No textures,, we need
> the terrain meshes, no textures. We don't need VT texturing. just view space
> clipping of opject. draw the terrain as meshes. we need no shadowing, not
> fire or smoke . just a loader of the basics we can test AI on. the tanks have
> no textures. I still want the ID cards over them but only team color and what
> point is is heading to and what path row.

And, 2026-09-15, narrowing it: **"the brain will be new. I need the world"**.

That is the load-bearing sentence. The existing AI - `Tanks/Sim/TankSim.vb`,
`TankDrive.vb`, `TankNav.vb`, `TankRoutes.vb`, `MapTankRays.vb`,
`Tanks/Sim/TankComms.vb` - **is the brain, and none of it is linked**. Brain
Testing supplies the WORLD and the BODIES; what drives them is written fresh
here. See "The seam" below - it is the most important section in this file.

*Written 2026-09-15 by nuTerra work, from a walk of the existing code. Nothing
below is built yet.*

---

## The one decision that shapes everything: LINKED SOURCE

Brain Testing must compile **nuTerra's own source files into its own
assembly**, via `<Compile Include="..\nuTerra\...\File.vb" />`. Not a project
reference, not a copy.

**Why not a project reference.** nuTerra's types are `Friend` - `Shader` is,
because `ShaderLoader` is a Module, and that is exactly why `MapTanks.DrawDepth`
had to be declared `Friend` rather than `Public` on 2026-09-14. `Friend` is
assembly-scoped, so a separate assembly cannot reach the loaders at all.
Widening the codebase to `Public` to suit a test app is a large edit to files
belonging to other lanes, for the newcomer's benefit. No.

**Why not a copy.** A fork of a 112 KB `MapLoader.vb` is a second thing to fix
every time the format understanding improves, and the second copy is always the
stale one.

**What linked source gives.** One copy of every loader on disk; a fix in
nuTerra reaches Brain Testing on its next build; `Friend` works because it IS
the same assembly at compile time; and the exclusion list becomes the project
file, which reads in one screen and is the honest statement of what this app is.

**The cost, and it is the main risk of the whole job.** VB compiles
all-or-nothing. Link `MapLoader.vb` and everything it names must resolve, which
will drag in things we mean to exclude. That is what stage 1 exists to
discover, and `BrainStubs.vb` is how it is answered.

---

## What is IN, and what already exists

Walked 2026-09-15. Line counts are today's.

| need | where it already lives | state |
|---|---|---|
| command line args | `nuTerra/Program.vb`, 389 lines, ~45 arguments | reuse nearly verbatim |
| map loader screen | `RenderEngine/MapMenuScreen.vb`, 291 lines - ImGui grid of installed spaces, sets `MAP_TO_LOAD` | reuse |
| terrain MESH | `MapLoader/TerrainBuilder.vb`, `ChunkFunctions.vb`; VAOs `all_chunks_vao`, `outland_vao`, `outland_far_vao` in `Scene/MapTerrain.vb` | reuse the build, replace the draw |
| buildings | `Scene/MapStaticModels.vb`, 1054 lines | reuse |
| view-space clipping | `MapStaticModels.frustum_cull()` - a GPU compute pass over `cull.comp`; plus `Modules/modFrustum.vb` | **already exactly what he asked for** |
| tank BODIES | `Tanks/TankFiles.vb`, `TankVehicle.vb`, `TankVisual.vb`, `TankPrimitives.vb`, `TankRenderer.vb` | reuse - geometry and draw only |
| ID cards | `Tanks/TankCards.vb`, 493 lines - bakes each card into a cell of one atlas, hangs it on a camera-facing quad, re-bakes only when the content changes | reuse the mechanism, change the content |

`TankCards` is a lucky find. Because a card is DRAWN into a texture cell rather
than expressed in a billboard shader, changing what it says is ordinary 2D
drawing into that cell. His three fields are a rewrite of one bake function,
not a new billboard.

## What is OUT

Virtual texturing (`Virtual Texturing/`, 944 lines, and
`TerrainTextureFunctions.vb`), all model and terrain textures, sun / lamp /
tank shadows, fire, smoke and particles, water, sky, decals, roads, trees,
minimap, the flight recorder, the flight bake.

Terrain and models draw with a **flat shader**: position, a normal for a cheap
directional term so the ground reads as shape rather than a silhouette, and one
colour. Tanks draw flat in team colour.

## `BrainStubs.vb` - how the exclusions are made to compile

Some excluded subsystems will be NAMED by code we keep. For each, the answer is
one of these, in order of preference:

1. **Leave it out and let the caller's own flag skip it** - where a call
   already sits behind a global (`FREEZE_FX`, `CLEAN_VIEW`), set the flag and
   the code never runs. Nothing to stub.
2. **A stub with the same signature that does nothing**, in `BrainStubs.vb`,
   each carrying the name of the file it stands in for.
3. **Link the real file anyway**, where the dependency is small and inert.

Every stub is a lie the app tells itself, so each one says which file it
replaces and why that was cheaper than linking the real thing.

---

## The stages

Each stage ends in something that RUNS and can be looked at. No stage is
"wire it all up and see".

### Stage 0 - the skeleton, no map

`BrainTesting/BrainTesting.vbproj` in the solution, `x64`, same target
framework as nuTerra. Its own `Program.vb` (args, copied and pruned) and a
window that opens and clears.

**Done when:** `BrainTesting.exe 19_monastery` opens a window titled
"Brain Testing" and exits cleanly.

### Stage 1 - the map menu, and the dependency closure

Link `MapMenuScreen.vb` and whatever it needs. This is where the closure
problem shows itself, and the stage is deliberately early so it is met on a
291-line file rather than after the loader is half wired.

**Done when:** the map grid appears, a click sets `MAP_TO_LOAD`, and the
exclusion list plus `BrainStubs.vb` are written down.

### Stage 2 - terrain as meshes

Link the terrain build path; draw `all_chunks_vao` and the two outland VAOs
with the flat shader. No VT, no terrain textures.

**Done when:** monastery's ground is on screen with its real shape, and a map
named on the command line skips the menu the way nuTerra does.

### Stage 3 - buildings, with the culling

Link `MapStaticModels.vb` and `cull.comp`. Flat shader.

**Done when:** the buildings stand in the right places, and the cull is
confirmed working by the drawn-instance count falling as the camera turns away.

### Stage 4 - tank BODIES, flat, and not driving

Link only the geometry half of `nuTerra/Tanks/`: `TankFiles`, `TankVehicle`,
`TankVisual`, `TankPrimitives`, `TankRenderer`. NOT `TankSim`, `TankDrive`,
`TankNav`, `TankRoutes`, `MapTankRays`, `TankComms` - those are the old brain
and it is being replaced. Not `TankFx`, `TankPuffs`, `TankBlast`, `TankTrail`,
`TankShots` either. Hulls draw flat in team colour.

**Done when:** `perteam=2` puts hulls on their bases, correctly posed and
facing, standing still because nothing is driving them yet.

### Stage 5 - the cards he asked for

Rewrite the cell bake to carry **team colour**, **the point it is heading to**,
and **the path row**. `TankSim` already holds both - `RunOf` / `RunAt` give the
run and the index within it, and `startTeam` gives the side.

**Done when:** he can read a hull's target and row off the screen without the
log.

### Stage 6 - the seam, with nothing behind it

Ship a NullBrain that holds every hull still, and prove the app runs a full
frame against it. That is the handover point: the world is up, the bodies are
posed, the cards are reading, and the brain is an empty file with a shape.

**Done when:** the owner can write a brain without touching anything else.

---

## The seam

Everything above exists to make this one interface easy to write against.

    ' What the world gives a brain, once a frame.
    Public Structure BrainInput
        Public dt As Single
        Public hull As BrainHull()        ' id, team, position, heading, speed
        Public ground As Func(Of Single, Single, Single)   ' terrain height at x,z
        Public blocked As Func(Of Vector2, Vector2, Boolean) ' is this segment clear
    End Structure

    ' What a brain gives back. Nothing else.
    Public Structure BrainOutput
        Public throttle As Single()       ' -1..1 per hull
        Public steer As Single()          ' -1..1 per hull
        Public target As Vector2()        ' what the CARD shows it heading to
        Public row As Integer()           ' what the CARD shows as the path row
    End Structure

**REVISED 2026-09-15 by Tank AI work, who will write against it.** Four
changes, and each one came with the reason:

- **`standable(x, z, radius)` - a POINT query, not only a segment one.** The
  algorithm the owner picked is a bounded Bug1: on hitting something, trace the
  obstacle boundary about 50 cells each way and score both ends by cells walked
  plus straight line to target. That probes a hundred places the tank is NOT,
  and needs a cheap "is this cell free for a hull of radius r". `blocked(a, b)`
  can emulate it with tiny segments, but that is the wrong primitive and would
  be the slow path.

  **TWO RADII, AND THE CALLER PICKS.** Tank AI work, 2026-09-16, and this is
  the part that "will silently not work if it ships with one":

  | radius | value | used for |
  |---|---|---|
  | TRACE | ~0.5 m, the grid cell | following the obstacle boundary |
  | FIT | `sqrt(hx^2 + hz^2)` + 0.3 m, per vehicle | once, to ask if the gap found is drivable |

  The trace radius is SMALL because walking a boundary means following the
  actual edge; query it with a hull-sized disc and every corner rounds off and
  the trace walks straight past the gaps it exists to find - which reads as
  "the algorithm does not work" when it is really "we asked the wrong question
  a hundred times".

  The fit radius is the half-DIAGONAL, not the half-width, because a hull
  rotates while it drives and a width-only test passes gaps a turning tank
  wedges in. Per vehicle, from the extents the world already hands over: a
  Panhard at 4.58 m and a Maus must not route identically, and one fixed
  `HULL_R` makes them.

  Step size for the trace is ONE CELL, because the owner's rule is "walk right
  50 cells and left 50 cells" - the cell is the unit, and 50 of them has to
  mean about 50 m or the bound means nothing.

  Budget is roughly 200 calls per blocked event, and a hull blocks only
  occasionally, so a few AABB tests each is affordable.

  **ONE NOTION OF BLOCKED, BOTH USES.** `standable()` must answer with exactly
  the rule the driving uses. If the trace walks an AABB boundary while the hull
  actually stops on a slope, on water, or on a crushable rule, then the brain
  traces an obstacle that is not where the tank stops and every score it
  computes is about a different map.
- **Hull half-extents belong ON `BrainHull`.** Ray origins sit on the hull
  edge, so every cast depends on the box - a Panhard and a Maus fan their rays
  differently. The world already knows it (`TankRoster.HullHalfExtents`), so
  handing it over stops each brain looking it up and getting it inconsistently.
- **No wall clock anywhere a brain can reach.** `dt` in, and nothing else - no
  `DateTime`, no unseeded RNG. Spawns are a pure function for exactly this
  reason and the brain has to hold the same line, or two runs still do not
  compare.
- **The PATH is not world data.** Which road a hull drives is a brain decision.
  The brain reads `<map>_paths.json` itself, from
  `C:
uTerra_shared	ank_paths` (moved out of `%TEMP%`, which the owner
  could not reach); nuTerra reads shared-first, flight-second, newest wins.

Three properties this has to keep:

- **The brain never touches OpenGL, the loaders, or a file.** It is given
  numbers and returns numbers, so it can be run without a window when that is
  wanted, and swapped mid-run.
- **`target` and `row` come FROM the brain**, not from the world guessing. The
  card shows what the brain believes, which is the entire point of the card -
  a card that displayed a value the world computed would agree with itself and
  prove nothing.
- **Bodies are driven by throttle and steer only.** No teleporting a hull to a
  waypoint. If a brain cannot drive there with the controls a tank has, that is
  a finding, not something to route around.

**The black box.** Tank AI work's `TankLog` writes a CSV per SIM run to
`C:
uTerra_shared	ank_logs`: eight ray distances, eight states, heading,
speed, travel since the last row, and the drive's own stated reason. Brain
Testing should write the SAME rows, because identical spawns plus identical
columns is what makes two different brains comparable at all.

The shape above is a proposal, not a decision - the owner writes the brain, so
the last word on what it is handed is his.

---

## Open questions for the owner

1. **Folder name.** `BrainTesting/` (no space) is proposed, so build commands
   need no quoting. The app TITLE is "Brain Testing" as instructed. Say if you
   want the folder spaced to match.
2. **"All the same cmd line args."** About fifteen of nuTerra's ~45 address
   subsystems this app will not have (`nolampfog`, `foggain=`, `gridfx`,
   `record`, `snapquit`). Proposed: parse them ALL so no existing script
   breaks, ignore the ones with nothing to act on, and say so once at startup
   rather than silently.
3. **The tank roster.** Tanks still load real geometry from the packages, which
   is the slow part of a tank load; flat-shading them does not make that
   faster. If the AI work wants instant restarts, a box-hull mode is the next
   thing to ask for - a different request, not assumed here.
