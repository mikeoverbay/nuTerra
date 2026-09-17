# Getting the node board to drive — working log

2026-09-17, by Tank AI work. Start of session 15:14 PST.

## The protocol (the owner's)

> "we only rebuild if changes to the nodes is not a fix. then we shut it down
> and try a recode to node logic"

> "I would only run for 10 seconds and increase as we get more winning nodes"

> "repeat and stop if its not gonna happen"

Board changes first — the graph is data and a change costs a relaunch. Node
*logic* (`GraphBrain.vb`) costs a build and is the fallback. Runs are 10 s
until the hull drives without looping.

## How a run is read

The board reports itself, one line per change of decision, naming the node:

```
brain: board Plank Hit=Y Will Clear=Y ... -> Drive Heading#68
```

Three questions in order: did the hull MOVE; which node won and did it keep
winning (one node repeating is a loop); did any test answer something
impossible (a wiring or units bug).

Adding `#id` to that line mattered more than it looks — the board has two
`Reverse` nodes and three `Turn To`, and for several iterations I was reading
a kind name and guessing which one had fired.

## Iterations

| # | changed | kind | result |
|---|---------|------|--------|
| 1 | baseline | — | `Stop` only. Never moved. |
| 2 | `Scan` gets `DriveRadius` | logic | **drives** — 11.9 m/s, 24 m |
| 3 | plank branch declines instead of `Stop` | board | drives, then chatters |
| 4 | `Vote` moved onto `Door Gap` | board | chatter **514 → 60** |
| 5 | `Not Moving` + spin inside Backing | board + test | `Turn To` fires |
| 6 | `Not Moving` = commanded-and-stuck | logic | rock-and-rotate, still trapped |
| 7 | `Backed Enough` clock | board + test | Backing exits at last |
| 8 | trace names the node, not the kind | logic | guessing stops |
| 9 | stuck-turns rung ABOVE the states | board | ends the run moving |
| 10 | `Best Progress`: forward arc, positive gain | logic | range starts **closing** |
| 11 | last resort drives the goal, not reverse | board | — |
| 12 | `Rear Better` needs a blocked front | logic | — |
| 13 | plank branch is a Priority, not a Sequence | board | scanning stops once a way is held |
| 14 | `Commit` latches the chosen way | board + pick | chatter **1916/1729 → 609/606** |
| 15 | any-gap-that-fits rung, above backing out | board | **`Reverse` gone**, 155 changes, 30.4 m door at 14° |

| 16 | `BrainSim` logs WHY it refused a move | logic | the measurement that ended the guessing |
| 17 | no throttle while badly misaligned | logic | refusals **21 → 3**, changes 155 → 51 |
| 18 | `Commit` does not time a turn as failure | logic | **full speed, 12 m/s**, runs a 31.3 m door |

Nine of the eighteen were board changes.

## The one that mattered most, and it was not in the brain

`BrainSim` moves the hull along its **current heading** and steers separately:

```vb
b.headingRad += str * TURN_RATE * dt
want = b.spawn + fwd * step_m        ' fwd is the HEADING, not the bearing asked for
```

So `driving -134 deg, 2.0 m clear, thr 0.35` meant: turn hard left, and
meanwhile drive **forward into the thing we are turning away from**. The
destination failed seven centimetres from a position that passes - every frame,
for the whole run. The refusal log is what showed it: destination equal to the
current position, the current position standable, and the move refused anyway.

The turn budget was meant to handle this and could not, because the creep floor
put 0.35 back underneath it. Scaling a throttle to nothing and then flooring it
at 0.35 is still 0.35 into a wall. Past about fifty degrees there is now no
throttle at all - turn on the spot, which the sim never refuses, and drive once
the nose is near.

That then exposed the last one: `Commit` releases a way after a second of
gaining no ground, and turning on the spot gains no ground **on purpose**. Every
turn was abandoned two thirds through, the bearing jumped, the next turn began
again. Its clock now runs only while we are asking to move.

## What was actually wrong

**`BrainRadar.vb:527` — `If bodyR > 0.0F Then`.** The whole ways-building block
is gated on it, and `GraphBrain` called `Scan(pos, heading, 0.0F)`. `WAYS` was
cleared and never refilled, so `Has Way` could never be true and the board
could only ever reach `Stop`. One argument, and nothing else could work until
it was right.

**The priority order was inverted.** `p4.a` TESTED the goal direction and DROVE
the deepest ray — two different bearings — and it sat above the opening rule.
So any time the goal looked passable the tank charged past a gap it should have
taken, the corridor shut, the plank fired, and it turned back. That is the
"bypassing openings and turning back" exactly.

**Rotation is the only command that is never refused.** `BrainSim` applies the
heading change *before* it tests whether the destination is standable, so a
wedged hull can always turn and can never push. Every state's own answer to
being stuck was to push harder in the direction already refused. The
stuck-turns rung above the state machine is what fixed it.

**Nothing keyed on an instant can settle.** `Reverse` and `Drive Heading` key
off opposite sides of one distance threshold, and the hull rocks across it, so
they alternated twice a tick — 1916/1729 in ten seconds. Clocks on the wedge
and stuck tests, and `Commit` holding a chosen way until it stops gaining
ground, are the same fix three times.

**In a pocket, the out is a gap that does not point at the goal.** Filtering
`Best Progress` to forward-and-gaining is right when there is a choice and
fatal when there is not: nothing qualified, and the chain fell past the
openings to reversing. The any-gap-that-fits rung removed `Reverse` from the
run entirely.

## Where it stands

Decisions look right. The tank picks wide gaps roughly ahead and commits to
them. What is left is physical: `thr 1.00, speed 0.0` still appears — the hull
is commanded forward and `BrainSim:214` refuses the move because the
destination is not standable. The board's answer to that is the stuck-turns
rung, which fires, but a cluttered start still pins it for long stretches.

Next, in order:

1. Log the rejection in `BrainSim` — `want`, `DriveRadius`, hull position —
   when `Standable` refuses. It is the one measurement never taken.
2. `DriveRadius` is 1.94 m and the test wants a clear box of that half-size at
   the destination. In a start surrounded by blockers that may simply be more
   room than exists, in which case the fix is not in the brain at all.
3. Score against RangeBrain over 70 s once it stops being pinned.
