# Handoff — Tank AI: the navigation grid and the driver

2026-09-11, on `master`. Written by Opus, who did the work. Scope is
`nuTerra/Tanks/` plus the parts of `nuTerra/Scene/MapFlightBake.vb` the AI
reads. Two other sessions are live on this checkout — see §9.

Claims are marked. **VERIFIED** means measured or run on this machine.
**REASONED** means derived and not seen.

---

## 1. Where it is

Three commits carry the AI itself:

- `e5af39a5` — **the navigation grid**, `nuTerra/Tanks/TankNav.vb`.
- `0ec96c5c` — **the driver**, `nuTerra/Tanks/TankDrive.vb`.
- `1838ff5f` — the measurements: the fleet drawn on its own map, and a
  reason recorded for every stopped tank.

Everything the AI stands on was built the same day and is in `master`:
`046d1d06` (tree trunks separated from canopy in the bake), `65536467` (the
outland marked, height needles removed), `12b830ab` (the height maps 16-bit),
`d197dd2e` (the foliage cutout fix — **this one changed the bake under the
grid, see §8**).

Nothing is pushed. The agent shell has no SSH key; the owner pushes.

## 2. What a tank does now

Per tank, every frame, in `TankDrive.Advance`:

1. If it has no goal, has arrived (within `ARRIVE_M` 6 m) or has been on one
   goal longer than `GOAL_PATIENCE_S` 45 s — pick a new one. `PickGoal`
   throws up to 24 darts into a **ring** 60–220 m out and takes the first
   that `TankNav.CanStand` accepts. A ring, not a disc: a uniform disc puts
   most candidates near the tank and it shuffles instead of travelling.
2. If it is backing out (`reverseS` > 0), reverse in a straight line and
   return.
3. Turn toward the goal at `TURN_RATE_RAD` 1.0 rad/s.
4. Drive only when within `DRIVE_CONE_RAD` 0.5 rad of the goal bearing.
   **Turn first, then drive** — rolling while still swinging arcs the hull
   into whatever it was turning away from, and the arc is widest exactly
   when the turn is largest, which is right after being blocked.
5. Probe the step with `CanStand`; then check other hulls ahead.
6. On success, move and ground the hull with `get_Y_at_XZ_fast`.

`TANK_AI` (default **off**) chooses between this and the old shuttle. The
shuttle is kept deliberately: it slides every hull along its own heading off
one shared distance, which is useless as behaviour and exactly right for
checking that a track band scrolls at the rate the hull moves. There is a
**Drive (AI)** checkbox in the TANKS! panel.

## 3. The navigation grid (`TankNav.vb`)

`SIZE` 1024 square, one byte a cell, `CELL_TEXELS` 8 — a power-of-two
downsample of the 8192 flight bake, so world→cell is the bake's own mapping
shifted and there is no second rounding to disagree about. On monastery a
cell is **1.37 m** and the grid builds in **~340 ms**. VERIFIED.

Flags: `BLOCKED` 1, `STEEP` 2, `OUTLAND` 4, `TRUNK` 8, `WATER` 16,
`PINNED` 32, `OFFMAP` 64. `IMPASSABLE` is all of them — no caller assembles
its own idea of impassable.

Three things about it are not obvious and all three were mistakes first:

**The canopy is not an obstacle.** A texel whose only height is a tree is
skipped, and the trunk bit answers instead. Without this every wood on the
map is solid and the tanks stay on the roads. This is the whole reason
`046d1d06` separated trunk from canopy in the bake.

**The boundary is the ARENA BOX, not the outland bit.** `OUTLAND` marks
where an outland *model* rasterised, and those are sparse scenery — only
10.67% of monastery's outer ring carries one, so three sides had open ground
running to the edge of the bake with nothing marked, and a tank steered by
that bit alone drives off the map between the cliffs. The real edge is
`MAP_BB_BL`/`MAP_BB_UR` out of `scripts/arena_defs`: −500..500 on monastery
inside a 1400 m bake. With it in, outland *inside* the arena counts **zero**
— which independently confirms the backdrop is entirely outside the play
area. VERIFIED.

