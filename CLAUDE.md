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
is the harness that measures a navigator change (step 4b there).

## Measurement

A still: `nuTerra.exe 19_monastery cam=... freezefx still=1 out=<dir>`, one
frame to `<dir>\still\still_NNN.png`; same build twice must be 0 px. Keep
`out=` off the owner's `G:\nuTerra_ScreenCaps`.
