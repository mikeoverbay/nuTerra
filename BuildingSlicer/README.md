# BuildingSlicer

Standalone scanner for the buildings in the World of Tanks packages, so building
work can be done without starting nuTerra and waiting for a map to load.

It shares no code with nuTerra on purpose — the same rule `SrtViewer` follows.
`PackedSection.vb` here is a reference implementation of the `.model` container
and can be proven out in seconds before anything is ported into the engine.

VB + OpenTK, `net8.0-windows`, x64.

## Running

    BuildingSlicer                                   scan and summarise
    BuildingSlicer --list                            every building, one line each
    BuildingSlicer --list --filter cathedral
    BuildingSlicer --asset hd_bld_eu_225_cathedral   every LOD and part
    BuildingSlicer --csv buildings.csv               one row per part
    BuildingSlicer --failures                        anything that would not parse
    BuildingSlicer --skip-vehicles                   skip vehicles_/audioww- packages
    BuildingSlicer --game "C:\Games\World_of_Tanks_NA"

The game install is auto-detected from the usual four locations.

### Building it

    dotnet build BuildingSlicer/BuildingSlicer.vbproj -c Debug

**Do not pass `-p:Platform=x64`.** nuTerra needs it to resolve its C++ DLL, and
`CLAUDE.md` says so for that project — but this one is pure managed and already
sets `<PlatformTarget>x64</PlatformTarget>`. Passing it anyway sends the output
to `bin/x64/Debug/` instead of `bin/Debug/`, which is not where anything looks.

## What counts as a building, and why it is not a guess

The game keeps its buildings in one folder, `content/buildings`, and names every
one of them with a `bld_` or `hd_bld_` prefix. Those are two independent
signals, and they were checked against each other over the whole shipped
library:

| | |
|---|---|
| asset folders under `content/buildings` | 325 |
| assets named `bld_`/`hd_bld_` in *any* package | 325 |
| named `bld_` but living somewhere else | **0** |
| under `content/buildings` but named something else | **0** |

The two sets are the same set. So the folder IS the answer and the prefix is a
second witness, and neither has to be trusted on faith — both are re-measured on
every scan and any disagreement is printed. If a patch ever ships a building
somewhere else, the scan says so instead of quietly missing it.

This is worth stating because the other way to answer the question is a
substring race — testing a path for "house", "church", "ruin" and so on, which
is what `nuTerra/Scene/MapFlightBake.vb` has to do because a map model carries
nothing but its folder name. That approach classifies `StreetLamp` as a **tree**
(s-t-r-e-e-t), and cannot decide whether `hd_bld_UNI_006_KitCrashFactory` is a
factory or the crash debris. None of it is needed here.

## Path shape

    content/buildings/<asset>/<state>/lod<N>/<part>.model          mesh
    content/buildings/<asset>/<state>/lod<N>/havok/<part>.hkt.model  collision

`<state>` is `normal` on all 4,962 files — no `damaged` or `destroyed` variants
ship as folders. Destruction is a *part*, not a state: assets carry meshes named
`..._crash` beside the intact ones, and those are usually the `nodefull` ones.

The **69 `havok/` files are collision proxies, not geometry**, and they are the
entire difference between the 4,962 `.model` files in the folder and the 4,893
that are building meshes. They are matched explicitly rather than left to fall
through as unrecognised: a proxy counted as a mesh inflates every part count,
and a proxy reported as an anomaly hides a real one. Their names carry the
material — `__n_stone0_1`, `__n_metal4_1_2`. The amphitheatre ships 20, one per
column and ruined column.

## What the scan finds

    packages  218 scanned, 0 skipped
    entries   525,483 seen, 286,265 indexed
    index in  ~1,900 ms
    scan in   ~215 ms

    assets           325
    mesh models      4,893   (4,893 parsed, 0 unparsable)
    havok proxies    69

    LODs per asset   8 assets have 3, 168 have 4, 149 have 5
    parts at lod0    180 assets are a single mesh, 145 are multi-part (up to 46)
    visibility box   2,918 models carry one, 1,975 do not
    node trees       1,927 models are nodefull (animated), the rest nodeless

Every package is scanned, vehicles and audio included. Skipping a package
because its name suggests it holds tanks is assuming the answer to the one
question this tool exists to ask — so the default asks it properly, and
`--skip-vehicles` is there once you have the evidence.

That evidence now exists: skipping the 41 `vehicles_`/`audioww-` packages drops
the index from 525,483 entries to 254,000 and halves the time (1,900 ms to
~900 ms) while returning **the identical 325 assets and 4,893 models**. No
building lives in a vehicle package. Use the flag freely; the full scan is what
re-proves it.

