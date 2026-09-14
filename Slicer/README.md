# Slicer

Pulls buildings out of the World of Tanks packages and writes them as STL or
OBJ, so they can be printed or opened in anything.

Standalone, the same rule `SrtViewer` follows: it shares no code with nuTerra,
so it runs without starting the engine and its format readers stay honest
reference implementations. VB + OpenTK, `net8.0-windows`, x64. **One
dependency, OpenTK** - every mesh routine here is written in this folder.

The name is historical. It began as a plane-slicer and the cut is still in
there as an inspection aid (`S` in the viewer), but the job is export: produce
a clean mesh and let a real slicer - OrcaSlicer, PrusaSlicer - do the slicing,
the supports and the G-code. That is their work and they are good at it.

## Running

    Slicer --export                                  write every building as STL
    Slicer --export --asset cathedral --out models    write one, somewhere
    Slicer --export 20 --set out.format=obj          the first 20, as OBJ
    Slicer --view                                    the 3D viewer
    Slicer --view --find "*eu*thouse*roof*"          open on the first match
    Slicer --check                                   watertightness sweep
    Slicer --list                                    every building, one line each
    Slicer --show-settings                           print the settings and exit
    Slicer --game "C:\Games\World_of_Tanks_NA"

The game install is auto-detected from the usual four locations.

## Export

    out.dir      exported
    out.format   stl          stl for a printer, obj to keep vertex sharing
    out.upAxis   z            z for printing, y to keep the app's own axis
    out.scale    1000         metres -> millimetres

**Two conversions happen on the way out and both are silent when wrong**, because
a wrong one still produces a valid file.

**Up axis.** This app works Y-up, like the game and like OpenGL. Every printing
tool works Z-up, because the build plate is the XY plane. Export Y-up and the
building arrives lying on its side - still valid, still printable, just on its
side. The rotation is about X: `(x, y, z)` becomes `(x, -z, y)`.

**Units.** These models are in METRES - a house is 13 units tall. STL carries no
units and every consumer assumes millimetres, so a 13 m house exported raw is a
13 mm ornament. x1000 makes the file's numbers millimetres, so it lands
life-sized and gets scaled down deliberately rather than shrunk by accident.

What each file gets, in this order and for these reasons:

1. every part of the LOD, merged - a building is a kit, and one STL per wall
   panel is not what anybody wants;
2. degenerates killed FIRST, so the bottom-fill walk is not tripped by
   zero-length edges;
3. each part's bottom closed at its OWN lowest point, before the merge, because
   a kit's pieces sit at different heights;
4. up-axis and scale applied last.

STL is written BINARY - same geometry at about a sixth the size. It has no
vertex sharing, so a welded mesh is un-welded on the way out; that is the
format, not the writer. OBJ keeps the sharing and is written with
`InvariantCulture`, because on a comma-decimal machine `1,5` in an OBJ is two
numbers and the file loads as garbage.

**These are not watertight** - see below. Run a repair pass, or let the slicer
do its own, before printing.

### Building it

    dotnet build Slicer/Slicer.vbproj -c Debug

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

## Finding a model

The viewer opens with a panel down the left: a search box, and every `.model`
in the library at **lod 0** - 1,189 of them across 325 assets. Double-click a
row and that model loads.

    /              focus the search box          Tab    hide the panel
    Enter          load the selected row         Esc    clear, then leave the box
    up / down      move the selection            wheel  scroll the list

**`*` is a wildcard and there can be as many as you like.** `*eu*thouse*roof*`
finds the three townhouse roofs; `house` on its own is read as `*house*`,
because that is what someone typing three letters into a search box means. The
match runs against `asset/partname`, so either half works.

The matcher is written out rather than translated into a `Regex`. A model path
is nothing but `_`, `.` and digits, and a Regex translation needs every one of
those escaped - one missed escape silently matches the wrong set, which looks
exactly like a search that works.

### A row loads ONE model, not its asset

