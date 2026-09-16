# nuTerra — working rules for Claude

Read `docs/README.md` first: it indexes every reference and handoff, and its
"House rules" section (measure, then claim; run the null control; shaders
validate at runtime only; shader source is `nuTerra/shaders/`) applies to
everything below.

## The repo is shared

Two Claude sessions (Opus and Fable) work this same `C:\nuTerra` checkout at
the same time, with the owner.

- `git status -sb` and `git log --oneline -5` before acting; dirty files that
  are not yours belong to the other session. Never `git add .`, never stage,
  stash or clean them.
- Pull before pushing when behind. The agent shell has no SSH key, so a
  `git fetch` from it fails - say so rather than assume the branch is current.
- Work on `master` in this checkout - no worktrees, no side branches - so the
  IDE build carries the change. A worktree under `C:\experiment` loads a
  different project's `CLAUDE.md`.
- `nuTerra/cam_paths/*.campath` are committed with the work that made them;
  never hand-edit one.
- No subagent fan-out or Workflow runs on this repo.
- Three sessions as of 2026-09-11 evening: Flight Studio (this file's owner)
  and nuTerra work (Tanks, render, bake) share THIS checkout on `master`;
  Tank AI (`docs/HANDOFF_2026-09-11_tank_ai.md`) works by the owner's
  decision in its OWN clone, `C:
uTerra_tankai`, branch `tank-ai`, its own
  build and its own running exe - it fetches from `C:
uTerra` and never
  pushes into it, so nothing it does can land here except by a merge the
  owner performs. The "work on master here" rule above governs the two
  sessions in this tree, not that one.
  EVERY nuTerra instance, whichever tree it was built from, writes its
  flight bake to `%TEMP%
uTerralight` (per USER, `MapFlightBake.vb:753`)
  - the folder the planners read. A second instance silently replaces the
  bake under the same name with a fresh timestamp; the Tank AI clone
  redirects its TMP/TEMP to stay out of it. Before trusting a bake, check
  its write time against the run that made it; provenance keys in the meta
  (exe, built, written) are requested from the writer.
  Every nuTerra a session launches carries its name in the title bar:
  pass `"owner=Flight Studio"` (QUOTED - unquoted it splits into two arguments)
  on the command line; the tag reads straight off
  `Environment.GetCommandLineArgs()` and shows as `nuTerra - <map>   [tag]`.
  The owner asked for this with three identical windows on one desktop
  (2026-09-11). Unset, it falls back to the checkout folder the exe sits in.
  And stop a nuTerra by its exe PATH, never by process name - with two
  checkouts live, a name match kills the other session's app.
  THE JOBS, as the owner split them (2026-09-12, night): nuTerra Work -
  fixes in the engine, the main nuTerra UI, offering up knowledge, and
  producing the HEIGHT MAP and the COLOUR TYPE (the bake, the kind
  classification, the palette: a data product the other two ask for). Tank
  AI - develop the AI path creation, and work with Flight Studio to get it
  rendered. Flight Studio - code Flight Studio, and work with Tank AI to
  develop the rendering. Route CONTENT - what is drawn, to the base, in
  what colours - is Tank AI's and Flight Studio's even when the panel lives
  in nuTerra's UI (the Route resolver panel is plumbing only). Standing
  rules, his words: "If I ask something that isn't in your job list, ask me
  first" and "auto hand off the work if I ask the wrong session and let me
  know" - when the owner of an ask is obvious, forward it and tell him it
  was forwarded; never bounce it back, never build it in another lane;
  where no owner is obvious, ask.
  The protocol that has held all day: NAME THE FILE before editing anything
  that is not yours, wait for the ack, and measure rather than argue. The
  flight bake's contract lives in `<map>_meta.txt` (kind_mask, outland_bit,
  trunk_bit, trunk_radius; solid_bit and stem_min_m once written) and the
  readers in `tools/` take every bit from there - the writer must not move a
  bit without telling the Flight Studio session. The 0.25 m stem threshold was
  measured on the pre-alpha-fix bake and must be re-measured on the current
  one before it is fixed.
- Building from the agent shell WORKS, despite what older notes say:
  `dotnet build nuTerra/nuTerra.vbproj -c Debug -p:Platform=x64
  -p:BuildProjectReferences=false "-p:VCTargetsPath=C:\Program
  Files\Microsoft Visual Studio\18\Community\MSBuild\Microsoft\VC\v180\\"
  "-p:OutDir=C:\nuTerra\nuTerra\bin\Debug\net8.0-windows\\"`
  (v170 works too). `Platform=x64` is REQUIRED - without it the C++ DLL does
  not resolve (`nuTerraCPP.QuadtreeWrap` not defined) - but on its own it
  sends the output to `bin/x64/Debug/`, which nothing launches; the `OutDir`
  puts it where the IDE builds and the launcher looks. A running nuTerra
  locks the exe there: stop it first. The C++ project cannot compile on this machine at all -
  `MSBuild.exe` dies on an assembly mismatch, `dotnet` cannot import the
  .vcxproj - so its DLL is reused as built, which is fine: nobody edits it.
  `dotnet build PathStudio/PathStudio.vbproj` builds the Studio launcher.
  Staleness test: no `.vb/.vert/.frag/.h` under `nuTerra/` outside bin/obj
  newer than `bin/Debug/net8.0-windows/nuTerra.dll`. A commit newer than the
  binary is NOT evidence of a stale build - the other session builds, runs
  and measures before it commits.

