# Rays or search: what the measurements say

19_monastery, hull 4.5 m, collision = flight bake at 8192² / 0.171 m,
Y over 1.0 m solid, trees + fences + props exempt (a tank crushes them).
Every route below was re-sampled at a quarter texel and none has a single
sample inside solid ground.

Written 2026-09-12 after the owner asked the Tank AI and Path Studio
sessions to settle the algorithm between them.

## The two answers, side by side

|                     | rays (ring resolver) | search (A* catalogue) |
|---------------------|----------------------|-----------------------|
| routes team 1 -> 2  | 2                    | 8, and that is the cap not the map |
| routes team 2 -> 1  | 1                    | 8                     |
| shortest            | 939 m                | 826 m                 |
| time                | 45-84 s              | 11.5 s for all eight  |
| points per route    | 321-376              | 10-54                 |
| repeatable          | no                   | yes                   |
| what a failure means| it gave up           | proof there is no way |

## Why the ray family struggles HERE

Path Studio's `tools/radar_tangent.py` solves this problem in the air and
its layer-3 docstring names our failure exactly:

> A ring fitted round the blocker would be hopeless here; a circle round a
> 200 m wall has a 100 m radius and its tangents mean nothing.

Three measurements, all on this map:

1. **At team 1's base, all 121 fan rays are blocked at a 90 m look.** There
   is no silhouette to walk around because the tank stands IN the clutter
   rather than flying above it.

2. **11% of points along a route already known good have no clear bearing
   in any direction at 90 m.** A fan navigator dies at every one of them.
   Clear-bearing rate by look distance: 6 m 100%, 12 m 100%, 25 m 100%,
   50 m 96.9%, 90 m 89.2%.

3. **A fan offers two candidates per hop; in clutter both fail.** The ring
   survives only by offering 864 per collision, which is brute force, not
   insight - and is why it needs 121 opening bearings, and why 1.5 degrees
   of opening bearing swings it from 11 winning chains to 5.

A correction worth keeping, because it was stated confidently and was
wrong: *"a 3 m fan can never see past a 40 m building, so look far."*
Backwards. At tank scale you cannot see 90 m in ANY direction, so a long
fan just reports blocked everywhere. Short looks are what work here.

## Why the search wins

Path Studio's own reasoning for their layer 4:

> Wall-following exists because a robot cannot see the map. This one can -
> the whole occupancy grid is in memory - so the way round is a search, not
> a guess. Optimal, and when it returns nothing that is a PROOF the target
> is unreachable, not a timeout.

Octile A* on a coarse grid grown by the hull radius, string-pulled against
that same grown grid, then the owner's rule 3 on top: tag the corridor
spent, search again, stop when there is no way left.

Two traps already paid for, do not re-enter them:

- **String-pull against the GROWN grid, not the fine centre line.** Testing
  the centre line says nothing about the hull's shoulders; that is how a
  smoother once reported an 8.5 m gap for a 9 m hull.
- **The heuristic must match the cost.** Octile heuristic with octile step
  costs. A mismatched heuristic is not a slower A*, it is Dijkstra wearing
  a hat - 466,229 expansions on this map once already.

## Where the ray walker dies, three times over

Three independent locations, none of them blocked terrain:

| where | what the ground is | what the rays did | what the search did |
|---|---|---|---|
| west, near (-115, -313) | one free region crossing N-S, 67.9% takes the hull, widest 58.7 m | stall, "no progress" | routes 0 and 1 pass within 75 m |
| south from team 2, band near (-32, 101) | 53% takes the hull, 2 regions cross N-S, but 41 separate free regions in a 180 m box | 31 of 31 sampled chains die here | route 0 passes 33 m away |
| team 1's base, any bearing | - | all 121 fan rays blocked at a 90 m look | unaffected |

The common factor is CLUTTER, not blockage. The bug walk fails where the
ground is broken into many small free regions, which on a town map is most
of it. That is why the direction asymmetry exists at all: team 1 to team 2
wins 100 chains of 121, team 2 to team 1 wins 3 - on a map the search
crosses in 812 m one way and 814 m the other, i.e. a symmetric problem.

An asymmetry that large on a symmetric problem is the clearest statement
available that the number of routes a bug walk finds is a property of the
walk, not of the map.

## Which planner: measured, not argued