This is the point of the panel rather than a detail of it. An asset's lod0 is a
KIT holding all of its interchangeable variants at once - `hd_bld_eu_049_thouse`
has 24 - so loading the whole asset stacks two dozen overlapping walls in the
same cubic metre. That is what made the first OBJ export unreadable, and no
count caught it: the file was well formed, every `usemtl` resolved, every map
existed. It took loading the OBJ back in and looking at it.

Picking a row narrows to the one mesh. The roof above is 880 triangles on its
own against the asset's 20,653. Left/Right (whole asset) and `[` `]` (LOD) both
clear the pick, because a part chosen at lod0 does not exist at lod3 and showing
nothing would be worse than falling back.

### Typing must not drive the viewer

Every viewer hotkey is a bare letter - W, B, P, D, R, F, S, C, X, Y, Z, K, E -
so while the search box has focus the whole key block stands down. Without that,
typing "house" walks through the shell pipeline, the bottom fill, the cut and
the solid/wireframe toggle on the way to filtering the list. The mouse is
deliberately NOT gated: the camera stays live while you type.

Two more places the same care was needed. A drag that STARTS on the panel stays
the panel's until the button comes up, so a click on a row cannot also spin the
model once the pointer crosses into the 3D. And the 3D viewport is INSET by the
panel width rather than drawn full-window underneath it - otherwise the building
centres behind the list, and every framing decision gets measured against a
width that is not the visible one.

### Drawing it with one dependency

There was no text rendering in this app at all. The first attempt was a 5x7
bitmap font authored by hand, to keep the app to its one dependency; it does not
pay - 95 glyphs is 665 rows of art to get right by eye, one wrong row is a
silently ugly character, and the result is worse than what the machine already
has.

`<UseWindowsForms>` gets `System.Drawing` out of the Windows Desktop shared
framework. It is NOT a NuGet package, so the project still carries exactly one
`PackageReference` - which is what that rule was protecting. The app was already
`net8.0-windows` and x64, so it runs on exactly the machines it ran on before.

ASCII 32..126 is rasterised once into a 16x6 atlas. The 96th cell is solid
white, and every untextured quad - panel background, row highlight, scrollbar -
points its UVs at that one texel. So the whole interface is one shader, one
vertex buffer and one `DrawArrays`, with no state to get out of order between a
background and the label sitting on it.

## The viewer

`--view` opens an OpenTK window on the building and reads its real geometry.

| | |
|---|---|
| drag | orbit |
| wheel | zoom |
| left / right | previous / next building |
| `[` / `]` | coarser / finer LOD |
| up / down | solo one part, or back to all |
| `W` | wireframe |
| `R` | reload |
| `Esc` | quit |

Per-load detail prints to the console, which is why the project is `Exe` and not
`WinExe` — a WinExe detaches from the console and every line goes nowhere.

**Normals are derived from the triangles, not read from the file.** The vertex
does carry a packed 8-8-8 normal, but decoding it is a second thing that can be
wrong, and a face normal cannot be — it falls out of the winding, which the X
mirror already had to get right. If the shading looks correct then the positions
and the winding are both right, which is what wants proving first. Lighting is
two-sided (`abs` on the diffuse term): building meshes are open shells with
single-sided walls, and a wall facing away from the key light must not read as a
hole.

## The geometry: `.primitives_processed`

`docs/primitives_reader.md` documents this format and
`nuTerra/ModelLoaders/PrimitiveLoader.vb` is the engine's reader;
`PrimitivesFile.vb` here is a third reader of the same bytes, which is the
point — when two disagree, the file is not the thing that is wrong.

The section table is at the **end** of the file, entries carry **no offsets**,
and bodies are padded to 4 — so a body is located only by summing every size
before it, in order. A BPVT vertex section's body starts at **136**, not 68
(a second 64-byte format string plus 4 bytes); the 132-byte guess matches by
integer-division coincidence and shifts the stream by one float. The index
group table sits **after** the index buffer. Positions need **negate X** *and*
**swap two corners of every triangle** — do one without the other and the model
looks right in silhouette while every face points inward.

