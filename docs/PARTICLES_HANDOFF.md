# Card particle system — handoff

State as of the end of the atlas/UV session. Branch `particles-cardtest`, on
top of `da3d753`.

## Lockdown

Fire and the **volumetric smoke model FX** work and are locked. Nothing in
`shaders/Model_shaders/volumetric.{vert,frag}` or the `draw_fx` path changes
without walking the full call path and its state assumptions first. The card
particle system is a *separate* path and is not covered by the lockdown.

Corollary that cost time this session: the puffs visible above the monastery
house are drawn by the **volumetric FX mesh**, not by particle cards. Do not
attribute pixels to the particle path without an A/B — toggle
`PARTICLES_ENABLED` off and on at a fixed camera and diff the frames.

## Uncommitted work

Everything below is working-tree only. Nothing from this session is committed.

```
 M nuTerra/Forms/Window.vb
 M nuTerra/MapLoader/MapLoader.vb
 M nuTerra/Modules/modGlobalVars.vb
 M nuTerra/Particles/modParticles.vb
 M nuTerra/RenderEngine/modRender.vb
 M nuTerra/Scene/MapParticles.vb
 M nuTerra/shaders/Model_shaders/particle.frag
```

Builds clean — only pre-existing warnings (NETSDK1138, DotNetZip NU1903,
`IsMultiThreaded` obsolete).

Carried in from earlier in the branch, also uncommitted: the measured GL
save/restore (`GlState` / `SaveState` / `RestoreState`), the size-track fix
(track 5, not track 0), the atlas region ordering, and the `PARTICLES_WIRE`
debug switch.

**Neither of this session's two changes has been confirmed on screen.**

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

**`SPEED_GAIN = 4.0`** in `MapParticles.vb` is a hand-tuned fudge from before
the size track was fixed. Re-evaluate — it may no longer be needed.

**No depth sorting** for particle cards.

**Still unidentified:** tracks 0, 4, 7 (track 0 rises 0.66 → 3.07 typical);
the `999+216` bitfield; the `1000+84/+88` pair; the effect-id hash (not a path
hash — tested 10 algorithms across 19,039 strings).

**Stale comment:** `modParticles.vb` still describes the rect as stored "with
v in GL convention (0 at the bottom)". True of the stored value, but
misleading now that the unflipped upload is understood. Reword when next in
that file.

## Testing

Launch args (`Program.vb`): `<map> [cam=r,ax,ay,lx,ly,lz] [freezefx] [clean]
[half] [blackfx] [snap|snapquit]`. The `cam=` form is exactly what Snapshot
prints, so a view can be set up by hand, saved, and reproduced verbatim on
every later launch.

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
  Never reason numerically about pixel values from a capture.
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
