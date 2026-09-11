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
- Building from the agent shell WORKS, despite what older notes say:
  `dotnet build nuTerra/nuTerra.vbproj -c Debug -p:Platform=x64
  -p:BuildProjectReferences=false "-p:VCTargetsPath=C:\Program
  Files\Microsoft Visual Studio8\Community\MSBuild\Microsoft\VC180\\"`
  (v170 works too). The C++ project cannot compile on this machine at all -
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
| **Path Studio** | `tools/`, `PathStudio/`, `nuTerra/cam_paths/`, `docs/*path_studio*`, `docs/camera_flight_plan.md`, `docs/bulb_placer.md`, `CLAUDE.md` |
| **nuTerra** | everything under `nuTerra/` except `cam_paths/`, and the rest of `docs/` |

Crossing the line - the flight bake (`nuTerra/Scene/MapFlightBake.vb`) is the
one file both sides care about - is done by MESSAGE, not by editing: say what
you need changed to the owning session (the desktop app can message a session
by name), and wait for it. Never edit a file that shows as modified in
`git status` and is not yours. Commit only by explicit path.

## Always start the thing you just built

- **Path Studio** (`tools/*.py`): when an edit is done, kill every
  `python.exe` running `path_studio.py` and start a fresh one from
  `C:\nuTerra\tools` (`python path_studio.py`, detached). No question - the
  Studio imports the tools once at launch and the edit is invisible until
  then. Say it is up.
- **nuTerra** itself: after a built change, kill any running nuTerra and
  launch the exe with `19_monastery` for inspection.
- The owner's own running nuTerra instance is his: ask before stopping it
  unless he has handed the helm.

## Path Studio

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