### A vertex format the engine cannot read

Buildings ship three vertex formats, and one of them is not in nuTerra's table:

| format | stride | sections |
|---|---|---|
| `BPVTxyznuvtb` | 32 | 4,796 |
| `BPVTxyznuviiiwwtb` | 40 | 392 |
| **`BPVTxyznuvitb`** | **36** | **164** |

The stride is never stored — only the name is, and the name has to be looked
up. So the third one was **derived, not guessed**: a vertex body runs from 136
to the end of its section, which makes `(sectionSize - 136) / count` the stride
exactly. It came out a whole number on all 5,352 vertex sections under
`content/buildings`, and it reproduced 32 and 40 for the two formats the engine
already knows — which is what makes 36 trustworthy for the one it does not. 36
is also what the name predicts: `BPVTxyznuvtb` plus one 4-byte `i` bone index.

Since re-censused over the **whole install** rather than just the buildings -
**121,000 vertex sections** - after the PKG Explorer session flagged a format
this reader did not have. The same arithmetic returns exactly one stride per
format, unanimously:

| format | stride | sections | where |
|---|---|---|---|
| `BPVTxyznuvtb` | 32 | 64,836 | |
| `BPVTxyznuviiiwwtb` | 40 | 53,300 | skinned |
| `BPVTxyznuvitb` | 36 | 2,128 | havok collision proxies |
| `BPVTxyznuv` | 24 | 608 | |
| **`BPVTxyz`** | **12** | **67** | audio occluders, position only |
| `BPVTxyznuviiiww` | 32 | 36 | `env_birds` |

**No non-BPVT format ships.** Not one section in 121,000 uses a bare
`xyznuv`-style header, so the 136-byte body offset is right for 100% of real
data and the three non-BPVT rows in the stride table are dead fallbacks.

Adding `BPVTxyz` needed a second fix to be safe rather than merely wrong later.
The vertex loop read the packed normal at `+12` and the uv at `+16..+23` at
FIXED offsets, unconditionally - fine when every shipped format had both. At
stride 12 that takes 12 bytes out of the NEXT vertex and runs off the end of the
buffer on the last one, and the bounds check only guarantees
`nVerts * stride`, so it would not have caught it. Both reads are now gated on
the stride being large enough to hold the field.

Section sizes, measured the same pass: only ever **0 or 2 mod 4** - 245,050 and
36,842 of 281,892 sections, never odd. So a reader that pads with a wrong
`+= size Mod 4` still lands correctly on every shipped file, and would keep
doing so right up until it did not.

`PrimitiveLoader.load_primitives_vertices` has no case for `BPVTxyznuvitb`. It
falls through to `Case Else`, which is `Debug.Assert(False)` — compiled out in
Release, leaving `stride = 0`. Those 164 sections cannot be read by the engine
today. Reported to the nuTerra Work session rather than fixed here; `ModelLoaders`
is theirs.

### How the parse is checked

Same rule as the `.model` reader: two readers, same numbers.
`bld_101_02_Vhouse02_a` at lod0 reads **9,233 vertices and 5,645 triangles** in
the viewer and the identical figures from an independent Python decode. All
4,865 building `.primitives_processed` files parse without error.

## The material tags: `s_`, `n_`, `d_`

Every material in a building visual carries an `identifier`, and it is prefixed.
Measured over **29,662 materials in 5,092 building visuals**:

| prefix | count | meaning |
|---|---|---|
| `s_` | 19,191 | static — never has a twin |
| `n_` | 5,673 | **n**ormal, the intact state of a destructible |
| `d_` | 4,727 | **d**estroyed, the same piece as rubble |

`n_` and `d_` are a matched pair: **212 identifier stems carry both** forms
(`n_wood0_1` ↔ `d_wood0_1`), and the `materialKind` beside them moves by a
constant:

```
materialKind(d_) - materialKind(n_) = +14     in 211 of 212 stems
```

