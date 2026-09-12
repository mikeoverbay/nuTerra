# Handoff — Tank AI: the navigation grid and the driver

> **Status 2026-09-12: current.** This DELIBERATELY OVERWRITES the "behind by 37
> commits" banner added on master as `10c8db4f` by the Shader IDE session, which
> was correct when written and stops being true the moment these sections land.
> §1, §3, §7, §8 and §9 are rewritten and §12 is new; §10 and §11 are Path
> Studio's and untouched. The work described still lives on branch `tank-ai`
> in `C:\nuTerra_tankai` and reaches this checkout only by a merge the owner
> performs, so if you are reading this ON master the merge has happened and
> `nuTerra/Tanks/TankRoutes.vb`, `TankRayPath.vb` and `tank_tools/` are here.

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

**AND THE REST OF IT IS NOT HERE.** Since 2026-09-11 the Tank AI lane has
worked in an ISOLATED CLONE at the owner's instruction — "merges have
destroyed code when working with friends before":

    C:\nuTerra_tankai     branch `tank-ai`, remote `shared` -> C:\nuTerra

As of 2026-09-12 that branch is **37 commits ahead** of `shared/master`:
9 files, +4,151 lines, none of it merged. Two files exist ONLY there and
you will not find them in `C:\nuTerra\nuTerra\Tanks\`:

- `nuTerra/Tanks/TankRoutes.vb` — 965 lines, the route catalogue.
- `nuTerra/Tanks/TankRayPath.vb` — 538 lines, the VB ray resolver.
- `tank_tools/` — a new top-level folder, Python: `ray_studio.py` (1,819
  lines, the live resolver viewer) and `ROUTING_FINDINGS.md`.

So this document describes the lane as it stood on 2026-09-11 and the work
has moved a long way since. §12 is the current state. Anyone reading only
the shared checkout is two days behind and will not know it.

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

**The canopy is not an obstacle — and neither is a fence, or a vase.** On
`tank-ai`, `KIND_FENCE` and `KIND_PROP` join `KIND_TREE` as exempt from the
height test. The owner's rule: "A fence or curb is not going to stop a tank.
small trees are crushed. Its mandatory we are not limited on travel by a
grape vine," and "we hit a vase, we smash it!". `master` still exempts only
`KIND_TREE`, so this paragraph is TRUE OF THE BRANCH AND NOT OF MASTER.
A texel whose only height is one of those three is
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

**`clear_m` is built AFTER `Load()`, and the order is not cosmetic.** The
clearance field lives in `TankNav` on `tank-ai`. Building it before the
learned pins were loaded gave the planner a map without them, so it routed
through cells the driver had already discovered were impassable — and every
run added pins for ground the planner would keep choosing, so the error
compounded instead of converging. `Pin()` now also reduces `clear_m` in a
40-cell radius.

Monastery, inside the arena: **518,400 cells, 69.3% open**, 17.1% blocked,
10.5% steep, 9.9% water, 0.9% trunk. Measured on the PRE-`d197dd2e` bake
and **STILL NOT RE-VERIFIED** as of 2026-09-12 — see §8. The bake-level
consequence of that commit HAS now been measured; this grid-level split has
not. Do not quote these six numbers as current.

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
2. ~~**Pathfinding.** Slots in at `PickGoal`.~~ **DONE, and not there.**
   Superseded on 2026-09-12. It is not a `PickGoal` tweak - it is a route
   CATALOGUE built once and held in memory, which is the owner's own design:
   "Rule one. seek base. Rule 2 seek using path finders algo. Rule 3. Tag
   that path as used and try path. When we cant find a way there, we are
   done. Save every path. That needs to be permanently in mem at startup
   unless we want to watch tanks drive for 200 years." See §12.
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
AI's view of the map.** STILL OPEN as of 2026-09-12 - the VB grid has not
been rebuilt.

**But the bake-level half IS measured now, and it found a real cost.**
2026-09-12, on 19_monastery: **2,581 cells hold something solid more than
1 m above the floor - median 1.70 m, max 22.08 m, mostly rock - and key as
`KIND_TREE`.** Under the crushable rule (§3) a tank drives straight at them.
That is the price of `d197dd2e` and it is the last remaining way a route
from §12 puts a hull into a cliff. VERIFIED.

Two candidate fixes were put to the owner and he has not chosen:
one extra `read_heights` between `draw_models` and `draw_trees` in
`MapFlightBake.vb`, or exempting `stonestairs` / `StoneSteps` /
`Gravestones` by name. Under the 2026-09-12 job split the classifier is the
nuTerra Work session's, so the call is theirs to put to him - the Tank AI
lane measured it and cannot fix it.

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

Four sessions now share `C:\nuTerra` with the owner. **`git status -sb` and
`git log --oneline -5` before acting; never `git add .`; never stage, stash
or clean a file that is not yours.**

**REPLACED 2026-09-12.** The owner split the sessions BY JOB and the three
of us then agreed a directory map, because a rule you can test by looking at
a path is the only kind that survives a busy day.

| job | owns |
|---|---|
| **nuTerra Work** - engine, main UI, and producing the HEIGHT MAP and COLOUR TYPE | everything under `nuTerra/` except `Tanks/` and `cam_paths/` |
| **Tank AI** - develop the AI path creation, and work with Path Studio to get it rendered | `nuTerra/Tanks/*`, `tank_tools/*` |
| **Path Studio** - code Path Studio, work with Tank AI on the rendering | `tools/`, `PathStudio/`, `nuTerra/cam_paths/`, their docs, `CLAUDE.md` |

`nuTerra/Modules/modGlobalVars.vb` is SHARED: name the change and tell the
others first. That has already caught a duplicate declaration.

Still unsettled at time of writing: `nuTerra/Scene/MapTankRays.vb` and
`RouteFilm.vb`. They draw route CONTENT, which is the Tank AI lane, but they
live in nuTerra's tree. Tank AI and Path Studio both argued ONE AUTHOR PER
FILE rather than splitting GL maintenance from content decisions inside one
file - that split is exactly what the directory rule exists to prevent.

**Two standing rules from the owner, which bind all sessions:**

1. "If I ask something that isn't in your job list, ask me first." He wants
   the boundary raised rather than quietly crossed, including when his own
   asks land outside it.
2. "Talk to the sessions and decide on what areas each will handle. auto
   hand of the work if I ask the wrong session and let me know." The agreed
   protocol: an ask outside your lane with an obvious owner gets FORWARDED
   to them and the owner told you forwarded it - not bounced back to him;
   no obvious owner means ask him rather than guess; and say in the forward
   what you already know that bears on it.

**The height map and the colour type are nuTerra Work's DATA PRODUCT** - the
bake, the kind classifier, the palette. Ask them for a claim about what a
kind means rather than building your own read of it. The line agreed between
the sessions: reading the bake as data, and geometry derived from it
(clearance, corridor width, route length), belongs to the reader; what a
kind MEANS, and the writer, belong to nuTerra Work.

- Name files before editing anything that is not yours, and wait. Both
  sessions have caught real bugs in the other's code by reading it rather
  than assuming.

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

## 10. Foliage, for whoever writes the navigator

Added after the first draft, from the Path Studio session, who measured most
of it. Attribution matters here because §8 tells you to re-measure two
numbers and these are not among them — these hold.

**The top map is a SINGLE LAYER, and that is the first thing to internalise.**
A bush standing under a tree's canopy reads at the *canopy's* height, because
there is only one surface per texel and the canopy won the depth test. Path
Studio's measurement on monastery: 136 olives with a linden within 7 m read
**9.8 m**; the 828 standing alone read **4.8 m**. VERIFIED (theirs). The same
bush is two different heights depending on what is above it, so **height is
not a property of the plant** and a navigator that treats it as one will be
wrong about a fifth of the foliage on this map.

**"Does it stop a hull" comes from the trunk bits, never from the height.**
That is what `046d1d06` separated them for. `TankNav` already does this — see
§3, the canopy is skipped and the trunk bit answers — and anything new should
too.

**The trunk stamp separates ivy and wild bush from trees. It does NOT
separate bushes in general.** `Olive_bush` has bark and stamps exactly like a
tree; so do the roses and the grapevine. Of monastery's 36 species, 20 carry
bark at LOD0 and 16 do not, and the 16 are the eight ivy variants, the wild
and unknown bushes, and the two smallest cypresses — which are *trees*.
VERIFIED. For DRIVING that is the right answer anyway: a trunk-less 2.2 m
sapling is something a tank flattens. It is the wrong answer for botany, and
the solid bit in §8 exists because a rose stem is not a hull-stopper either.

**No per-species height table classifies anything**, so do not write one.
79% of placements carry a scale between **0.70 and 1.30**. VERIFIED. That is
why a big `Olive_bush` (declared 6.43 m, effective to 8.36 m) genuinely does
stand taller than a small `Linden_Regular_Small` at 0.707× (7.52 m) — which
was the owner's original complaint and was not a bug. It is also why the
solid bit thresholds a measured component size rather than a species.

**The gate in `tools/` is the CAMERA's, not a tank's.** `KIND_MIN_H` /
`TREE_MIN_H` 3.0 means "fly over foliage under 3 m". A tank's rule is a
different question — what its hull hits — and should not reuse those
constants. Path Studio offers `radar_commit.foliage_state(bake)` and
`bake.trunk` / `bake.solid` as the inputs for a drivable mask and will keep
those signatures stable; ask them rather than reimplementing the read.

**Tooling that already exists**, both off by default:

- `treedump` — writes `<map>_trees.csv` beside the flight bake, one row per
  SpeedTree placement: species, x z y, `declared_h`, scale xyz, `has_bark`,
  `above_pivot_h`. Use `above_pivot_h`, not `declared_h`: every species' box
  has a negative minY (roots or a base plate below the pivot, 0.03 m on a
  grapevine to 2.02 m on a tall linden), so comparing a bake height against
  `declared_h` under-reads by whatever is buried.
- `<map>_tree_boxes.csv` — the `.srt` box per species, written by a
  scratchpad script rather than the app; ask if you want it committed.
- `treetrace` — per-species draw-call verdicts at load: LOD, part kind,
  vertex and index counts, mean `|ny|` and the share of near-vertical
  vertices, and the texture named. The facing columns are what ruled out
  "the foliage is edge-on to a top-down pass" as an explanation.
- Path Studio has `verify_bake.py`, which joins those CSVs to a bake and
  prints per-species height and footprint ratios. Ask them for a committed
  copy in `tools/` if you are going to iterate on the bake.

## 11. Monastery's corridor is 16 m wide, not 4.8 m

Added 2026-09-11, answering a question the Tank AI session asked and could not
answer from inside: its catalogue reported two routes between monastery's bases,
both "narrowest 4.8 m", and its smallest disc is 4.79 m against a 4.5 m hull. So
4.8 could mean a genuine pinch or could mean the floor of the disc set, and
nothing on that side separates them.

Measured from the bake instead, with no disc involved, at 0.171 m a texel rather
than the nav grid's 1.37 m cell. Same drivable rule as `TankNav`, so the numbers
are comparable: OUTLAND, TRUNK, WATER, non-tree obstacle over `MAX_OBSTACLE`
1.0 m, STEEP on the 8×8 cell at `MAX_SLOPE` 0.7, OFFMAP outside the arena box
less `ARENA_MARGIN`. Clearance is then the Euclidean distance from each free
texel to the nearest impassable one, and a hull of width w fits along a path iff
2 × clearance ≥ w every step of it, so twice the bottleneck IS the width the
catalogue's number is trying to be.

**The widest corridor between the two 50 m base discs is 16.06 m at its
tightest.** Bisected on the threshold, connected at 8.032 m clearance and
disconnected at 8.060, which brackets it to three centimetres. The pinch is at
world **(-88.3, 26.1)** — a central constriction, not near either base.

So 4.8 m was the disc floor. Nothing between these bases is forced through
anything like it, and a wider vehicle does not start losing routes until it is
over 16 m wide, which no tank is. `HULL_R` can be tuned without that number
being the constraint anybody thinks it is.

**The shortest route is a separate question and gives a different number.** A
route that minimises distance cuts corners and gives width away doing it: 739 m
ring to ring (centre to centre adds up to ~100 m, so it brackets the
catalogue's 862 m), and its true minimum clearance is 2.91 m — **5.81 m wide**,
at world (-81.3, -24.9), 51 m from the widest corridor's pinch and the same
constriction. Both numbers are real and they answer different questions: 16.06 m
is what the map allows, 5.81 m is what haste costs.

**Two checks that the mask is the same one `TankNav` builds.** Off-map came out
at 50.6% of the map against the grid's `offmap 530176` of 1048576, which is the
same 50.6%. Free space came out 36.3% against the grid's 34.4% open — higher,
by about the margin finer resolution predicts, since at 0.171 m a thin obstacle
no longer poisons a 1.37 m cell around itself.

**One thing to fix in any goal test.** Neither base centre is usable ground.
`team1` has 3.78 m clearance and `team2` 2.39 m — 4.78 m of width for a 4.5 m
hull, 14 cm a side. Both texels are plain terrain, so it is nearby obstacles
rather than anything built on the mark. Aim at the disc, not the mark.

**A method limitation, stated because it produced a wrong-looking result.** The
route geometry above was searched on a 1024² grid coarsened by block MINIMUM, so
a route it finds is genuinely traversable. That grid finds no route at all at 8 m
clearance, which contradicts the full-resolution answer — and the full-resolution
answer is the right one. Coarsening conservatively erodes the narrow links
between wide areas, and those links are exactly what a bottleneck search depends
on. 16% of texels clear 8.03 m, so this is not a thin-ridge artefact; it is the
connections being cut, not the rooms. Use full resolution for any width claim
and the coarse grid only for route shape.

## 12. The route planner, 2026-09-12

Everything in this section is on branch `tank-ai` in `C:/nuTerra_tankai`
and NOT in the shared checkout. See §1.

### What was asked for, and what it became

The owner's design, in his words: "Rule one. seek base. Rule 2 seek using
path finders algo. Rule 3. Tag that path as used and try path. When we cant
find a way there, we are done. Save every path." Then a method for rule 2:
cast a short ray; if it is clear, move; if it hits, draw a ring at the hit
point, expand it in 0.5 m steps until a tangent clears the obstacle, anchor
there and carry on; sweep the opening bearing from left round to east.

That is TangentBug (Kamon, Rimon, Rivlin, IJRR 17(9), 1998) - Path Studio
identified it, and the same paper covers their fan silhouette and our
expanding ring, which are two different objects that had both been called
"rings" in conversation. Both exist because a ROBOT CANNOT SEE THE MAP.
We can, and that turns out to decide everything below.

### Two resolvers, measured against each other

`tank_tools/ray_studio.py` runs both on the same map and draws both.
19_monastery, base to base, hull 4.5 m:

| | rays (ring resolver) | search (Lazy Theta* + string-pull) |
|---|---|---|
| distinct routes 1->2 / 2->1 | 2 / 2 | 8 / 8, and it EXHAUSTS |
| shortest | 985 m | 812 m / 814 m |
| winning chains | 100 of 121 one way, 3 the other | n/a, deterministic |
| time | 30-50 s a sweep | 0.6 s a route |
| a failure means | it gave up | proof there is no route |

Asked the catalogue for 12 routes and got 8 BOTH ways, so eight is the map
and not a cap. That is what makes rule 3's "when we cant find a way there,
we are done" an actual proof.

### Why the ray walker loses here, measured in three places

Not blocked terrain in any of them - CLUTTER:

- Team 1's base: **all 121 fan rays blocked** at a 90 m look. There is no
  silhouette to round because the tank stands IN the clutter.
- West, near (-115, -313): one free region crosses the window north-south,
  67.9% of it takes the hull, widest free radius 58.7 m. The rays stall in
  open ground; search routes 0 and 1 pass within 75 m.
- The band near (-32, 101): 53% takes the hull, but **41 separate hull-wide
  free regions** in a 180 m box. 31 of 31 sampled chains die there; search
  route 0 passes 33 m away.

And the clearest single number: the search crosses this map in 812 m one way
and 814 m the other - a symmetric problem - while the rays win 100 chains one
way and 3 the other. A gap that large on a symmetric problem means the route
count is a property of the WALK, not of the map.

A correction worth keeping because it was stated confidently and was wrong:
"a 3 m fan can never see past a 40 m building, so look far." Backwards. At
tank scale you cannot see 90 m in ANY direction. Clear-bearing rate along a
known-good route: 6 m 100%, 12 m 100%, 25 m 100%, 50 m 96.9%, 90 m 89.2%.

### The joint recommendation

Agreed between the Tank AI and Path Studio sessions: **the search plans the
route, the short rays drive it.** Path Studio's own layer hit rates say the
same from the air: on their shipped monastery plan, direct 93%, ring 7%,
search 0.3%, tangent **0 of 313 moves**; treated as two far-apart points,
search 92-100% and their walker never arrived. The ray family is the DRIVER
in both apps and the search is the PLANNER in both. The owner's short ray
was the right instinct at the wrong layer.

**Not yet decided by the owner** as of this writing. Both resolvers remain
live and both are drawn.

### The hull-width mistake, which generalises

The ray resolver validated a **centre line through single 17 cm texels**.
The only hull-aware test anywhere was a gap check on tangent candidates.
Distance transform to nearest solid, sampled every 0.5 m along a finished
route, hull radius 2.25 m:

    RAY route     min 0.17 m, median 7.77 m, 12% of the drive inside the hull
    SEARCH route  min 3.52 m, median 11.86 m, 0%

An eighth of a finished ray route had a 4.5 m tank overlapping solid
geometry. **And every "0 samples in solid" check reported on those routes
sampled the centre line too** - the check and the flaw shared an assumption,
so the routes measured clean while being undriveable.

Fixed by distance-transforming the fine collision map once at build and
eroding by the hull radius (6.4 s on 8192 square; 16.7% blocked becomes
26.7%). The RAW map is kept alongside the grown one **so the verification can
ask a question the planner never asked** - that is the actual lesson. It was
a net gain, not a cost: winning chains went 80 of 121 to 100, because erosion
also deletes the narrow false passages that were luring chains into traps.

Path Studio checked their side and are clean: `radar_commit` dilates the
blocked set by `BODY_R` and the ground-reach set by `TERRAIN_R` in
`build_world` before any ray is cast. **If any validator in nuTerra core
derives from the same mask as the thing it validates, it has this hole.**

### Also on the branch, and undocumented before now

`nuTerra/Modules/modGlobalVars.vb`: **`TANK_FIRING` defaults to `True` on
`master` and `False` on `tank-ai`** (line 1251 vs 1262). Deliberate - thirty
tanks firing while route work is being watched is noise - but it is a SHARED
file and the divergence will surface at the merge.

Reference: `tank_tools/ROUTING_FINDINGS.md` carries the full comparison and
the two traps already paid for (string-pull against the GROWN grid, never the
fine centre line; and the heuristic must match the cost or A* degenerates to
Dijkstra - that one cost 466,229 expansions on this map once).
