# Tank Docs

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
