# Handoff — Path Studio: the lane navigator, the lock, the lamp-shaped lights

2026-09-09, on `master`. Written by Fable (Opus started the day). Scope was
**Path Studio only** — `tools/*.py` — on the owner's instruction; nothing in
`nuTerra/` was touched by this work. Fable's other session landed the Tanks
and Shader IDE commits alongside.

Claims are marked. **VERIFIED** means measured or run. **REASONED** means
derived and not seen.

---

## 1. What is committed

Three commits on top of `48ec2f78`:

1. **Navigator** — `tools/radar_commit.py`, `tools/lane_test.py`,
   `docs/camera_flight_plan.md` (step 4b).
2. **Path Studio + light record** — `tools/path_studio.py`,
   `tools/cam_path.py`, `docs/bulb_placer.md`, the owner's regenerated
   `nuTerra/cam_paths/19_monastery.campath` and his `VM_FOG_Curve_0.png`.
3. **`CLAUDE.md`** — the repo had none; the rules the owner gave across
   sessions, in the checkout that actually loads them.

Not pushed. The owner pushes, after a pull — the agent shell has no SSH key
and cannot fetch.

## 2. The navigator (`tools/radar_commit.py`)

The ask: "it won't fly between narrow spaces that the tanks can drive
through … if we can't go forward, back up 2 locations and turn at step
angles; make the step angle slight."

**VERIFIED: there is no terrain-grade constraint anywhere in the flight
path.** The only slope logic (`SLOPE_TOL`) *forgives* phantom obstacles on
hillsides. Bare terrain never blocks at 1 m AGL. Three gates stack against a
lane and only the third is the navigator's decision-making:

| gate | | changed |
|---|---|---|
| A* nominal course, `flight_plan.build_cost` | 2.7 m grid, max-pooled, dilated, charged `14/(d+1.5)+26/(d+1)` for wall closeness — seals < 5.5 m and routes *around* a lane when a wider way exists | no |
| `BODY_R` (Standoff slider) | 6 m needs a 12 m lane | no |
| trap rule far probe | a straight 22 m line; a lane bending inside it read as a pocket | **yes** |

Measured on the monastery at 2 m standoff: 16 % of free space is in
corridors ≤ 6 m.

**What changed:**
- **The bend** — when the straight far probe hits an object, a continuation
  is tried from the near point out to `BEND_MAX` 75° in 15° steps for the
  rest of the distance. A pocket still has no continuation.
- **Slight turns** — heading moves ≤ `TURN_STEP_DEG` 8° per 2 m step (14 m
  radius); the step is tested on the heading actually flown. Before, it
  snapped to any bearing in one step and never tested the step.
- **Back up 2 and turn** — when the step does not fit, back up
  `BACKUP_STEPS` 2 locations of its own track, turning one `TURN_STEP` as it
  goes (a reversing arc), retry. Backed-over points are dropped, so the path
  bends as sharply as the corner needed and no sharper. The per-step cap
  escalates one step per `BACKUP_ESCALATE` 4 fruitless backups and decays one
  per `BACKUP_DECAY` 3 fitting steps — escalating every backup and decaying
  every step oscillated 8°/16° forever (1872 backups on one course).
- The 180° "reverse out" when boxed is now a backup along the track.
- `backups=` in the summary line; amber dash per backup in the picture.

**VERIFIED, `tools/lane_test.py` (four straight-through-village courses,
2 m standoff):** before 2/4 closed, 3236 reversals; after 3/4 closed, 0
reversals; the course that never closed (12 000 m) closes at 420 m with 10
backups. Course 3 is a straight line through a tree cluster and fails before
and after (933 backups, 338 boxed) — not a regression, not fixed.

**VERIFIED, the cost:** the shipped monastery route (`radar_commit.py` on
`19_monastery_plan.csv`) still closes, clips 0, **but deviates 26 m** from
its nominal at the hook after the departure leg (was 4 m), with 0 detours and
0 backups — a 14 m radius cannot follow that hairpin, so it overshoots and
comes round. Sweep of `TURN_STEP_DEG` × `LOOKAHEAD` (8/10/12 × 14/20/26):
12° only brings it to 15 m; on the lane courses 8/14 was as good as any
combination. Constants left at 8/14. A route drawn without a hairpin does
not show it. Not re-tuned against the owner's eye.

## 3. Path Studio (`tools/path_studio.py`)

All on the owner's instructions, in this order:

- **Clear targets** clears the start and heading too; **Backspace/Delete**
  backs all the way out (targets, then the start). A pre-existing crash on
  popping a selected target is fixed alongside.