Intact band **73–85**, destroyed band **87–98**, static outside both at
0 / 108 / 111 / 112. The number is set by the **trailing index, not the
material family** — `n_wood0_2`, `n_metal1_2` and `n_stone2_2` are all kind 74 —
so `materialKind` is a *state + slot* code, not a surface type. No
`material_kinds.xml` ships anywhere in `res/`, so the structure is known and
Wargaming's names for the numbers are not.

`collisionFlags` sorts the same geometry a second way, and it is nearly
single-purpose:

| flags | count | who carries it |
|---|---|---|
| 144 | 584 | `s_wall_*` — **584 of 584**, nothing else |
| 131 | 685 | `s_ramp_*` and `s_wall_*` — 674 of 685 |
| 255 | 5,197 | catch-all on ordinary static geometry |
| 0 | 22,864 | the default majority |

### Some primitive groups are never drawn

A `s_wall_0` material looks like this, complete:

```xml
<material>
  <identifier>s_wall_0</identifier>
  <collisionFlags>131</collisionFlags>
  <materialKind>0</materialKind>
</material>              <-- no <fx>
```

No shader, no properties, no textures. It is a `primitiveGroup` sitting in the
render mesh's index buffer that the renderer skips entirely — an invisible
blocker. The correlation holds both ways, which is what makes it a rule rather
than an observation:

* **1,512 of 1,512** materials whose identifier STARTS WITH `s_wall` or `s_ramp`
  have no `<fx>`. Not 97%, not "nearly always" - none of them, ever.
* **1,512 of 1,545** materials with no `<fx>` are `s_wall_*` / `s_ramp_*`

### The 32 exceptions were my own bug

That first number was reported as 1,512 of 1,544 before it was measured
properly, with 32 apparent exceptions carrying a real shader. The 32 are all
one identifier - **`s_nd_0_wall`** - and it is not a wall material at all. It is
an ordinary `s_nd` material, `collisionFlags` 0, with a real `PBS_tiled` or
`PBS_ext` shader, that happens to contain the letters `w-a-l-l`. A SUBSTRING
test swept it in; a prefix test does not.

That is the same mistake the flight bake's classifier makes when it keys
`StreetLamp` as a **tree** because the name contains `t-r-e-e`. It is worth
leaving written down here rather than quietly corrected, because the corrected
rule is much stronger than the one it replaces: a material that blocks is a
material with no shader, with no exceptions in the shipped library.

On `hd_bld_EU_049_THouse`, `UpperFloorsSmall_02` carries one: **4 triangles, 8
verts, 1.97 × 2.09 × 1.79 m** — two quads across a doorway.

**The exporter currently writes these as visible geometry.** A group with no
`fx` should be skipped. Worth keeping the scale honest, though: on that
building it is 4 triangles out of 20,657 at lod0 — **0.02%** — so this is a real
category of bug and it is *not* what made the first OBJ a mess.

## `.vt`: collision only, and none for buildings

10,630 `.vt` files ship. They decode completely and they are worth nothing to
this app.

```
u32     magic 0xB00BB00B
i32     version              2 on every file
6×f32   bbox min xyz, max xyz    exactly equals the vertex span
u32     vertex count
f32[]   count × 3
u32     index count
u8      index width flag     1 = uint16
u16[]   index count, in threes — one triangle each
u32     group count
        group count × { u32 startVertex, u32 vertexCount }
```

The vertex count is **not** divisible by three (427, 10,051, 39,423) — verts are
shared and it is the *index* list that comes in triples. The groups tile the
vertex array exactly, with no gap and no overlap.

**984 files parsed, 100% consistent, zero bytes left over on every one**, header
bbox matching the computed vertex span, every index inside the vertex count. The
95-byte `Gun_01.vt` is the entire format legible in one hex line: 3 verts, 1
triangle, 1 group.

### Why there is nothing here to read

`.vt` is a position-only mirror of the render mesh:

| check | result |
|---|---|
| `.vt` triangles vs the sibling render mesh | **identical, 120 of 120** |
| `.vt` groups vs the visual's `primitiveGroup` count | **equal, 120 of 120** |
| vertex positions vs the render mesh | 6,739 of 6,739 unique shared, raw coords, **no X flip** |

