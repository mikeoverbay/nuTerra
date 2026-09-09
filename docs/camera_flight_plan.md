# Camera flight — design

Status: **design agreed, step 1 built.** Written 2026-09-01 from a working
session with the owner. Nothing below is speculation about what might be nice;
it is the design as settled, so it can be picked up cold.

The goal: fly the camera around a map on a planned route that loops back to
its start, without flying into the ground, buildings or trees, and without the
avoidance feeling like a maze-follower.

## Step 1 — ground clamp (DONE)

`MapCamera.vb`, in `set_prespective_view` right after `CAM_POSITION` is built:

```vb
Const EYE_CLEARANCE As Single = 2.5F        ' a tall person
Dim ground = get_Y_at_XZ_fast(CAM_POSITION.X, CAM_POSITION.Z) + EYE_CLEARANCE
If CAM_POSITION.Y < ground Then CAM_POSITION.Y = ground
```

Hard wired, not a slider: it is a physical constant of standing on the map,
not a look to be tuned. The camera already sampled terrain at the PIVOT
(`CURSOR_Y`) and simply never did it for the eye.

The TARGET is deliberately not lifted. Raising only the eye tilts the view as
it rides up the terrain, which reads as the camera following the ground; moving
the pivot would swing the whole framing and feel like the map moved.

## Step 2 — three baked maps

One top-down orthographic pass into an FBO with **three colour attachments**,
baked once at map load and read back to CPU arrays. After the bake the flight
loop touches **no game data at all** - no `get_Y_at_XZ`, no model bounds, no
GPU readback. It is integer indexing into three small arrays.

| # | map | contents | blend |
|---|---|---|---|
| 1 | **top height** | highest surface at that texel, terrain AND objects | `Max` |
| 2 | **floor height** | bare terrain alone, with no objects on it | `Min` |
| 3 | **mask / kind** | obstacle vs bare terrain | — |

Map 2 was originally specified as the *underside of overhangs* - a ceiling you
could fly beneath, so `floor < y < ceiling` would find archways and gates. **That
idea is dead, and it is worth knowing why before anyone re-invents it.**

The bake for it is easy: models only, no terrain, front faces culled so only
undersides rasterise, keep the lowest. The reasoning was that a solid building
has its bottom slab at ground level, so the ceiling would land on the floor,
headroom would be zero, and the building would block with no special case.

**WoT building models have no bottom faces.** They are hollow shells that sit on
the terrain, and the underside is never seen, so the art does not have one. The
lowest back face in a building's column is therefore the INSIDE OF ITS ROOF. The
ceiling map would report the entire interior as flyable headroom and route the
camera straight through solid buildings - not failing safe, but confidently
wrong, which is worse.

There is no cheap fix. Depth peeling would find the real surfaces but a
height-field cannot store them, and a hollow shell has no interior to peel. If
flying through arches ever matters, it needs a different representation
entirely, not a third texture.

Different blend equations per attachment in one pass is `glBlendEquationi`.
There is precedent in this codebase: `MapDecals.draw_decals` uses exactly that
to max-blend the wetness channel while the rest blends normally.

Store height as **R32F in metres** - no encoding, no max-height constant to get
wrong, directly readable when debugging. Pack to RGB8 only if the map wants to
be viewed as an image.

**Resolution: `(map size / waypoint size) * 4`** - four texels per waypoint
span. For a 1 km map with 40 m waypoints that is 100x100, so all three together
are ~120 KB. Four decision points between waypoints is what lets a turn develop
over several steps instead of snapping.

Build a debug view of these before trusting them. Do not write flight logic on
top of an unverified bake.

## Step 3 — look-ahead

March an integer (Bresenham) line from the current position out to a maximum
distance. At each texel:

- **top height > my altitude - clearance** → blocked, must turn
- otherwise → fly over it

