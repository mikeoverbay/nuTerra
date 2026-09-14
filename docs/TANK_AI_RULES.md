# Tank AI rules

The owner's rules for the self-driving tanks, one place, kept current as the
work moves. Owner writes the rules; Tank AI (`nuTerra/Tanks/`, `tank_tools/`)
implements them; Fable AI Navigation Helper keeps this file in step. Status is
what Tank AI reported against the CODE, not memory. Started 2026-09-14.

Status words: **DONE** built and running, **PARTLY** half in, **HELD** waiting
on the owner, **OPEN** not started. "Verified" means seen driving, by the owner.

| # | rule | status | how / what is left |
|---|---|---|---|
| 1 | Assign each tank's start by where it spawns - spatial distribution, not list order | DONE | `AssignStarts` sorts both sides on world X and matches rank to rank, after the hulls are placed. A column of three shares one start. |
| 2 | Seek the assigned start with the maze solve (flood fill). A* parked. | HELD | The route itself is flood-fill and A* is parked. The SPAWN-to-start leg is straight-line steering; the stitch was turned off by the owner 2026-09-14. Waiting on his word whether it comes back. |
| 3 | Inside a ring is a win for that tank | DONE | Search: the sweep's goal is the ring via a super-source flood; 25 of 25 team-1 lanes finish 13-51 m from the flag. Driver: arrival is `InEnemyRing`, within 50 m of the other side's base marker. Not yet seen driving. |
| 4 | Whiskers must not stop a tank following its path when it can clear the distance ahead | DONE | `RayHits` keeps the hit distance; `BlockedAhead` fires only within half a hull plus one second of travel (~11 m at 7 m/s, 2 m stopped). The 20 m whisker sees far, reacts late. Not yet seen driving. |
| 5 | A tank forced around another tank seeks ANY point on its path line, not the original point | DONE | `TankSim.Rejoin` snaps to the nearest point on the hull's OWN run, searched forward from the current index so a doubling-back road cannot hand back a passed waypoint. Armed on skirt or go-around, consumed when free. Not yet seen driving. |
| 6 | Head to head: each tank always turns RIGHT to go around | PARTLY | Turn: `ClearWay` tries right, front-right, front, front-left, left, fixed right swing as fallback; never into a side the rays call occupied. OPEN: the re-search from the new position (flood-fill downhill walk, not A*). |
| 7 | Session-to-session talk stays short and to the point | DONE | `C:\nuTerra_shared\DIRECTIVES.md` rule 10. |

## Standing decisions

- A* is parked. One planner: the flood-fill distance field (Lee / Dijkstra on
  the metre grid, `tank_tools/maze.py`). Re-planning after a go-around is a
  downhill walk from the new position, not a second algorithm.
- Path Studio is on hold, not dead. Nothing here touches camera flight or its
  path creation (`tools/`, `PathStudio/`, `nuTerra/cam_paths/`).

## Log

- 2026-09-14 - file started. Rules 1-7 from the owner. 4, 5 and the driver half
  of 3 landed the same day, built clean, not yet driven.