Same verts, same order, same triangle count, same per-material partition — a
`.primitives_processed` with the UVs, normals, tangents and bone data stripped
off. The group index is the lookup back into the visual's `primitiveGroup` at the
same slot, which is how a hit triangle reports its `identifier`,
`collisionFlags` and `materialKind`. `Hull.vt` is not closed (10,296 boundary
edges), so it is a ray-hit soup rather than a physics volume.

It carries **no attribute this exporter needs that the geometry file does not
already have**. Its only use is collision.

And it does not apply here in any case:

| area | files |
|---|---|
| vehicles | 10,557 |
| MilitaryEnvironment | 42 |
| other | 31 |
| **Buildings** | **0** |

Within vehicles, Hull, Turret, Gun and lamps get one and **Chassis never does —
0 of 2,204**; Chassis is the skinned part, and a static copy of skinned
positions would be meaningless. lod0 only.

So for buildings the collision story stays where the section above left it: the
visual's `collisionFlags`, the invisible `s_wall_` / `s_ramp_` groups, and the
`.havok` binaries.

## The geometry is not solid

Measured before choosing any slicing default, over 261 lod0 meshes:

| | |
|---|---|
| watertight meshes (no boundary, no non-manifold edge) | **12 of 261 — 4.6%** |
| edges, welded: manifold / boundary / non-manifold | 83.14% / 15.21% / 1.65% |
| edges, raw indices: manifold / boundary / non-manifold | 48.81% / 51.17% / 0.02% |
| vertices | 2,940,023 raw -> 1,611,718 welded (**45.2% duplicates**) |

Two things follow, and between them they fix the two settings that are not a
matter of taste.

**Welding is mandatory.** 45.2% of the vertices sit on top of another vertex at
the same position, split apart for UV seams and hard normals. Judge the topology
on raw indices and it reads 51% boundary edges — every seam looks like a hole,
and a cut loop comes back shredded into fragments. Weld first and the same
meshes read 15%. So `mesh.weldTolerance` is validated as greater than zero and
the app refuses to run with it at 0.

**These are shells, not solids.** Only 4.6% are watertight, so a cut through a
typical building meets an *open span*, not a closed loop. That is why capping is
split into two settings: `result.cap` fills a closed loop, which is honest
geometry, while `result.capOpenSpans` closes a span that was never closed in the
source — inventing surface the artist did not author. The second is off by
default and should stay off unless you want a solid-looking result more than a
truthful one.

It also bears on which library can cope, though with a caveat about what has
actually been checked. That `MeshPlaneCut` treats the open-boundary case as
first class is verified — `CutSpans` and `FoundOpenSpans` are fields in its
source, quoted below. **The claim that CGAL rejects an edge shared by more than
two triangles is second-hand**, taken from a search summary; CGAL has not been
run here. Treat it as a reason to check before reaching for a CSG library, not
as a measured fact.

## Settings

    Slicer --show-settings              print them (no game install needed)
    Slicer --save-settings              write a commented slicer.settings
    Slicer --set slice.mode=stack       override one for this run
    Slicer --settings other.txt         use a different file

Plain `key = value` text with `#` comments, living beside the exe, editable in
Notepad without the app running. Unknown keys are preserved on a round trip
rather than dropped, so a newer build's settings survive an older one reading
and rewriting the file.

