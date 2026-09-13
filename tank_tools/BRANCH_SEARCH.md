# The branching ray search — pseudocode

> # SUPERSEDED, 2026-09-12 evening. THE CODE THIS DESCRIBES IS DELETED.
> 
> The owner: "the old radar seeking code with expanding rings" — removed from
> `ray_studio.py` in `e3245a29`, along with the bearing sweep and the fan.
> `ring_tangents`, `chain`, `resolve`, `ring_branch`, `BranchTree`,
> `ItemLedger` and the tangent helpers are all gone, 1,575 lines of a
> 4,333-line file.
>
> **What replaced it: `tank_tools/maze.py`.** Flood fill from the goal, then
> walk downhill. Exact, no parameters, nothing to tune. The comparison that
> ended it, base to base on monastery:
>
>     branch search   877 m   1.12x direct   621 casts
>     flood fill      846 m   1.08x direct   0.6 s      <- the exact optimum
>
> The branch search got within 3.7% of optimal and cost a day of tuning to do
> it; the flood fill is optimal by construction and has no knobs. See §12 and
> §14 of `docs/HANDOFF_2026-09-11_tank_ai.md`.
>
> **This file is kept as history, not as a spec.** It records a design the
> owner and I built together and everything it taught — the forward-only arc,
> the ring-per-attempt, why breadcrumbs strangled it. Do not implement from
> it. Do not update it.

The owner's design, 2026-09-12. Replaced the 121-independent-bearing sweep
with ONE tree, walked depth first, with backtracking and a claim rule that
made it terminate.

## Status

| | |
|---|---|
| spec agreed | 2026-09-12 |
| implemented in | `tank_tools/ray_studio.py`, `branch_search()` |
| in nuTerra (VB) | not yet |
| object identity | connected components, kind-split. The bake's real per-object ids (`<map>_ids.u32`, bake_version 2) are merged but not yet consumed here |

---

## The data structure (owner, 2026-09-12, second pass)

The first version of this doc branched ONLY at collisions. That was wrong and
the trial run proved it: 4,000 rays, 0 paths, closest approach 484 m of 785.
The real structure is per-POINT, not per-collision.

    POINT {
        id
        position
        angle_in      the ray angle that REACHED this point - the angle pointer
        parent        the point we came from
        origin        TANGENT  (this point is a tangent off a ring)
                    | CONTINUE (this point is just the end of a clear move)
        tried[]       angle_id -> PASS | FAIL
    }

    ANGLE         every ray angle is quantised and carries an ID, so it can be
                  recorded, blocked and traced. "each angle is an id".

    HIT LIST      the growing record of (point, angle) already tried and how it
                  came out. This is what stops the search re-casting a ray it
                  has already won or lost on.

    PATHS         completed routes, kept WHOLE - start to finish. Each good
                  move is part of the solution and the solution is the chain.

### Backing up

Back up to a point and ask **where that point came from**:

- **TANGENT** - the point is a tangent off a ring, so the alternatives are the
  other side of that ring.
- **CONTINUE** - the point is just where a clear move ended. It is STILL a
  branch point: we cast other ray angles from it, skipping every angle already
  in the hit list for that point, won or lost.

That second case is the part the first version missed. A continuation is not a
dead corridor with one exit - it is a place we can leave in any direction we
have not already tried. Branching only at collisions gave a branching factor of
1.00 and a 2,996-deep chain.

### Why the angle list is load-bearing

Without a per-point record of angles tried, backing up either re-casts rays it
has already cast - and loops for ever - or cannot tell an untried angle from a
spent one and stops early. The list IS the thing that makes backtracking
terminate.

## The table

Every ray is a node with an id and a parent, so any ray traces back up to
the group it came from.

    RAY {
        id            unique
        parent        id of the ray we branched from, or NONE for the root
        from          where this ray starts (a base, or a tangent point)
        heading       direction it flies; HELD until something stops it
        hit           where it stopped, or NONE if it reached the base
        object        which object stopped it (bake id), or NONE
        side          which hand we went round that object: LEFT or RIGHT
        children      the rays this one spawned
        tag           OPEN | MADE_IT | DEAD | UNTRIED
    }

    CLAIMED   set of (object_id, side) pairs, from completed paths only
    PATHS     the finished routes

`UNTRIED` is load-bearing. Without a state that says "this branch exists
and nobody has walked it", backing up cannot tell an unexplored ray from
one already dead, and the search either repeats itself or stops early.

---

## Walking one ray

    WALK(ray):
        p = ray.from
        repeat:
            q = p + ray.heading * STEP           # a step, five yards-ish
            if the segment p->q is not clear:
                return HIT at p, with the object that blocked it
            p = q
            if p is within REACH of the base and p->base is clear:
                return ARRIVED
            if we have travelled further than the budget allows:
                return DEAD

The heading is HELD the whole way. We do not re-aim at the base every
step - that was tried and every chain folded onto one greedy line within
a few metres of the base.