That single rule covers everything. A hill and a church are the same question,
and a 1.5 m fence stops mattering the moment you are above it, which is what
makes the flight dynamic rather than a corridor-follower.

The floor map is what allows passing *under* a bridge or arch: blocked by the
top map, but permitted when altitude sits between floor and top.

## Step 4 — route rules

- A planned path of waypoints that loops back to the start.
- Hold the set heading; deviate ONLY when the look-ahead says we would hit
  something.
- Track progress so we know we have actually **passed** each waypoint, rather
  than drifting by or circling it.
- Draw the flight plan on the minimap.

## Step 4a — NURBS path and heading

The waypoints are **control points, not the route**. Fit a NURBS curve through
them and fly the curve, so the path is smooth rather than a polyline with a
corner at every waypoint.

**Curve the heading too**, not just the position. Two ways, and they are not
equivalent:

- **Heading from the curve tangent** - the camera always looks where it is
  going. Free, always consistent with the motion, and gives natural banking
  into a turn. Right for a flythrough.
- **A second curve for heading** - lets the camera look at something while
  flying past it (hold the church in frame through a turn). More expressive,
  and needed if the route is ever meant to show a subject rather than just
  travel.

Start with the tangent; add the second curve only when a shot wants it.

Two things fall out of using a curve that make the rest simpler:

- **Waypoint progress becomes a parameter test.** "Have we passed waypoint n"
  stops being a distance-and-direction check against a moving point and becomes
  "has the curve parameter t crossed that knot" - monotonic, unambiguous, and
  it cannot be fooled by circling.
- **Avoidance becomes a deviation from a known nominal.** The curve is where we
  intend to be; a collision turn is a temporary offset from it, and rejoining
  is just steering the offset back to zero. Without a nominal path there is
  nothing to rejoin, and the camera wanders after every avoidance.

Sample the curve at a fixed arc length rather than fixed t, or speed varies
with control point spacing and the flight visibly slows through tight sections.

## Step 4b — lanes: the bend, slight turns, backing up (2026-09-09)

The navigator that flies the course is `tools/radar_commit.py`; its module
docstring is the authority on the rules. What changed, and why:

**The camera would not enter a lane a tank can drive through.** Measured on
the monastery at a 2 m standoff, with four synthetic courses drawn straight
through the village (`there and back`, so the navigator has to find the
lanes): two of four never closed, at 891 and 2051 reversals. Three things
stacked up against a lane, and only the third is the navigator:

1. The A* that lays the nominal course (`flight_plan.build_cost`) works on a
   2.7 m grid, max-pooled, with the standoff dilated on top, and charges
   `14/(d+1.5) + 26/(d+1)` for closeness to a wall. It seals anything under
   ~5.5 m and routes round anything narrow when a wider way exists. 16 % of
   the monastery's free space sits in corridors 6 m or narrower. **Not
   changed** - it is a route-shape preference, and the shipped routes are
   tuned to it.
2. The navigator's own standoff (`BODY_R`, the Studio's Standoff slider): at
   6 m a lane must be 12 m wide. **Not changed**; it is the slider.
3. The trap rule. Its far probe is a straight 22 m line, and a lane that
   bends inside 22 m read as a pocket. **Changed**: when the straight probe
   hits an object, a continuation is tried from the near point out to
   `BEND_MAX` (75°) in `BEND_STEP` (15°) steps for the rest of the distance.
   A pocket still has no continuation.

**And there was no reverse.** The only retreat was a 180° snap when boxed
in, and the heading snapped to whatever bearing the sweep chose in one 2 m
step. Now:

- the heading moves at most `TURN_STEP_DEG` (8°, a 14 m radius at 2 m steps)
  per step toward the chosen bearing;
- the step is tested on the heading it is actually flown on;
- when it does not fit, the camera backs up `BACKUP_STEPS` (2) locations of
  its own track, turning one `TURN_STEP` as it goes - a reversing arc - and
  tries again. The backed-over points are dropped, so the path that remains
  bends as sharply as the corner needed and no sharper;