| key | default | |
|---|---|---|
| `plane.axis` | `y` | `x`/`y`/`z`/`custom`. Y first because a horizontal cut gives floors |
| `plane.normal` | `0,1,0` | only read when axis is `custom` |
| `plane.offset` | `0` | metres along the normal |
| `plane.origin` | `auto` | `auto` = bounding-box centre, or `x,y,z` |
| `slice.mode` | `single` | `single` or `stack` |
| `slice.spacing` | `2.0` | metres between planes; roughly a storey |
| `slice.count` | `0` | 0 = as many as fit the bounds |
| `result.keep` | `below` | `below`/`above`/`both`. `both` costs two cuts — `MeshPlaneCut` deletes the positive side in place |
| `result.cap` | `true` | fill closed cut loops |
| `result.capOpenSpans` | `false` | **invents geometry** — see above |
| `mesh.weldTolerance` | `0.00001` | metres. **Must be > 0** — see above |
| `mesh.dropDegenerate` | `true` | drop zero-area triangles before cutting |
| `scope.lod` | `0` | |
| `scope.parts` | `all` | or a substring of the part name |
| `scope.includeHavok` | `false` | no reader for proxy geometry yet, so inert |
| `out.dir` / `out.format` | `slices` / `obj` | |

Settings are validated before the packages are read, so a bad value costs a
second rather than a full scan.

**The assumption these encode**, stated plainly because it is not settled: these
are the settings for **sectioning a mesh with planes** — the geometry3Sharp
path. They are not 3D-print settings; there is no layer height, nozzle, infill
or support here. If printing is the goal, the measurement above is the first
thing to deal with, because a 4.6%-watertight shell is not printable without a
repair or solidify pass, and those settings are not in this table.

## The cut

`MeshSlicer.Clip` is Sutherland-Hodgman against a single plane, per triangle:
classify the three corners, keep / drop / clip, fan the resulting 3- or 4-corner
polygon back into triangles, and record the two intersection points as a segment
of the cut outline. `--view` drives it live — `S` toggles, `,` and `.` move the
plane, `X`/`Y`/`Z` pick the axis, `K` cycles below/above/both.

Measured on `hd_bld_eu_225_cathedral` lod0: 156,342 triangles, cut on Y at
38.46 m (the bounding-box midpoint, which is what `plane.origin = auto` means),
**1,529 triangles clipped and 1,529 cut segments**. Those two numbers being
equal is the self-check — every straddling triangle contributes exactly one
segment, so an inequality means a degenerate case was silently dropped.

Nothing here caps a hole. The cut is shown as it is, open where the mesh is
open, which given 4.6% watertight is most of the time.

### geometry3Sharp: what is verified and what is not

geometry3Sharp remains the intended library for the **export** path, where a cut
has to be capped, welded and written out and robustness matters more than
latency. It is deliberately not used for the interactive cut above, because
`MeshPlaneCut` operates on a `DMesh3` and cuts in place — driving it from a key
the owner holds down would mean converting in and back out, twice over for both
halves, every frame.

**Verified** — read from `mesh_ops/MeshPlaneCut.cs` at master:

    public class MeshPlaneCut
    public DMesh3 Mesh;                    // a DMesh3, so using it costs a conversion
    public Vector3d PlaneOrigin;
    public Vector3d PlaneNormal;
    public List<EdgeLoop> CutLoops;
    public List<EdgeSpan> CutSpans;        // open boundaries are first class
    public bool CutLoopsFailed = false;
    public bool FoundOpenSpans = false;
    public bool CollapseDegenerateEdgesOnCut = true;
    public virtual bool Cut()
    public bool FillHoles(int constantGroupID = -1)
        /// A quick-and-dirty hole filling. If you want something better,
        /// process the returned CutLoops yourself.        <- its own words

**Not verified** — the package has not been added, run, or its `Cut()` body
read:

* That it deletes the **positive** side specifically. The field list does not
  say which side goes; that detail came from a summary, not from the code.
* Boost licence, NuGet availability, the `dotnet8` branch. Read off the project
  page, not confirmed by installing it.

Anything in this section marked not-verified should be checked before it is
built on. It is recorded this way rather than left out because knowing which
half of a claim is solid is more useful than a clean-looking paragraph.

## Closing the bottom

The owner's observation, and it measures out: these models are **cut off flat
where they meet the ground**, so the boundary at the bottom is already planar
and can simply be triangulated. No general hole-filling, nothing invented.

Measured over 128 lod0 meshes before any of it was written:

| | |
|---|---|
| with a bottom boundary ring of 3+ edges | 69 — 54% |
| of those, every bottom vertex has degree 2 (clean rings) | 30 — 43% |
| bottom boundary edges per mesh | median 12, mean 20, max 183 |
| with no bottom edge at all | 23 |

54% rather than 100% is correct, not a shortfall — a roof or a tower that never
reaches the ground has no bottom cut to fill. And because only 43% of rings are
clean, the walker handles open chains and T-junctions rather than assuming a
closed loop.

**The plane is each MESH's own lowest point, not the asset's.** That was the
other way round first, reasoning that a roof's lowest boundary is in mid-air.
Measuring `hd_bld_eu_049_thouse` killed it: the asset is a KIT of eleven
independent pieces, each plane-cut at its own base, spread over a metre of
height. Against the asset minimum ten of eleven were excluded and the building
rendered wide open from below.

    lowerfloorssmall_01  -1.1138   8 bottom edges   <- the only match
    lowerfloorsbig_01    -1.0000  12 bottom edges
    upperfloorsbig_03    -0.1379  24 bottom edges
    roof_01              -0.7378   8 bottom edges

**The tolerance is a fixed 2 cm, not a fraction of the model.** Span-scaling it
gave the 102 m cathedral 0.205 m of slack, which swept in edges never on its
bottom plane — 148 spurious triangles, and a worst fill spread of 0.18 m. A
plane cut is exact; the tolerance only absorbs float noise, which does not grow
with the building.

### Checking it without looking at the screen

`--shot <file.png>` renders one frame from below, paints the fill red and
everything shipped in the package blue, and writes a PNG — so the result can be
inspected without anyone watching the window.

The stronger check is the number beside it: **the worst Y spread of any single
mesh's fill**. Each fill is supposed to lie in one plane, so a fill that climbed
off its plane shows up as a spread even when the picture looks plausible. Per
mesh, not across the asset — a kit legitimately spreads a metre across its
pieces while every individual fill is dead flat.

| asset | fill | rings | worst single-mesh spread |
|---|---|---|---|
| `bld_101_02_vhouse02` | 47 tris | 4 | 0.0110 m |
| `hd_bld_eu_049_thouse` | 82 tris | 11 | 0.0187 m |
| `hd_bld_eu_211_lighthouse` | 122 tris | 4 | 0.0102 m |
| `hd_bld_eu_225_cathedral` | 321 tris | 34 | 0.0193 m |

**Known limits.** The fan is from the ring centroid, which is exact for a convex
outline and will put slivers outside a strongly concave one — an ear clip is the
upgrade if an export ever needs it. 29 of the cathedral's 34 rings are open
chains rather than closed loops, and those are fanned anyway. And a mesh with no
bottom boundary gets nothing, which is why the cathedral is only partly red from
below.

## Is it watertight?

**No.** `--check [n]` answers it with a sweep, and the honest number is that the
bottom fill barely moves it. Over 120 assets / 464 lod0 meshes:

|  | before fill | after fill |
|---|---|---|
| watertight meshes | 12 (2.6%) | 20 (4.3%) |
| manifold, no branching | 321 (69.2%) | 321 (69.2%) |
| open boundary edges | 1,575,479 | 1,560,893 |

**The fill closes 0.9% of the open edges.** It touched 345 meshes and made 8 of
them watertight. So the bottom is genuinely a plane cut and closing it works —
but the bottom is not where the holes are. These meshes are open everywhere:
windows, doorways, interior faces, unjoined panels. Roughly 3,400 boundary edges
per mesh remain after the bottom is closed.

And **85,321 non-manifold edges survive**, which is the harder half. A hole can
be filled; an edge carried by three or more triangles means the surface branches
and encloses nothing definite. No amount of hole-filling fixes that.

So anything downstream that needs a solid — a CSG library, a 3D print, a volume
— cannot have one from this geometry by closing the bottom alone.

### The sweep caught a bug in the fill