---

## Branching at a collision

Each collision generates rays, one per side. Both are branches.

    BRANCH(ray, hit_point, object):
        children = []
        for side in (LEFT, RIGHT):

            # THE CLAIM RULE. A completed path claims (object, side), not
            # the object outright - so another path may round the same
            # building provided it goes the OTHER way.
            if (object, side) in CLAIMED:
                continue                          # this side is spent

            t = EXPANDING_RING_TANGENT(hit_point, side)
            #   ring grows in RING_STEP from RING_MIN to RING_MAX,
            #   first radius with a standable, reachable tangent wins,
            #   the way to it must be clear from ray.from (not from the
            #   ring centre - the tank is still back at the anchor),
            #   and a ray must be able to LEAVE it.
            if t is NONE:
                continue                          # no way round this side

            child = RAY {
                parent  = ray.id
                from    = t
                heading = direction from t toward the BASE   # re-aim here,
                                                             # and only here
                object  = object
                side    = side
                tag     = UNTRIED
            }
            children.append(child)

        return children

The hit point is never a node on the path. It is the ring's centre only;
the path runs from the previous anchor straight to the tangent.

---

## The search

Depth first, left first, and it does NOT stop at the first success.

    SEARCH(base_from, base_to):
        root = RAY { parent=NONE, from=base_from,
                     heading=LEFTMOST_SWEEP_BEARING, tag=UNTRIED }
        stack = [root]

        while stack is not empty:
            ray = top of stack

            if ray.tag == UNTRIED:
                ray.tag = OPEN
                result = WALK(ray)

                if result == ARRIVED:
                    path = the chain of rays from root down to this one
                    PATHS.append(path)
                    ray.tag = MADE_IT

                    # CLAIM WHAT IT USED, so later branches cannot retrace
                    # it - and claim it PER SIDE.
                    for each (object, side) rounded along that chain:
                        CLAIMED.add((object, side))

                    pop ray                       # and carry on searching
                    continue

                if result == DEAD:
                    ray.tag = DEAD
                    pop ray
                    continue

                # HIT: make the branches and walk into the first one
                ray.children = BRANCH(ray, result.hit, result.object)
                if ray.children is empty:
                    ray.tag = DEAD
                    pop ray
                    continue
                push ray.children in order RIGHT then LEFT
                #   so LEFT comes off the stack first - "start scanning left"
                continue

            # every child of this ray has been tried
            if all children are MADE_IT or DEAD:
                ray.tag = if any child MADE_IT then MADE_IT else DEAD
                pop ray                           # back up a level
                continue

        return PATHS

---

## When it is done

When the stack empties, every branch descending from the first ray is
tagged. That is a PROOF that no further route exists under these rules,
not a budget running out - which is the owner's "When we cant find a way
there, we are done" as an actual terminator.

---

## What this replaces, and why

- **The 121-bearing sweep.** Independent chains that shared nothing: the
  first fifty metres out of the base was re-walked 121 times, and the
  outcome was chaotic in the opening bearing - a 1.5 degree shift took one
  direction from 11 winning chains to 5. A tree shares every prefix and
  explores instead of sampling.
- **Dedup after the fact.** The claim rule kills a retracing branch AT THE
  COLLISION, not after four hundred hops. "Stop trying it over and over"
  becomes structural.
- **Connected-component landmarks.** The bake now carries real per-object
  ids (`<map>_ids.u32`, bake_version 2), so "the same object" means an
  actual model placement rather than a blob of whatever geometry touches.

## Still to settle

1. **What counts as "rounded"** when claiming a completed path: only the
   objects the path actually took a tangent around, or every object within
   the hull corridor? The pseudocode above claims only the rounded ones,
   because that is what "hit the same object" reads as - but claiming more
   makes the catalogue smaller and terminate sooner.
2. **The budget in WALK.** Some limit is needed or a branch can wander for
   ever in open ground. Distance travelled without closing on the base is
   the honest one; hop count is the cheap one.
3. **Ring failure on BOTH sides** ends a branch, and a branch that ends is
   not necessarily a map with no way - it is this ring size giving up. Worth
   logging which, the way the catalogue now separates a cap from a proof.


---

## Changelog

- **2026-09-12 (second pass)** — branch at EVERY reached point, not only at
  collisions. Each point carries the angle that reached it; each angle has an
  id; a per-point hit list of tried angles tagged PASS or FAIL blocks re-casts.
  Backing up asks whether a point came from a tangent or from a continuation,
  and a continuation is still a branch point. Written after the first trial run
  failed: 4,000 rays, 0 paths, branching factor 1.00.
- **2026-09-12** — spec written back from the owner's description and agreed:
  heading held between collisions and re-aimed at the base only at a tangent;
  both sides of every ring become branches; a completed path claims
  `(object, side)` so another route may round the same object the other way.