- **Edit path** — a lock, default ON. Every path control (three sliders,
  Clear, Generate) and every map click/drag/key that would place or move the
  start or a point is greyed or ignored until it is pressed; loading a map
  locks again. Lights are outside the lock.
- **Buttons** blue-grey (`BUTTON` `#3f4d68`); disabled is a darker flat face
  with dim text (`BUTTON_OFF`).
- **Map list** 5 rows, a live search box above it, the "Maps with a bake"
  label gone. Empty = the arenas; typed text filters across every space
  including the "other" dropdown's.
- **Notes** moved to the right panel, anchored at the bottom under the lamp
  controls (a weighted spacer row).
- **Lights mirror the Light Bulb Placer.** The panel sliders are gone. Add
  Light → the cursor is a light bulb (`bulb.cur`, drawn once into the flight
  folder by `_write_cur`, classic BMP format because Tk loads cursors through
  the OS); each click drops a light and opens its **editor window**
  (`LightEditor`): type radios (point / cone / inverse cone / dual cowl), aim
  as x y z numbers, the two half angles with the Placer's exact labels and
  ranges (cap cut 0–179 / base cut 1–180 + edge blend for dual; inner 0–89 /
  outer 0.5–89.5 for cone and inverse, `cone = 2·outer` kept truthful),
  colour swatch, level, range, height over ground, fog mix, shaft curve
  radios, **Curve editor…**, **Show shape** (`ShapeView`: side elevation
  through the aim axis — cone edges + lit wedge, inverse cone as the blue
  dark wedge, dual cowl as the lit band with green rims, range circle,
  ground line; redraws live). **OK** applies and closes, **Save** applies
  and writes the `.campath` and stays open, **Cancel**/X discards — asking
  if anything changed. Add Light again, or Esc, closes it the same way and
  turns the mode off. Shift-click a light, or **Edit light…**, opens the
  editor on it. Backspace/Delete removes the selected light in any mode, the
  last placed one in Add Light mode, and works from the editor window too
  (not while typing an aim number).
- The editor works on a **copy**; nothing reaches the light until OK or
  Save.

`tools/lane_test.py`-style headless smoke test of all of it lived in the
session scratchpad and is not kept; the checks were: record round trip,
`copy_with_lights` keeps the new fields, window starts locked, search
filters, Edit path toggles the lock, a light placed through the mode gets an
editor, kind/angles/shape view/OK apply, mode off. All passed.

## 4. The `.campath` light record (`tools/cam_path.py`)

36 → **72 bytes**, appended after `curve`: `kind u32, aim x y z, cone,
blend, ang0, ang1, vol_mix`. **Aim is an OFFSET from the light in metres**
(not a point), so a light moves without re-aiming; readers normalise.
`LIGHT_FMT "<8fII3f5f"`, `LIGHT_FIELDS 18`, `light_bytes()` is the one
writer. Readers go by `light_stride`; a 36-byte file reads back as a point
aimed down with `vol_mix` 1. The docstring is the authority.

**OPEN — `MapCamPath.vb` does not read the new fields.** It reads the first
36 bytes by stride, so nuTerra still sees every map light as a point aimed
down: nothing regresses, the shapes do nothing in the scene. The one VB
change: when `light_stride >= 72`, take kind, dir (normalise the offset),
cone, blend, ang0, ang1, vol_mix from offset 36. Out of scope today by the
owner's instruction; noted in `docs/bulb_placer.md`.

## 5. The owner's monastery save

`nuTerra/cam_paths/19_monastery.campath` in commit 2 is his: regenerated
with the new navigator (388 points; HEAD had 396), one point light
(level 0.32, range 9.4 m), the 2 bulbs carried through. `VM_FOG_Curve_0.png`
is his curve-editor save. Committed so nothing is lost; whether the route is
the keeper is his call.

## 6. Open

1. `MapCamPath.vb` reader for the 72-byte light (section 4).
2. The 26 m hairpin deviation on the shipped route (section 2) — course
   shape vs. slight turns; the owner has not judged it by eye.
3. Lane course 3 (tree cluster) never closes, before or after.
4. The A* nominal course still routes around lanes (gate 1); the navigator
   only gets to use the lane work when a target is placed in one and the
   route reaches it.
5. Tk cursor: `bulb_cursor()` falls back to `crosshair` if the `.cur` fails
   to load — not seen failing, not verified on another machine.

## 7. Etiquette carried forward (now in `CLAUDE.md`)

- Path Studio only, when told so — and kill/relaunch the Studio after every
  edit without asking.
- Two sessions on one checkout: check state before acting, never stage the
  other's files, pull before push.
- Bash heredocs with apostrophes fail in this harness; the Write tool with a
  Python patch script of exactly-once anchors is the reliable way to land a
  large edit.