Path Studio's recommendation was Theta* (Nash, Daniel, Koenig, Felner,
JAIR 2010) - A* whose parent pointer skips to the furthest ancestor still in
line of sight, so the path is taut through corners instead of zig-zagging on
grid edges - with the note that grid A* plus a string-pull gets most of the
way there. Both were built and run:

| | length 1->2 / 2->1 | points | time |
|---|---|---|---|
| A* + string-pull   | 829.3 / 826.2 m | 7 / 10 | 0.3 s |
| Lazy Theta*        | 812.6 / 814.6 m | 19 / 16 | 0.6 / 0.9 s |
| Lazy Theta* + pull | 812.2 / 814.4 m | 13 / 11 | same |

Their prediction held: the string-pull alone lands within 2% of Theta*. The
catalogue uses Lazy Theta* + string-pull anyway, because it is baked once at
startup and then RACED on - a fraction of a second costs nothing there and
twenty metres in eight hundred is worth having. Plain Theta* on its own is
NOT the answer: it leaves collinear runs, so it gives more waypoints than the
cheaper method, and the pull is what fixes that.

The full catalogue, 19_monastery, 0 of 344,557 samples in solid either way:
eight routes each direction, 812 m to 2,737 m. Asked for twelve and got
eight BOTH ways - so eight is the map, not a cap. That is the owner's "when
we cant find a way there, we are done" as an actual proof.

## Plan with the search, drive with the rays

Path Studio's framing, and it is the one that reconciles the measurements
with the owner's instruction: *the short ray is right for driving and wrong
for planning, and those are two jobs.* The catalogue is built by search and
held in memory; the tank then DRIVES that polyline and uses its 3 m rays for
the local dodge - a shell hole, another tank, something the bake never knew
about. The rays were never the wrong idea, they were the wrong layer.

Also worth recording, from Path Studio's own instrumentation: on their
shipped monastery plan their tangent layer fired 0 of 313 moves, and their
point-to-point walker base-to-base never arrived at all - 438 m short one
way, 357 m the other. Neither of their bug-walk layers is the workhorse.
Both TangentBug shapes - their fan silhouette and our expanding ring - come
from the same 1998 paper (Kamon, Rimon, Rivlin, IJRR 17(9)), and both exist
because a robot cannot see the map. We can.

## The joint recommendation

Agreed between the Tank AI and Path Studio sessions on 2026-09-12, put as
one recommendation rather than two half-arguments:

> **The search plans the route. The short rays drive it.**
> In the air and on the ground alike.

Path Studio's layer hit rates say the same thing from the other end. On
their shipped monastery plan, where waypoints are close together: direct
93%, ring 7%, search 0.3%, tangent **0 of 313 moves**. Treated as two far
apart points, base to base: search 92-100%, and their walker never arrived.
So the fan / tangent / ring family is the DRIVER in both apps, and the
search is the PLANNER in both.

Concretely here: the catalogue is built by search and held in memory at
startup, and the tank drives that polyline using its 3 m rays for the local
dodge - a shell hole, another tank, anything the bake never knew about. The
ray work is not discarded; it is the other half, and most of it already
exists in `nuTerra/Tanks/TankDrive.vb`.

One correction to something said earlier in this file's own history: the
claim that "the honest range is the map" only holds where you can SEE. In
clutter you cannot, which is one more reason the planner has to be a search
rather than a longer ray.

## Which ring he meant

Settled, and not the way it first looked. Two different objects share the
name: Path Studio's acceptance ring is a tolerance around the TARGET; ours
is an obstacle-rounding circle at the HIT POINT. The worry was that the
owner had pictured theirs from watching the Studio, which would have meant
a day spent building the wrong thing.

His own words decide it. On zoning: *"draw rings and find areas as large as
we can that the ring fits without hitting something"* - a free-space circle
grown until it touches. On obstacles: *"expanding rings"* for getting round
one. Both are a circle grown until it hits something, which is ours.

The ring resolver therefore stays, and stays drawn: `[a]` in the viewer puts
the search catalogue beside it in a different family of colour so the two
can be read apart on the same ground.

## What is still HIS to decide

Whether the tanks actually switch to search-planned routes. Everything above
is evidence for that decision, gathered because he asked the two sessions to
work the algorithm out between them - but the switch itself has not been
made behind him, and both resolvers still run.