The largest things in the library, by lod0 footprint:

|  | wide | tall |
|---|---|---|
| `hd_bld_eu_055_mountaindam` | 612.8 m | 206.1 m |
| `hd_bld_eu_083_bhouse` | 180.1 m | 65.2 m |
| `hd_bld_uni_034_constructionsite` | 148.2 m | 32.2 m |
| `hd_bld_eu_211_lighthouse` | 136.8 m | 83.5 m |

The dam is three parts spanning a valley, which is why its footprint is far
wider than any single mesh in it (215 m).

## The `.model` file

A `.model` is a **packed section** — BigWorld's binary form of what was XML
before it shipped. Same container as `.visual_processed` and the rest of the
`_processed` family, and `nuTerra/ResMgr/packed_section.vb` reads the same
format.

    u32      magic 62A14E45
    u8       version
    strings  the dictionary, NUL terminated, ended by an empty string
    element  the root

and an element is

    i16      number of children
    u32      own data descriptor
    i16+u32  one name index + data descriptor per child
    bytes    the data blobs, back to back

A descriptor packs an **end offset** in its low 28 bits and a **type** in its
top 4. The offsets are ends, not starts, and they are relative to the first
blob — so a blob's length is its own end minus the previous end. There are no
start offsets anywhere in the file: read the blobs in order or not at all.

Types are `0` element, `1` string, `2` int, `3` floats, `4` bool, `5` blob.

Four things are worth knowing:

* **The dictionary is bytes, not text.** Reading it through a `BinaryReader`
  decodes every byte through an `Encoding`, so anything above 127 comes back as
  U+FFFD or eats the byte after it. This reader walks a `Byte()` with its own
  cursor and decodes Latin-1, so every byte survives and a name compares equal
  to the one in the file.

* **A vector is not always floats.** Most files store `min`/`max` as three
  float32, but some store the identical value as *text* —
  `"-8.299856 -0.950243 -4.354095"`. A reader that only handles the float form
  silently drops those. Measured, not hypothetical.

* **The box key has two spellings and so does the visual.** The box is
  `visibilityBox` on almost every file and `boundingBox` on three of them. The
  visual is named by `nodelessVisual` for static geometry or `nodefullVisual`
  for anything with a node tree — and three files spell that one
  `nodefullvisual`, so the lookup is case-insensitive.

* **About 40% of models carry no box at all** (1,975 of 4,893). That is normal,
  not a parse failure, which is why `HasBox` exists rather than a zero box.
  Where it is missing is worth knowing:

  | | carries a box |
  |---|---|
  | lod0 | 1,042 of 1,189 — 87.6% |
  | lod1 | 589 of 1,156 — 51.0% |
  | lod2 | 531 of 1,115 — 47.6% |
  | lod3 | 488 of 1,007 — 48.5% |
  | lod4 | 268 of 426 — 62.9% |
  | nodeless | 1,291 of 2,966 — 43.5% |
  | nodefull | 1,627 of 1,927 — 84.4% |

  So lod0 is well covered and the rest is a coin flip, but the stronger signal
  is the node kind, not the LOD: an animated `nodefull` model almost always
  carries one. `hd_bld_eu_225_cathedral` shows both — 15 of 16 parts boxed at
  lod0, and at lod1 only `tower_02_crash` and `tower_03`, which are exactly two
  of its three `nodefull` parts.

  Do not read a missing box as a missing mesh. If a box is needed for every
  part, it has to come from the geometry, not from the `.model`.

### How the parse is checked

Two independent readers, same numbers. The scanner's `--csv` output was diffed
against a separate Python decode of all 4,893 models:

    rows not found by python : 0
    has_box disagreements    : 0
    box component diffs >1mm : 0
    largest component diff   : 0.000055 m

That last figure is float32 surviving a round trip through the CSV's four
decimal places. If the two ever disagree by more than that, the descriptor
arithmetic or the Latin-1 decode is wrong — not the file. Check it before
believing anything else the tool says.

A spot check that does not need a second reader: `hd_bld_eu_225_cathedral`
reports its `tower_03` at 80.14 m and `tower_01` at 75.75 m, against a whole
asset 82.09 m tall. A cathedral with an 80 m spire is the right order of
magnitude; a stride or offset error does not land there by accident.

## Not done yet

The slicing. This is the scan half — it finds the buildings, groups them by
asset, LOD and part, and reports what each one is and how big. Reading the
actual geometry means following `nodelessVisual` into `.visual_processed` and
`.primitives_processed`; that format is already written up in
`docs/primitives_reader.md`, and the section table, the BPVT 136-byte preamble
and the two coordinate flips are the traps waiting there.