## File ownership - no two sessions in one file

Two sessions on one working tree race the moment they touch the same file:
one writes over the other, or one commits a half-edit of the other. So the
tree is split, and a session stays on its side.

| session | owns |
|---|---|
| **Flight Studio** | `tools/`, `PathStudio/`, `nuTerra/cam_paths/`, `docs/*path_studio*`, `docs/camera_flight_plan.md`, `docs/bulb_placer.md`, `CLAUDE.md` |
| **Tank AI** | `nuTerra/Tanks/`, `tank_tools/` (in its own clone until the owner merges) |
| **nuTerra Work** | everything else under `nuTerra/`, and the rest of `docs/` |
| shared | `nuTerra/Modules/modGlobalVars.vb` - name the change, tell the others |

Agreed between the three sessions 2026-09-12 at the owner's instruction
("talk to the sessions and decide on what areas each will handle"). ONE
AUTHOR PER FILE - a two-author file is what this table exists to prevent.
Flight Studio owns no VB file: what it draws of a route it draws in Python on
its own canvas; route content in nuTerra's window is Tank AI's. The bake:
reading it as data and deriving geometry from it is the reader's; what a
kind MEANS, what is in a bin, the palette and the writer are nuTerra Work's
- ask for a bit or a key, never infer a classification from names.

Crossing the line - the flight bake (`nuTerra/Scene/MapFlightBake.vb`) is the
one file both sides care about - is done by MESSAGE, not by editing: say what
you need changed to the owning session (the desktop app can message a session
by name), and wait for it. Never edit a file that shows as modified in
`git status` and is not yours. Commit only by explicit path.

## Always start the thing you just built

- **Flight Studio** (`tools/*.py`): when an edit is done, kill every
  `python.exe` running `path_studio.py` and start a fresh one from
  `C:\nuTerra\tools` (`python path_studio.py`, detached). No question - the
  Studio imports the tools once at launch and the edit is invisible until
  then. Say it is up.
- **nuTerra** itself: after a built change, kill any running nuTerra and
  launch the exe with `19_monastery` for inspection.
- The owner's own running nuTerra instance is his: ask before stopping it
  unless he has handed the helm.

## Flight Studio

The app is the Python in `tools/`; `PathStudio/Program.vb` only launches it.
`radar_commit.py` is the navigator and its docstring is the authority on the
flight rules; `docs/camera_flight_plan.md` is the design. `tools/lane_test.py`
is the harness that measures a navigator change (step 4b there). The 3D view
is a pygame + PyOpenGL window (`GLView`), optional - without them the PIL
`View3D` serves. Start the Studio on a map with `python path_studio.py
19_monastery`.

To reach the other session, use the session tools: `list_sessions` to find
it by title (`nuTerra work`), `send_message` to deliver a user turn there.
That is how a change to a file the other side owns is asked for.

### The map picker

The owner's rule: nothing happens to the map list after a name is clicked. It
signals everything downstream and is never gone back to. The only thing that
leaves either picker is a map NAME.

Six bugs got it there and `docs/tk_event_traps.md` has all of them. Four came
from code with no business near a picker, and the last one was not our code at
all - it was Tk's `<B1-Motion>` class binding walking the selection while the
button is down.

Do not fence it off - that was tried and removed. Test it instead, the way a
person uses it: `python tools/picker_click_test.py` clicks 25 rows with the
pointer drifting after every press. A clean press was never the failure, so a
clean test proves nothing.

### Testing the Studio: no mouse, and take the lock

Never test by driving the pointer or the keyboard - no synthetic clicks,
scrolls or keystrokes, and never bring a window to the front to do it. The
owner is at the machine. A click meant for the Studio has landed in a browser,
and a wheel event over the map list fired two real map loads and left a modal
open; a harness misfire like that reads exactly like an app bug and has cost
hours.

Drive it IN-PROCESS instead. `tools/live_trace_probe.py` is the worked
example: import `path_studio`, build `Studio(root)` on a Tk root parked at
`+4000+4000`, call the methods a click would call, and pump `root.update()`.
To capture the canvas, wrap `ImageTk.PhotoImage` and keep the PIL image it is
handed - that is the exact frame, no screenshot, no focus taken.

And take the lock while you have it:

```python
studio.set_test_lock(True, "why")   # ...work...   set_test_lock(False)
```

or `PS_TEST_LOCK=1` / `--test-lock` at launch. It strips every widget's
bindtags, so no binding fires at all - the widget's own, or its class's - the
title says `LOCKED, a test has it`, and unlocking puts every tag back.
`after()` callbacks are untouched, so a trace already running carries on.
`python tools/test_lock_test.py` proves it holds and lifts.

## Measurement

A still: `nuTerra.exe 19_monastery cam=... freezefx still=1 out=<dir>`, one
frame to `<dir>\still\still_NNN.png`; same build twice must be 0 px. Keep
`out=` off the owner's `G:\nuTerra_ScreenCaps`.