**Slope is a RATIO, not a drop.** The placement test allowed 2 m across a
9 m footprint, about 12°. Carrying that number onto a 1.37 m cell permits
55° and the tanks path up cliff faces. `TankNavLimits.MAX_SLOPE` is 0.7,
about 35°, and it is a ratio because a height only means a slope while the
cell size stays put — and the cell size follows the map's span.

`CanStand` tests a **disc**, not the square that encloses it. See §5.

Monastery, inside the arena: **518,400 cells, 69.3% open**, 17.1% blocked,
10.5% steep, 9.9% water, 0.9% trunk. VERIFIED — but see §8.

## 4. Learned pins, and why they are hard to get right

`Pin(x, z)` marks a cell the bake did not predict. Pins persist to
`%TEMP%\nuTerra\tanks\<map>_pins.u32` as a plain list of cell indices —
indices, not the whole grid, because the baked flags come back for free next
load and storing a derived thing invites the two to disagree.

**Pinning on first contact made the fleet worse, run over run.** VERIFIED:
299 pins in one session, 264 more in the next, with fewer tanks moving each
time. Most were not map errors — they were one tank steering into a corner
it could not turn out of, which says nothing about whether the ground is
passable. A pin now needs **`PIN_CONFIRM` 3 independent wedges at the same
cell**, and there is a `PIN_BUDGET` of 2000. With that, pins converge and
stop: 576 on monastery, static, 0.11% of the arena by area.

**Saving is on the report tick, not in `Dispose`.** `Dispose` is not reached
when the process is killed, which is how this one usually ends — so nothing
had ever actually persisted despite the code being there. `Save()` no-ops
unless something was learned.

## 5. Four deadlocks, all the same shape

Every one was something that looked like progress being counted as failure,
or the reverse. Recorded because the next behaviour added will make the same
class of mistake.

1. **`CanStand` tested the enclosing square.** A 4.5 m hull demanded clear
   ground 7.75 m away diagonally. The first fleet drove until it reached
   ground whose corners were not clear and then no direction passed:
   **0 of 30 moving**. It tests a disc now.
2. **Being blocked repicked the goal every frame**, so the desired heading
   was fresh noise each frame and the turn never completed. A tank now
   commits for `REPICK_S` 1.2 s.
3. **Turning counted as being stuck**, and a 180° turn takes longer than the
   stuck timeout — so a tank repicked a third of the way round, which
   changed the target and restarted the turn. Only a tank that is *aligned*
   and still not moving accumulates `stuckS`.
4. **Yielding tested a full circle**, so a tank was held up by one behind it
   that it was already driving away from. In a fifteen-strong spawn cluster
   that is most of them and the group locks itself in place. `Crowded` now
   ignores anything behind the beam.

## 6. How to tell whether it is working

Do not judge this from a screenshot. A tank that is stuck and a tank that is
waiting look identical in one frame.

**The fleet report**, every 5 s of `ANIM_DELTA`:

```
tank ai: 18/30 moving, 3 stuck, 0 without a goal, mean 3.1 m/s, furthest 6522 m, 590 pin(s)
tank ai:   turning 6, aligned-but-stopped 0, ground 4, traffic 0, reversing 0
```

The second line is the one to read. **Turning is progress** — a hull
swinging toward a new goal is doing the right thing — so 24–27 of 30 were
getting on with it, not 18. `aligned-but-stopped` is the category that is
always a bug; it should stay at 0. `ground` is the number pathfinding will
move: 3–7 tanks are against the grid at any moment, and each recovery costs
a turn, which is why `turning` runs higher than open-country driving would
explain.

