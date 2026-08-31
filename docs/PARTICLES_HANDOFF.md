# Card particle system — handoff

**Merged to `master`** (`97ef7a7`). The branch `particles-cardtest` still
exists and git does **not** consider it merged - its tip `46b9a98` is not an
ancestor of master. The entire content delta is two `.gitignore` lines for
test images, so nothing of substance is stranded there - but check that for
yourself rather than trusting this sentence.

This document covers the particle **simulation** and the `.vfxbin` data behind
it. How the FX are lit, accumulated, tonemapped, glowed and composited is a
separate document: **`FX_PIPELINE.md`**. Read that one first if the question is
about how FX *look* rather than how they move.

## Lockdown

Fire and the **volumetric smoke model FX** work and are locked. Nothing in
`shaders/Model_shaders/volumetric.{vert,frag}` or the `draw_fx` path changes
without walking the full call path and its state assumptions first. The card
particle system is a *separate* path and is not covered by the lockdown.

Corollary that cost time: **do not attribute pixels to either path without an
A/B.** Toggle `PARTICLES_ENABLED` off and on at a fixed camera and diff the
frames.

SETTLED: the smoke above the monastery house is **cards**, not the volumetric
mesh. An earlier claim to the contrary was made from inference and was wrong.
`Fire_monastery_big.visual_processed` declares only four FIRE materials and the
snapshot reports exactly four fx draws; the map's only smoke mesh,
`Grass_fire\SmokeBotton_02`, sits ~150 m away by the tank wrecks. Since smoke
pixels can now come from either path, always name which one you mean.

Second corollary, learned the hard way on the glow: **the sim is
non-deterministic run to run**, so an A/B across two live runs measures sim
drift as much as the change. Pin it with `freezefx`.

## Commit state

On `master`. The merge commit `97ef7a7` un-reverted the whole card particle
system, which master had deliberately removed in `86c977b`.

Builds clean — only pre-existing warnings (NETSDK1138, DotNetZip NU1903,
`IsMultiThreaded` obsolete).

**Merging across that revert has a trap in it.** Git flagged 7 conflicts, but
that was not the whole story: master's revert had deleted lines the branch
never touched, so the automatic merge silently took the deletion on five more
files it did *not* flag — `Window.vb`, `ShaderLoader.vb`, `ResMgr.vb`,
`MapScene.vb`, and `particle.vert`, which went missing entirely. That tree
compiled `MapParticles.vb` against a non-existent vertex shader. If you ever
merge across a revert again, diff the merged tree against the source branch;
the conflict list understates it.

## Solved this session

### 1. Atlas region ordering

`.vfxbin` block 999 stores the sheet rect at `+192..+204` as
**`(v_max, u_min, v_min, u_max)`**. Settled against the grid catalogue in
`Tank-Exporter-PY-master/cust_tools/extract_wot_fire_atlas.py`: across 2404
emitters this ordering lands 297 regions on a catalogued grid — the best of
all 24 permutations — and `smoke_Big` resolves to the region the catalogue
names `smoke_white` 8x8 @128px, matching its declared 8x8.

### 2. The v flip — the actual bug

`TextureMgr.load_dds_image_from_stream` (`TextureMgr.vb:331`) hands raw DDS
bytes to `CompressedSubImage2D` with **no vertical flip**. DDS stores rows
top-down; GL places the first supplied row at `v = 0`. So the atlas sits
upside down in texture space and

```
sampler_v = 1 - stored_v = y_top_down / 4096
```

Rows walk **down** in sampler v, not up. Fixed at `MapParticles.vb:281`:

```vb
Dim vOff = (1.0F - p.em.vMax) + cellY * cellH
```

Before this, cards spent the back half of every life sampling **spark and
debris frames** from the bottom of the atlas. That is the origin of the
"sparse discrete puffs" symptom.

Worth knowing: the pre-session committed code used a hardcoded stand-in sheet
at `u 0.25..0.50, v 0.00..0.25`, which is accidentally *exactly* right for
`smoke_Big` in sampler space. That is why the stand-in looked plausible and
the first "proper" decode looked worse than it.

### 3. Spawn position

Cards now spawn at `s.origin` with no positional randomness
(`MapParticles.vb:214`), per instruction. The authored box it replaced was a
genuine ±0.5 m (table below) — revert is one line if the shipped behaviour is
wanted. At ±0.5 m against 7–10 m cards it was not causing visible scatter.

## Verified reference data

