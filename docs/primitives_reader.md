# Reading a WoT `.primitives` outside nuTerra

`tools/draw_model.py` opens any model straight out of the installed packages,
parses the mesh, and draws it — no viewer, no map load, no bake.

```
python tools/draw_model.py StreetLamp02 --campath nuTerra/cam_paths/19_monastery.campath
python tools/draw_model.py bld_19_01_Vhouse_05 --lod 1 --out house.png
python tools/draw_model.py StreetLamp01 --campath nuTerra/cam_paths/19_monastery.campath --radius 0.8
```

It prints the bounding box and every bulb record in the `.campath` that targets
that model, and writes a four-panel PNG: front, side, three-quarter, and a
close-up of the bulb. Geometry within `--radius` of the bulb is painted red.

## Why it exists

A bulb is authored INSIDE a light fixture, and whether the fixture encloses it
decides whether the lamp shadow bake seals the light in — see `shadows.md`.
That question cannot be answered from a frame: a bulb sealed in its own housing
and a bulb with the wrong range both look like a lamp that does not light. It
is obvious in one drawing.

It also answers the plainer question — *what does this model even look like* —
without a 40-second map load.

## The file format

Everything below is `ModelLoaders/PrimitiveLoader.vb` restated. That file is the
ground truth; this is here so the next reader does not have to re-derive it.

### Finding the file

The path in a `.visual_processed` ends in `.primitives`, but what ships is
`.primitives_processed`. The loader does a plain string replace. The tool scans
every `res/packages/*.pkg` for it, because a model can live in a map package
(`19_monastery.pkg`) or a shared one (`shared_content_sandbox-part1.pkg`) and
nothing in the path says which.

### The section table is at the END

```
last 4 bytes          -> length of the table
table starts at        len - 4 - that
per entry:             size:u32, 16 unused bytes, namelen:u32, name, pad to 4
```

Entries carry **no offsets**. Bodies start at offset 4 and each is padded to 4,
so the only way to locate a body is to add every size before it, in order. Get
the padding wrong and every section after the first is shifted.

Two layouts exist: one global `vertices` / `indices` pair, or one
`<base>.vertices` per mesh. The tool handles both by taking every section whose
name ends in the right suffix.

### Vertices

```
64 bytes    format name, null padded  ("BPVTxyznuvtb", "xyznuv", ...)
+68 bytes   ONLY when the name starts with BPVT
4 bytes     vertex count
            body
```

So a BPVT body starts at **136**, not 68. A 132-byte guess matches by
integer-division coincidence and silently shifts the whole stream by one float
— this has bitten the project before, on the uv2 section.

Strides by format name:

| format | stride | | format | stride |
|---|---|---|---|---|
| `xyznuv` | 32 | | `BPVTxyznuv` | 24 |
| `xyznuvtb` | 32 | | `BPVTxyznuvtb` | 32 |
| `xyznuviiiwwtb` | 37 | | `BPVTxyznuviiiww` | 32 |
| | | | `BPVTxyznuviiiwwtb` | 40 |

Position is the first three floats of each vertex. The tool reads nothing else;
normals are packed 8-8-8 in a `u32` and are not needed to draw a silhouette.

### Indices

```
64 bytes    type name  ("list" = u16, "list32" = u32)
4 bytes     index count
4 bytes     primitive group count
            indices
            group table: startIndex, nPrimitives, startVertex, nVertices (4x i32 each)
```

The group table sits AFTER the indices, so you have to skip
`count * indexSize` to reach it.

### Coordinates

Two flips, and both are needed or the model is inside out:

* **Negate X** on every position. DirectX to OpenGL.
* **Swap the first two indices** of every triangle. The winding has to flip with
  the mirror, or every face points inward.

Do one without the other and the model looks right in silhouette while every
normal is backwards.

## The `.campath` bulb record

A record begins with its primitives path in a 160-byte field, so **searching the
file for the path finds the record**. That beats walking the header, where the
bulb block sits behind five counts and strides (`MapCamPath.Load`).

From the record start: `kind` at +160, `pos` at +164, `aim` at +176. Kinds are
`0` point, `1` cone, `2` inverse cone, `3` dual cowled.

## How the parse is checked

The printed bounding box is directly comparable with nuTerra's own log line:

```
bulb placer: env_19_08_StreetLamp02 - 1 mesh(es), box 0.54 x 5.70 x 0.79 m
```

Two independent readers, same three numbers. If they disagree, the stride, the
BPVT preamble or the X flip is wrong — not the model. Check this before
believing anything else the tool says.

## What it found on 19_monastery

The map's two street lamps, and they are not the same problem:

| | `StreetLamp01` | `StreetLamp02` |
|---|---|---|
| box | 0.43 x 3.33 x 0.43 m | 0.54 x 5.70 x 0.79 m |
| bulb kind | 3, dual cowled | 1, cone |
| bulb pos | (0, 2.934, 0) | (0.006, 4.469, 0.504) |
| aim | straight down | straight down |
| shape | vertical post, lantern on top | tapered post, curled arm, hanging shade |

`StreetLamp01` is the case kind 3 was built for: the bulb sits **on the post
axis**, so the lantern encloses it above and to the sides and the post runs
directly below it. Nothing but its own fixture is within half a metre. That is
the model that measured 100% blocked inside 1 m before the bake learned to leave
a light's own model instance out of its cube.

`StreetLamp02` hangs its bulb under a shade with open air below, and the arm
carries it 0.50 m off the post — a much weaker version of the same thing.

Both are drawn in the tool's own output; run it and look rather than taking this
table's word for it.
