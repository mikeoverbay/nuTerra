# Handoff - Path Studio session, 2026-09-12 (to a new session)

Written at the end of the Fable session's budget. Read `CLAUDE.md` first: it
carries the three-session areas table, the two standing rules, the build
recipe, the bake contract and the shared-temp trap. Then
`HANDOFF_2026-09-11_path_studio_3d_keyed_bake.md` (14 sections, the full
record of 09-11 and the night of 09-12). This file is only what is NEW or
IN FLIGHT since that one.

## Who you are

The **Path Studio** session: `tools/`, `PathStudio/`, `nuTerra/cam_paths/`,
`docs/*path_studio*`, `docs/camera_flight_plan.md`, `docs/bulb_placer.md`,
`CLAUDE.md`. No VB file is yours. The other two sessions are **nuTerra Work**
(engine, UI, the height map and the colour type - id
`local_314e5d2c-71d6-4849-8742-1190957297a7`; their peer list shows YOU as
your worktree directory, tell them your title) and **Tank AI** (the AI path
creation, in its own clone `C:\nuTerra_tankai`, branch `tank-ai` -
`local_43e3ae0d-bdc8-4d53-b6c0-33eba4185074`). A fourth, "Bloom and Bulbs
handoff" (`local_bcacbd18-ff0c-4578-b47a-f3250c5819c1`), does the Shader IDE
and engine fixes and owns `docs/README.md`.

The owner's rules, his words: "If I ask something that isn't in your job
list, ask me first" and "auto hand off the work if I ask the wrong session
and let me know." "Close the app when you finished" - and relaunch the
Studio after every tools edit. Launch nuTerra with `"owner=Path Studio"`
(quoted) and stop it by exe PATH, never by name. Never `git add .`; commit
by explicit path. Measure; hand visual judgement to the owner. Write into
`C:\nuTerra` with Bash/python (the Write tool refuses the base checkout
from this worktree; every edit today went through patch scripts in the
scratchpad - see the 09-11 handoff for the pattern and the CRLF/`'''` traps).

## State of the tree (master, `C:\nuTerra`)

Everything of ours is committed through `69b0c34e` except the module below.
Path Studio is open on 19_monastery (pythonw 32480) with the height-map
watcher. No nuTerra of ours is running.

Landed today: the height-map watcher (a re-baked map reloads in place, route
and lights kept); the bake-carried palette (`kind_N_rgb` from the meta,
`bake_kind_rgb()`); provenance shown on load/reload (`written`, `commit`,
`built in <tree>`); zoning tried, measured worse on both sides and removed
(reader and overlay gone; numbers in the 09-11 handoff, section 11); the
point-to-point routing measurements ("plan with the search, drive with the
rays", handoff section 14).

## IN FLIGHT: route identity by signature - `tools/route_sig.py`

The owner: "we need a test to find out when a path was a winner so we can
stop trying it over and over. Make a list of all objects hit in the path."
Two routes are the same route when they pass the same obstacles on the same
sides (the homotopy class). The Tank AI session built it on the ground and
its count of distinct routes agreed with their Lazy Theta* catalogue (8 and
8) - the cross-check to keep. The owner on the Studio side: "that is your
call. Goal is to learn what works.. try and try again."

`tools/route_sig.py` is written, smoke-tested on the real monastery, and
committed WITHOUT being wired into the Studio:

- `label_objects(bake, raw)` - components of the navigator's blocked mask
  (`build_world`'s `raw`, 2048) split by KIND, trunk stamps as their own
  objects. Monastery: 14,736 objects, largest 54,610 m2 (the cliff band),
  0.9 s.
- `signature(path, objects, bake, reach_m)` - {object: L | R | both} for
  every object within `reach_m` of the route, by the sign of
  (direction x offset) over every cell seen from every sample. The shipped
  route with reach 6 m passes 450 objects, L/R/both 234/204/12, 0.1 s.
- `flipped(sig)` - swap hands; a FORWARD route compared with a REVERSE one
  must go through this first, or every object differs (measured: 179 do).
- `compare(sig_a, sig_b, objects, bake)` - objects taken on different sides,
  with a centroid to ring, largest first; objects under 2 m2 ignored;
  objects passed by one route only are a near-miss, not a difference.
- `same_route(...)`.

To finish, in this order:
1. Wire it into `path_studio.Studio.apply_direction` (~line 5990): after
   `self.diverge = divergence(...)`, build the objects once per bake
   (cache on the Studio; `build_world(self.bake, None)[0]` is `raw`),
   signature both routes, flip the reverse one, and keep
   `self.sidediff = compare(...)`. Draw them as rings on the canvas beside
   the divergence rings (the draw is at ~line 4040; use a different colour
   and label the kind, `BAKE_KIND_NAMES`) and as points in the 3D view
   (`GLView.build_overlays`, beside `st.diverge`). Put the count in the
   status line at ~line 5984: "N objects passed on different sides".
2. Test on a synthetic bake (the pattern in the scratchpad tests of the
   09-11 handoff: a keyed rgba8/r16 bake under the real monastery meta,
   floor encoded as (0 - offset) * scale, never raw zeros): two routes round
   one building on opposite sides -> one difference at the building; the
   same route reversed, flipped -> none.
3. The "over" side for the camera is not needed: what the gate flies over is
   not in the blocked mask, so it never appears. Keep the reach at the
   navigator's standoff (`BODY_R`) plus a few metres, not a free number.
4. Tell the Tank AI session what the monastery signatures look like from
   the air; they asked. Render ids in the bake (nuTerra's) would replace the
   kind split; the 2,581 rock-keyed-as-tree cells should land first.

## The bake contract has a reference now

`docs/flight_bake.md` (nuTerra Work, 2026-09-12) is the format three sessions
read: the layers, the key byte, the palette and provenance keys, the kind
classifier's substring-race trap, the per-kind area table. Read it before
touching a reader. Coming in ONE bake-version bump, both from nuTerra: the
canopy-over-rock key fix and PER-OBJECT IDS in the bake (the owner: "render
ids ... so we know what is what on the map. colors is not enough") - a
second render target, a `<map>_ids.u16` layer of ~134 MB and an id ->
model-name sidecar. When it lands, `route_sig.label_objects` should take
the ids as the objects instead of kind-split components, which is what the
kind split stands in for today.

## Open with the other sessions

- nuTerra Work: the canopy-over-rock key (a 1.7 m rock under a 2.5 m bush
  is keyed TREE and flown through - now the owner's decision), the arena
  bounds keys (`arena_x0/x1/z0/z1`, agreed, unwritten), the `no_draw` groups
  on the table and stone fence (665 of 885 triangles undrawn). A new bit in
  the key byte is a contract change; they will tell you first.
- Tank AI: the joint line to the owner - plan with the search, drive with
  the rays; the ring resolver stays drawn until he says which ring he meant.
- Shader IDE session: `docs/README.md` line 16 understates the 09-11
  handoff; the replacement row was sent to them (README is theirs). Ask them
  to add a row for THIS file too.
- The shipped `19_monastery.campath` clips foliage the old bake could not
  see (11 of 2,110 points, worst 4.8 m short at (154, 113)); regenerate it
  in the Studio on the current bake before it is flown.