Atlas: `particles/content_deferred/PFX_textures/eff_tex.dds` — **4096×4096**,
13 mips, DXT5, inside `res\packages\particles.pkg`. A second
`content_forward/PFX_textures/eff_tex.dds` is the **same sheet at 2048×2048**,
identical layout and frame order. Effects from the `content_deferred` tree
pair with the deferred atlas, and nuTerra is a deferred renderer, so use that
one. UVs are resolution-independent; pixel numbers are not.

Sheets in use — pixels are top-down, i.e. as seen in an image viewer:

| emitter | file px | sampler u | sampler v | grid | cell |
|---|---|---|---|---|---|
| `smoke_Big` | (1024, 0) – (2048, 1024) | 0.25 – 0.5 | 0.0 – 0.25 | 8×8 | 128 px |
| `smoke_Slow` / `smoke_Fast` | (2048, 2560) – (3072, 3584) | 0.5 – 0.75 | 0.625 – 0.875 | 8×8 | 128 px |

Frame 0 — the top-left cell — in sampler space:

```
smoke_Big    u 0.25000 .. 0.28125    v 0.00000 .. 0.03125
smoke_Slow   u 0.50000 .. 0.53125    v 0.62500 .. 0.65625
step 0.03125 in both axes
```

The slow/fast sheet is **not** in the tank-exporter catalogue and is not on
the 1024 lattice. It is nonetheless real: an alpha-gutter scan of column
x 2048..3072 finds clean empty bands at y 2560..2581 and y 3566..3595, and the
eight 128 px rows between them run mean alpha 36.7, 49.7, 50.9, 45.2, 37.9,
29.8, 20.6, 9.7 — one rise-and-fade puff lifecycle. The catalogue's
`flame_columns_light` (2048, 3072, 3072, 3584) cuts straight through this
sheet and is wrong there. Worth fixing in the Python tool; does not affect
nuTerra.

Emitter parameters, read from block **1000** (`BLOCK_SOURCE`) — note the
block number, reading these out of block 999 produces 1e30 nonsense and a
false "nothing ever spawns" conclusion:

| emitter | rate/s | box half | spread | size | life |
|---|---|---|---|---|---|
| Big/`smoke_Slow` | 1 | 0.5, 0.5, 0.5 | 0.3491 (20°) | 7–10 | 3–4 s |
| Big/`smoke_Fast` | 1 | 0.5, 0.5, 0.5 | 0.3491 | 7–10 | 4–6 s |
| Big/`smoke_Big` | 5 | 0.5, 0.5, 0.5 | 0.3491 | 2–4 | 3–4 s |
| Med/`smoke_Slow` | 1.5 | 0.5, 0.5, 0.5 | 0.3491 | 6–9 | 3–4 s |
| Med/`smoke_Fast` | 2 | 0.5, 0.5, 0.5 | 0.3491 | 3–5 | 4–6 s |
| Small/`smoke_Fast` | 2 | 0.5, 0.5, 0.5 | 0.3491 | 4–5.5 | 3–4 s |

Block offsets, relative to the block start found by `FindBlock` (id, then
`2UI`):

```
block 1000 (source):    +36 rate    +44..+52 box half extents    +68 spread
block  999 (particle):  +176/+180 sizeMin/Max    +184/+188 lifeMin/Max
                        +192..+204 sheet rect (v_max, u_min, v_min, u_max)
                        +220 rows    +224 cols
```

## Particle lifecycle — verified sound

`MapParticles.vb:194` ages and kills:

```vb
For i = live.Count - 1 To 0 Step -1
    live(i).age += dt
    If live(i).age >= live(i).life Then live.RemoveAt(i)
Next
```

Reverse iteration, so `RemoveAt` is safe. Dead cards are removed outright —
there is no pool and no recycling. `Particle` is a `Class`, so every spawn is
a fresh heap object with all eight fields assigned at birth. No stale
position can survive a death. Spawning is capped at `MAX_PARTICLES` = 4096,
four orders of magnitude above current live counts, so the cap is not in play.

## Open problems

**Density is the big one.** rate 1–5/s against a 3–6 s life gives only **3–30
live cards per emitter**, which cannot produce the game's dense column. Those
are the authored numbers, so one of these must be true: the game instantiates
many more emitter instances than the three `.vfxbin` files we read; `rate` is
scaled by something not yet found; or LOD/quality multiplies it. This is the
next thing to chase.

**`SPEED_GAIN = 4.0`** in `MapParticles.vb` is motion tuning, not authored
data. Its premise still holds — it was set when track 0 drove size, and track 0
drives size again — so the re-tune it once demanded is not owed. Still a knob,
just not an urgent one.

