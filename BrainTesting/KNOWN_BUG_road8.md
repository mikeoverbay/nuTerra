# Open bug: the road 8 tank finds its start, then loses the road

Recorded 2026-09-25 at the owner's instruction: "Road 8 tank is not following
its assigned path. It goes to start and gets lost for some reason" and
"i want to use as is thou so dont destroy why it gets losts. I want to test".

**This bug is deliberately NOT fixed.** It is a live test case. Nothing in the
follower's advance rule has been changed to make it go away. If you fix it,
put the old behaviour behind a tune flag so this scenario can still be run.

## Reproduce

```
BrainTesting.exe 19_monastery perteam=5 learn graph pingoal path trail=100 maximized owner=PathFinder
```

Watch hull 2 — press `TAB` twice from the default view, or look for the badge
reading `H2  road 8`. Reliable every run, but NOT identical run to run: the
sim diverges from the same code and the same start (see the owner's standing
note that one run is a sample). What repeats is the shape, not the numbers.

The other four hulls complete. This one does not.

## What it does

From the black box, `saved/BUG_road8_hull2_lost_20260925.csv` under
`C:\nuTerra_shared\tank_logs\`, tank 3 (= hull 2, id is hull+1):

```
 8.9 s  ( 68.3, -374.6)   reaches road 8's start at (66.5, -377.5) correctly
12.5 s  ( 80.5, -338.1)   driving north up the road, 30 m clear, full throttle
16.0 s  ( 36.1, -333.9)   swings 45 m WEST
20.2 s  (-15.3, -334.3)   95 m west of its road
26.0 s  ( 25.2, -306.0)   turning back east
30.1 s  ( 66.0, -316.7)   back on the corridor
32.6 s  ( 91.5, -318.8)   overshoots 25 m east, starts backing
        ...backing attempts 1, 2, 3, 4 around (100, -308), indefinitely
```

The stall detector names it:

```
hull 2 has driven 250 m to cover 68 m and still not reached waypoint 24/143
at (20.5, -237.5) - it is 126.1 m away, ahead of the nose.
```

Tank 3 wrote 3,636 rows against 1,245-1,800 for the other four. Rows are
written on a change of reason, so twice the rows means its board is flipping
decisions twice as often as a tank that is getting somewhere.

## Cause, from the tape with wp_at filled

```
 4.2 s  wp  0/143  ( 12.8,-377.4)  driving   1 deg; 30.0 m clear; thr 1.00
 8.5 s  wp 14/143  ( 69.8,-373.9)  driving -106 deg; 30.0 m clear; thr 1.00
15.1 s  wp 14/143  ( 51.8,-324.4)  driving  -50 deg; 30.0 m clear; thr 1.00
17.8 s  wp 14/143  ( 35.0,-335.4)  driving  -67 deg; 30.0 m clear; thr 1.00
18.2 s  wp 19/143  ( 23.5,-348.1)  driving +108 deg; 30.0 m clear; thr 1.00
22.4 s  wp 19/143  (-16.4,-327.4)  driving  +71 deg; 30.0 m clear; thr 1.00
27.9 s  wp 24/143  ( 16.5,-306.1)  driving  -32 deg; 28.9 m clear; thr 1.00
```

IT IS NOT THE ORDERING. Measured on the graph: road 8 is 67 nodes running
along-axis 12.6 m to 777.3 m with ZERO segments going backward and zero
total backtrack. Head to tail, correctly ordered. The 95 m westward excursion
that looks wrong on an x,z track is the ROAD - node 1 sits at (-2.5, -308.5),
69 m west of the road's own start, reached down a single 97 m segment.

IT IS NOT AN OBSTACLE. The corridor reads 30.0 m clear and the throttle is
1.00 through the entire wander. Nothing is in the way.

IT IS A CORNER THE HULL CANNOT TURN. Road 8 takes a 45 degree corner at node
1 at the end of that 97 m straight. The smoother leaves corners around 3 m of
radius - it REPORTS worst radius and deliberately does not enforce it, see
worst_radius in BrainPath - and a hull at 12 m/s with a 0.45 rad/s turn rate
needs 26.7 m. So it arrives at the corner at full throttle, cannot hold it,
overshoots, and flies a 50 m loop to come back. At 18.2 s the goal steps from
waypoint 14 to 19 and the demanded bearing flips from -67 to +108 degrees, a
175 degree reversal, which is the far side of that loop.

The waypoint index jumping 5 to 14 at a time is NOT the fault. Waypoints sit
about 5 m apart after Chaikin and the reach is 10 m, so arriving at one ticks
off several. That is the rule working.

SAME ROOT CAUSE AS THE BASE-RETURN BUG, one layer up. That one was a follower
that could not pass a waypoint inside its turning circle. This one is a
follower that does not SLOW DOWN for a corner it cannot hold. The standing
open item "throttle_for_turn still discards its bearing" is the same sentence
from a third direction.

The fix, when it is wanted, is a speed limit from the curvature ahead:
v <= R * TURN_RATE, with R the circumradius of the next few waypoints. That
is the number worst_radius already computes and throws away. Put the old
behaviour behind a tune flag so this scenario stays runnable.

## What is known, and what is not

Known: it reaches its assigned start. The corridor reads 30 m clear and
throttle 1.00 through the wander, so it is not being blocked - it is being
steered. The commanded bearing swings through -107, +101, +75 degrees in the
seconds before it goes wrong, which is a goal moving a long way between
ticks, not a tank fighting an obstacle.

Waypoint 24 sits at (20.5, -237.5) while road 8's two ends are both at
x = 66.5, so the maze genuinely routes this road 46 m west around something
mid-way. That switchback is the part of the road it fails on, and the first
thing to check is whether the reached-or-passed rule is skipping across it:
`waypast` is 3, so a waypoint within 30 m and behind the nose counts as
passed, and a switchback puts waypoints behind the nose that have not been
driven. `tune=waypast=0` disables that half of the rule and is the first
experiment.

Answered above, by filling `wp_at` and `wp_of`. Those columns existed in
every tape ever written and read `0/0` on every row, so the one question the
file exists to answer about a follower could not be asked of it. They are
filled as of this commit, from the follower rather than from the brain's own
belief.

Still not known: why it settles into a repeating backing loop around
(100, -308) rather than recovering the way the other four do after their own
overshoots. Four tanks on the same board recover; this one does not, so there
is something about that spot as well as about the corner.

## Related

- The stall detector that found it is in `BrainPath.Tick`.
- Road ids are stable and come from Tank Path Studio's `<map>_paths.json`;
  point indices are not, so always name the road.