The first version wrapped every chain all the way round, inventing an edge
between the last vertex and the first. On an OPEN chain that edge is not a
boundary edge and may already be carried by two triangles, so the fill pushed it
to three: **manifold meshes fell from 321 to 314**. The picture looked fine
throughout — the count is what found it. Open chains now stop one triangle short
and leave the gap as a gap.

That is the argument for `MeshCheck` existing as an API rather than a one-off
script: `Analyse` gives the edge census for a mesh, `AnalysePair` gives it
before and after the fill, and a regression in the filler shows up as a number
on the next sweep.

**Known limit: it is slow.** 101 s for 40 assets, ~250 ms per mesh, dominated by
the string-keyed dictionary used to weld positions. Fine for a check that runs
occasionally, too slow to put in a loop.

## Rebuilding a set model into a shell

The owner's framing: **these are set models, cheap, the way Hollywood does it.**
Facades built for what the camera sees, no back walls, no closed volume. He
named the expensive fix (a ball-pivot walk that reconstructs a real surface) and
rejected it as long and slow, and gave the cheap one instead: rays through to
find the outside walls, weld every vertex in range, then a post pass removing
everything that makes a zero-length line.

`--shell` runs it, or `E` in the viewer.

**The ray cast is the rasteriser.** A GPU rasteriser IS a ray caster - one ray
per pixel, with the depth test doing nearest-hit for free. Each triangle is
drawn in a colour encoding its own index, from 64 directions on a Fibonacci
sphere at 512px, and every index that appears in any frame is exterior. That is
16 million rays in about half a second; a CPU walk would not finish one view in
that time. Backface culling is off, because a set model's walls are single-sided
and frequently wound inward - culling would discard the outside of every one of
those.

On `hd_bld_eu_049_thouse`, 548 ms:

| | |
|---|---|
| bottoms closed before the scan | 82 tris |
| merged | 20,739 tris |
| welded at 0.05 m | 13,989 tris (−5,663 degenerate, −1,087 duplicate) |
| **seen from outside** | **4,505 of 13,989 — 32.2%** |
| final | 4,505 tris, 3,571 verts |
| boundary edges | 5,831 → **2,685** |
| non-manifold edges | 369 → **137** |

**Two thirds of the geometry is never visible from outside.** That single number
is the set-model character measured: 67.8% of this building exists only to be
seen from inside, or not at all.

### Order matters, and it was wrong first

The fill ran *after* the scan to begin with. The render showed why that fails:
with the bottom still open, rays arriving from below fly up into the building
and light the interior, so the scan calls all of it exterior and keeps it.
Closing the bottom first stops those rays at the fill, which is what makes the
inside genuinely unseen and therefore removable.

The fill must also happen **before the merge**, because it works per mesh off
that mesh's own lowest point - merging eleven kit pieces at eleven heights into
one mesh leaves one bottom plane and produced a single 8-triangle ring where
eleven belonged. Fixing the order moved every number: visible 38.4% → 32.2%,
boundary 3,223 → 2,685, non-manifold 156 → 137.

### It is still not watertight

2,685 boundary edges and 137 non-manifold edges remain. What is left is windows
and doorways, which are genuinely open in the source, and the raw edges exposed
where interior geometry was removed. The shell is a much better object than the
raw asset - 74% fewer triangles, 54% fewer holes - but it is not a solid, and
nothing here claims it is.

## Not done yet

**Export.** The cut exists only on screen; `out.dir` and `out.format` are
parsed and unused. Writing a cut out is where capping, welding and
geometry3Sharp actually come in.

**Stack mode.** `slice.mode = stack` validates and is honoured by nothing — the
viewer cuts with one plane. `slice.spacing` and `slice.count` are inert.

**Textures and materials.** The `.visual_processed` beside each
`.primitives_processed` names the material per primitive group; the viewer tints
parts from a fixed palette instead.

**Havok proxies.** Counted and identified, never parsed. `scope.includeHavok`
does nothing until a reader exists.

**A startup double-load.** `LoadCurrent` runs twice when the viewer opens, so
the first asset is parsed twice. Startup only, not a loop, nothing visible
depends on it — but it is real and unexplained, not cosmetic.
