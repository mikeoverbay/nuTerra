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
| 2 | **floor height** | underside of overhangs - the ceiling you can fly beneath | `Min` |
| 3 | **mask / kind** | obstacle vs bare terrain | — |

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

## Known limits, decided rather than discovered

**A height field is 2.5D.** One height per texel stores only the top of things.
This is why the floor map exists - without it, a stone arch reads as "solid at
12 m" and the camera refuses clear air at 3 m. Two layers handle an arch or a
bridge. They do NOT handle genuinely stacked geometry (a multi-storey interior);
that would need a voxel or portal structure, and is not worth reaching for until
the simple version is seen to fail.

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
