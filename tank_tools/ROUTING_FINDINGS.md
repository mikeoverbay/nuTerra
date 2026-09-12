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

## What is NOT decided

Both resolvers are still in `ray_studio.py` and both still draw. The owner
asked for the expanding rings and asked to watch them; `[a]` in the viewer
draws the search catalogue beside them in a different family of colour so
the two can be compared on the same ground. Which one the tanks actually
use is his call, not one taken behind him.
