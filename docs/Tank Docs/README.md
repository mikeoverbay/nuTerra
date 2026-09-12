# Tank Docs

> **Status 2026-09-12: these three papers describe milestone 1 (2026-09-09) and
> stop there.** The module has since taken 28 commits on `master` from the Tank
> AI session: the Tank Exporter shader port, skinning, running tracks and wheels,
> thirty tier-ten vehicles on the two bases, ID cards, firing with recoil, flash,
> tracer, impact and smoke from the game's own tables, a loading panel, the
> navigation grid and the self-driving. `01_tank_module_design.md` §11 lists what
> that changed against the paper; the navigation and driver are in
> `..\HANDOFF_2026-09-11_tank_ai.md`. The formats in `03_formats_verified.md` are
> unaffected and still hold.

Papers for the tank-loading module, kept apart from the map renderer's docs the
way the code is kept apart from its core.

| document | what |
|---|---|
| `01_tank_module_design.md` | the design paper: what it is, the isolation rules, the data as read off the packages, the raw-buffer loader, placement and teams, the milestones, the file plan |
| `02_loader_notes.md` | (to come with the code) what each file does |
| `03_formats_verified.md` | (to come with the code) every byte-level fact about the tank files, with the file it was read from |

Sources studied: the VB Tank Exporter at `C:\!_Tank Exporter\!_Tank Exporter`
(`Modules\ModTankLoader.vb`, `shaders\tank_fragment.glsl`), and Tank Exporter
PY at `C:\experiment` (`tankExporterPy\loaders.py`, `VISUAL_PROCESSED_FORMAT.md`,
`docs\`). Test vehicle: A88_M53_55.