**`CARD_SIZE_SCALE = 0.5`** halves the peak of the size curve. Track 0 read raw
grows a card about 12x over its life, which is what closes the separated puffs
into a continuous column, but the peak was larger than the game's. Applied to
the curve rather than clamped, so the growth keeps its shape instead of
growing a flat top partway through the life; birth size halves with the peak,
so the two stay in the authored ratio.

Smaller cards also mean less overlap, which mattered when the FX composited
into an 8 bit buffer. It matters less now — see `FX_PIPELINE.md`.

~~**No depth sorting** for particle cards.~~ Done. `BuildInstances` fills a
preallocated `sortKeys` with `-(pos - camPos).LengthSquared` per emitted
instance and `Array.Sort(sortKeys, instances, 0, n)` orders the filled range
farthest-first before upload. Measured on a populated column: 13 cards,
36.88 m -> 32.39 m, descending throughout. It is needed because `particle.frag`
emits `vec4(rgb * alpha, alpha)` unconditionally - no additive branch - so every
card is order-dependent under the pass blend.

**Still unidentified:** the `1000+84/+88` pair, and the effect-id hash (not a
path hash — tested 10 algorithms across 19,039 strings).

Closed since: **track 0 is size over life**, read raw, and tracks 2/4/5/7 are
tool defaults — byte-identical across all seven smoke emitters of the three
burning-house effects, so they carry no information. The `999+216` bitfield is
a lattice of shader-feature selectors, not a rank: bit 26 is set on 100% of
`ps_long.fx` emitters and 0% of every other pixel shader. See
`VFXBIN_PARTICLE_FORMAT.md`.

**There is no authored render order for FX or particles** — swept exhaustively
and it is not in the data. The game composites with order-independent
transparency and its GPU cull appends with an atomic counter that scrambles
order outright. Do not go looking; the format doc's "Render order" section
records what was checked.

**Atlas animation** now runs once across a particle's life rather than looping
at the authored fps: `frame = floor(t * cells)`, clamped, with the random
`frameSeed` start offset removed. The wrap it replaced made cards appear to
restart in mid-air, which is what the "respawning where they died" report turned
out to be. The authored fps at `999+228` is decoded but no longer used.

## Testing

Launch args (`Program.vb`): `<map> [cam=r,ax,ay,lx,ly,lz] [freezefx] [clean]
[half] [blackfx] [snap|snapquit] [settle=N] [gridfx] [gridfxoffset=F]
[noglow]`. The `cam=` form is exactly what Snapshot prints, so a view can be
set up by hand, saved, and reproduced verbatim on every later launch.

- `settle=N` overrides the 150-frame wait before the automatic snapshot. The
  emitters run at 1-5/s against 3-6 s lifetimes, so a steady-state column needs
  several seconds — 150 frames photographs a column that is still filling.
  `settle=600` is a reasonable full column.
- `freezefx` pins `FX_TIME` at 0 **and** stops particles spawning, so only the
  meshes render. This is what makes an A/B deterministic.
- `noglow` / `gridfx` toggle the two FX features that have no other headless
  writer.

```
nuTerra.exe 19_monastery cam=-51.1409,6.1232,0.1388,142.8446,11.8214,29.2966 half clean snapquit
```

`snap` / `snapquit` count **150 frames after `MAP_LOADED`**, so a first run
that regenerates the outland caches delays them by minutes — that is not a
hang. Output lands in `%TEMP%\nuTerra\snapshot.txt` and
`%TEMP%\nuTerra\fx_pass.png`.

`PARTICLES_WIRE` in `modGlobalVars.vb` draws cards as untextured wireframe
coloured by age — green at birth, red at death. This is the tool that found
the size-track bug.

Kill `nuTerra.exe` before building, or the exe copy fails with MSB3027.

## Traps

- Screenshots are **post-tonemap**. Alpha ×1/×8/×64 read back 105/127/146.
  Never reason numerically about pixel values from a capture. `fx_pass.png` is
  the exception — it is the raw buffer straight off the GPU.
- **The sim is non-deterministic.** Two live runs differ. Pin with `freezefx`
  before comparing anything.
- `attach_*` names draw buffers but does **not** bind. Readbacks must restore
  the framebuffer binding.
- All 8 G-buffer attachments are in use; `gAUX_Color` only looks free.
- Codebase convention is that a pass **sets** what it needs and does not
  restore. The particle pass is the deliberate exception, because it runs
  mid-frame and `draw_fx` leaves a specific state that the base rings and the
  minimap inherit.
- Sampling an attached `gPosition` is a feedback loop.
- Python `'''` collides with VB `'''` doc comments in generated scripts; use
  `chr(39)*3` or `"""`.