- the per-step cap escalates one `TURN_STEP` per `BACKUP_ESCALATE` (4)
  fruitless backups and decays one per `BACKUP_DECAY` (3) steps that fit.
  Escalating on every backup and decaying on every step oscillated 8°/16°
  forever in a tight spot.

Result on the same four courses: 3 of 4 close, reversals 3236 → 0, the one
that closed at 12 000 m now closes at 420 m with 10 backups. The fourth is a
straight line through a tree cluster and failed before as well.

**The cost of slight turns**: the shipped monastery route now deviates up to
26 m from its nominal at the hook after the departure leg (was 4 m), with no
detour and no backup - a 14 m radius cannot follow that hairpin, so the
camera overshoots and comes round. 12°/step only brings it to 15 m. That is a
property of the course shape, not the navigator; a route drawn without a
hairpin does not show it.

`radar_commit.py` prints `backups=` in its summary and draws each one as a
short amber dash.

## Step 4c - the grid was the constraint all along (2026-09-09)

`ROUTE_GRID` 512 -> 2048, `BODY_RADIUS` / `BODY_R` 6.0 -> 0.5,
`TURN_STEP_DEG` 8 -> 32.

**The grid is a floor on the standoff.** The dilation is a whole number of
cells, so at 512 (2.73 m a cell) the smallest clearance that can be expressed
is 2.73 m however small the body radius is set. The old standoff sweep -
"failed to close at 8, 6, 4, 3 AND 2 m of standoff, so the standoff was never
the binding constraint" - never left that floor. 2 m and 3 m were the same
number to it.

Measured on 19_monastery, against clearance computed at the bake's own
resolution, of the space a 0.5 m body can genuinely fly:

| grid | cell | keeps | what it loses |
|---|---|---|---|
| 512 | 2.73 m | 79.8 % | 100 % of it corridor under 12 m |
| 1024 | 1.37 m | 90.4 % | same |
| 2048 | 0.68 m | 96.4 % | same |

The coarse grid does not lose the map - it loses **exactly the lanes**.
Computing clearance finely and pooling it down does not help: a 1 m lane either
blocks (conservative pooling) or breaks into dashes (centre sampling). The grid
has to resolve the corridor. Cost is ~41 s for a worst-case A* leg at 2048
against 1.75 s at 512, and routing is a one-off click. If that ever matters,
the answer is a coarse pass to find the corridor and a fine pass banded around
it, NOT a coarser grid - that puts the floor back.