**The fleet map**: `navdump` writes `<map>_nav.png` at build and
`<map>_fleet.png` every 5 s to `%TEMP%\nuTerra\tanks\`. One pixel a cell,
coloured by *why* a cell is closed; tanks drawn as dots (hollow = stopped)
with a heading leader and a line to their goal. This is what answers "are
they spread and travelling or clumped and shuffling", and it is what the
owner should be shown rather than asked about.

**Determinism**: every random choice comes from `New Random(&H7A2B0000 Xor
inst.id)` and every step is taken against `ANIM_DELTA`, not frame time. Two
runs of the same build put the same tank in the same place on the same
frame. Do not introduce `Date.Now`, `Rnd()` or `DELTA_TIME` into this path —
it is the only reason a still of thirty moving vehicles can be compared with
another still.

## 7. What is next, in the order I would do it

1. **The radar fan.** Per-tank line-of-sight at ~5 Hz so the thirty stop
   being wanderers and start being two teams. The ray march already exists
   in `TankShots.vb` and tests the same `top_m` — a LOS test is that march
   without the shot. REASONED: a naive sweep is 30 × 15 rays at full step
   and far too much (~13 M steps/s); stagger it round-robin so a few tanks
   sweep each frame, use a coarse step, and cap the range.
2. **Pathfinding.** Slots in at `PickGoal` and nothing else has to change.
   That is the piece that moves the `ground` count in §6.
3. **Target selection and firing.** The gun already has its own reload from
   the def (`a3e3225f`), a magazine model and a shot pool; it currently
   fires on a free-running cadence at a swept aim. Point it at what the
   radar sees.

## 8. Open, and one thing that needs re-measuring

**The foliage cutout fix changed the ground under the grid.** `d197dd2e`
landed after the AI work and put **94% more tree texels** into the bake
(2,339,008 → 4,530,949, 3.49% → 6.75% of the map). Trunk texels are
unchanged at 61,455 — bark was always exempt from that test — so
`TankNav`'s trunk-derived blocking should be unaffected, REASONED. But the
grid has not been rebuilt and measured since. **Re-run with `navdump` and
compare the open/blocked/steep/water split against §3 before trusting the
AI's view of the map.**

**The solid bit** is designed, measured and agreed with the Path Studio
session but **not written**. It would label the connected components of the
trunk mask and set bit `0x20` on those spanning `stem_min_m` 0.25 m or more,
so a rose stem and a grapevine stop being obstacles while a real trunk does
not. Contract: `solid_bit=32` and `stem_min_m` in the meta, bit `0x80`
left as the raw stamp, readers never applying the threshold themselves.
Path Studio's readers already handle it and treat an absent key as "no solid
information", falling back to the height gate. The component distribution
was bimodal on the *old* bake — 41% single-texel stems against a mode at
7–8 texels — and that measurement should be **redone on the new bake**
before the threshold is fixed.

Not done, and wanted: tank-vs-tank collision beyond the `SEPARATION_M` 8 m
stand-off; suspension or any terrain-following beyond sampling ground height
at the hull centre; anything at all about damage, despite `hullHp`/`crewHp`
existing and being swept by a demo.

## 9. Who else is in this repo

Three sessions share `C:\nuTerra` with the owner. **`git status -sb` and
`git log --oneline -5` before acting; never `git add .`; never stage, stash
or clean a file that is not yours.**

- **Path Studio** owns `tools/`, `PathStudio/`, `nuTerra/cam_paths/`,
  `docs/*path_studio*`, `docs/camera_flight_plan.md`, `docs/bulb_placer.md`
  and `CLAUDE.md`. They consume the flight bake and have readers for every
  bit in the key byte. **Tell them before changing what the bake writes** —
  the contract is in the meta and they read it from there.
- **The Tanks session** owns everything under `nuTerra/` except
  `cam_paths/`, plus the rest of `docs/`.
- Name files before editing anything that is not yours, and wait. It has
  worked all day; both sessions have caught real bugs in the other's code
  by reading it rather than assuming.

Two conventions that have already cost time:

**VB is case blind.** `Dim blocked` shadows the constant `BLOCKED` and every
test silently becomes `f And 0`; `TILE_SIZE` and `tile_size` are one
identifier and will not compile. The count locals in `TankNav.report` are
all `n_`-prefixed for exactly this reason. Do not un-prefix them.

**Build with the OutDir set**, or the binary lands where nothing launches:

```
dotnet build nuTerra.vbproj -c Debug -p:Platform=x64 \
  -p:BuildProjectReferences=false \
  -p:OutputPath=C:\nuTerra\nuTerra\bin\Debug\net8.0-windows\
```

with `VCTargetsPath` set as an environment variable. `nuTerraCPP.vcxproj`
cannot build on this machine; the DLL is reused as built, which is why
`BuildProjectReferences=false` is not optional. Kill every running nuTerra
before launching another. Rebuild and run to check for errors every time —
shaders validate at runtime only — and hand visual judgement to the owner
rather than asking him to measure something you could have measured.