**Standoff.** 6 m demands a 12 m lane and leaves 60.5 % of the map flyable;
1.75 m (a real tank's half-width) leaves 78.5 %; 0.5 m demands 1 m and leaves
86.3 %. Anywhere a tank can drive, the camera should fly.

**Turning circle.** At a 2 m step, 8 degrees is a 14.3 m circle. A course that
doubles back on itself - which is what placing two targets up one corridor
produces - asks for a 180 degree reversal in the width of that corridor, and a
14 m radius cannot do it. It swings wide, logs a detour and backs up. On a
538 m loop where 40 of its 135 points sit within 3 m of a distant part of
itself:

| deg | radius | max dev | mean dev | detours | backups |
|---|---|---|---|---|---|
| 8 | 14.3 m | 22.2 m | 1.36 m | 2 | 4 |
| 16 | 7.2 m | 8.3 m | 0.53 m | 0 | 1 |
| 32 | 3.6 m | 6.5 m | 0.36 m | 0 | 0 |
| 45 | 2.6 m | 4.2 m | 0.25 m | 0 | 0 |

45 tracks best; 32 is the compromise that still fights nothing and gives a
gentler arc for a camera that has to look like it meant it.

### Three bugs found while measuring

- **The dilation used `round`.** That gives LESS clearance than was asked for
  whenever the radius is not a whole number of cells - at 512 a 6 m radius
  became 5.47 m. `ceil` now.
- **The clearance cost was computed on distance in CELLS**, so it silently
  depended on `ROUTE_GRID`: the same wall is four times further away in cell
  units at 2048 than at 512, and raising the resolution alone would have gutted
  the term without anyone touching a weight. Metres now.
- **The clearance term priced lanes out.** `14/(d+1.5) + 26/(d+1)` reaches
  about 19x the base weight one cell from a wall, so A* went round every
  passable lane. `blocked` is already dilated by the body radius, so what is
  left is a *preference*, and it saturates: full penalty against a wall,
  nothing beyond `CLEAR_FULL`, linear between.

### `min_clear` is blind - do not tune on it

`score()`'s `min_clear` reports 0.97 m in every run, floored by the one-cell
standoff. It cannot report "flew closer to a wall". `DIRECT_TO_TARGET` was
nearly shipped because of this: flying straight at the aim point when the line
is clear measures as 3 m off a 458 m route and no change in deviation, and the
gap centring it skips was worth 4.32 m -> 3.49 m of worst-case clearance.
Measured against the UNDILATED mask at the bake's resolution, which is what
that comparison needs. The switch is in and OFF.

## Why the bake must be its own pass

Proved the hard way on 2026-09-02 by trying to shortcut it: the G-buffer
already carries a surface-kind byte, so a top-down beauty render was
reinterpreted as a collision mask. It took three attempts to get a binary
image, and every failure was post-resolve machinery contaminating the result:

1. **Water drew over it.** MapWater is a FORWARD pass after the resolve, so the
   river painted itself blue straight onto the mask.
2. **SSR added on top.** Reflections tinted the supposedly binary output grey.
   Also after the resolve.
3. **The tonemap ate the white.** deferred.frag outputs
   `correct(final_color, exposure, 1.2)` - a saturating curve - so a written
   1.0 arrived as mid grey. Writing at the very END of main bypasses it. Note
   there are TWO other branches that also write outColor from gColor; a patch
   that only touches the lit path silently misses them.

None of these can touch a dedicated top-down FBO pass that writes height and
coverage into its own targets. That is the argument for step 2 being real work
rather than a reinterpretation of the beauty pass - the shortcut LOOKS cheaper
and is where all the contamination lives.

The one-off mask did confirm the data is there and sane: Abbey came out 25%
obstacle coverage, with the walled town centre, the tree lines and the rock
clusters all clearly separated from open ground.

## Known limits, decided rather than discovered

**A height field is 2.5D, and there is no second layer to rescue it.** One
height per texel stores only the top of things, so a stone arch reads as "solid
at 12 m" and the camera will not take the clear air underneath it. The ceiling
map was supposed to fix exactly this and cannot, for the reason recorded under
step 2 above.

So the accepted limit is: **the camera flies OVER what it can clear and AROUND
what it cannot, and never through anything.** Arches, gates and bridges are
flown around. That is a real loss of a nice shot, and it is a deliberate trade
rather than an oversight.

**Coarse texels are conservative, and that has a cost.** Each texel takes the
max height in its cell, so one 12 m lamppost makes its whole 10 m cell read as
12 m and the camera swerves around a block of empty air. Safe, but it can look
silly. If it does, the fix is a finer HEIGHT map while the steering keeps
stepping at waypoint/4 - decouple the two resolutions rather than coarsening
the safety.

**Terrain height lookups are unverified on 133x133 maps.** `get_Y_at_XZ` and
friends were written when every map was 69x69. The 2026-09-01 game patch made
101_dday and 23_westfeld 133x133, and westfeld's chunk heights already look
wrong. The step 1 clamp uses `get_Y_at_XZ_fast`, so it is trustworthy on Abbey
and suspect on those two. Step 2 removes the exposure entirely, since the bake
becomes the source of truth.
