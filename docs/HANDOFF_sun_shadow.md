# Handoff — renderer state

Supersedes the previous version. Covers everything through the flicker hunt
(three real bugs found and killed), the outland block cull + weld hardening +
background decimation, the VT page debug tool, and the model-cull fixes - all
committed and owner-verified. Where it says "was", that is what the code did
before and why it changed.

The owner is the sole developer of nuTerra, a VB.NET / .NET 6 / OpenTK offline
World of Tanks map viewer. He guides tightly, tests every build himself, and is
usually right when he says something looks wrong. Believe the screenshot over
the reasoning - and believe his toggle experiments over your measurements: this
session his one-line observations ("I toggled outland and it stopped", "FFS it
is rendering the CSM shadows") ended hunts that instrumentation had circled for
hours.

---

## Where this left off

- Nine commits this session (plus two still unpushed from the previous one),
  all owner-verified, ending `42494c2` (README). The owner pushes from his
  terminal - the agent shell has no SSH key.
- The "settling-in lighting flicker on larger far away patches" is DEAD. It
  was three stacked causes, found in this order:
  1. z-fights between the welded outland sheet and far terrain (fixed: draw
     order + weld hardening below),
  2. the sheet rising honestly above ravine floors between weld texels and
     above the cascade's own encodable Y floor (fixed: exact-minimum tuck +
     Y-floor extension, audited to zero),
  3. **the real carrier**: old per-map settings files were silently
     re-enabling the PARKED live CSM cascades (`shadow_mapping=1` from before
     that checkbox was removed) - their every-4-frames camera refit pulsed
     the far-field shading during glides. modMapSettings no longer saves or
     applies that key. This was also why FPS was lower than the owner
     expected after moving to baked shadows - the scene was paying for both.
- The outland decimator is IN (the previously reverted idea, done right):
  prohorovka near 1.96M -> 125k tris, far 2.06M -> 621k, ~2.5 s in the
  background after load. Owner loved the adaptive wireframe.
- Verify on more maps when convenient (owner's list from before still
  stands): a snow map, a desert map, the two BIG epic maps (208/209,
  1024-chunk grids), and lakeville again since the new Y-floor logic and
  decimator touch it. Also still pending from two sessions back:
  hills_outland_smokes brightness, Abbey/Prokhorovka smoke, D-Day base smoke.

## How to work in this repo

- The repo is `C:\nuTerra`; the owner opens `C:\nuTerra\nuTerra.sln` in
  **VS 2022** and runs Debug|Any CPU -> `bin\Debug\net6.0-windows`.
- **Kill the running exe before building** (`Get-Process nuTerra |
  Stop-Process -Force`) - that kills the owner's live session, so batch
  changes before a rebuild.
- Build: `MSBuild.exe nuTerra.sln /t:Build /p:Configuration=Debug
  /p:Platform="Any CPU"` from `C:\nuTerra` (VS2022 Community path).
- `nuTerra.exe <space_name>` (e.g. `05_prohorovka`, `37_caucasus`) loads a
  map directly. Only agent launches use it; the owner's VS launches get the
  picker. Run with CWD = the bin folder (ShaderLoader resolves `shaders`
  from CWD).
- **Shader SOURCE is `nuTerra\shaders\`** (git-tracked); `bin\...\shaders`
  is build-copied output. Shaders only validate at runtime - launch and
  check stdout for `Shaders Built.` / `didn't compile`.
- **Snapshot writes `%TEMP%\nuTerra\snapshot.txt`** and flushes the buffered
  console log. It carries the cull buckets, the outland block counts
  (`outland blocks a/b+c/d`), the FX sort counter, `shadow mix: live=` (the
  tell that caught the CSM resurrection), glGetError, and names every FX
  model in view.
- **Committing from PowerShell 5.1**: `git commit -m @'...'@` breaks on any
  double quote in the message, and `Out-File -Encoding utf8` puts a BOM in
  the subject line. Write the message with `-Encoding ascii` and use
  `git commit -F <file>`.
- **Screenshot the live app** (the instrument that cracked the glacier):
  PowerShell Add-Type user32 `SetForegroundWindow` + `GetWindowRect` +
  `Graphics.CopyFromScreen`. It captures the SCREEN REGION - front the
  window first or you photograph whatever covers it. The owner will even
  frame the bug and say "take a screen shot". Synthetic input (SetCursorPos
  + mouse_event) can drive repeatable camera drags for A/B measurements.
- Per-map saved settings live in `%TEMP%\nuTerra\MapSettings\<map>.txt`
  (modMapSettings). See the trap table before trusting any "off" feature.

### Cracking game shaders (proven five times)

An `.fxo` in `shaders.pkg` is a ZIP; its `effect` entry holds DXBC blobs
findable by the `DXBC` magic (u32 total size at blob offset 24). Disassemble
with `fxc /nologo /dumpbin` from the Win10 SDK - full reflection: cbuffer
layouts WITH register defaults, texture bindings, asm. Variants compile per
bool material property; diff blobs to find what a flag selects.

### Offline space.bin analysis (Python, no launch needed)

Header table at 0x14: u32 count, then {char4 magic (readable as-is, do NOT
reverse), i32 ver, i64 off, i64 len}. `BWArray` = {u32 item_size, u32 count,
payload}. BWST = {12B entries {key, str_off, str_len}, blob after entries}.
BSMI = transforms(64B) / chunk_models / vis_masks(u32 game-mode bits) /
model_indexes; translation at matrix bytes 48-59. BSMO array order:
loddings, tbl_2, colliders, bsp_kinds, visibility_bounds(24B), model_info,
sounds, lod_loddings, lod_renders, renders(28B, verts_name_fnv at +16).
The WoT install is `C:\Games\World_of_Tanks_NA`; maps are
`res/packages/<map>.pkg` -> `spaces/<map>/space.bin`. This session's scripts
(vis-mask census, bounds dump) are ~80-liners re-derivable from this.

## Engine-wide traps

| trap | detail |
|---|---|
| **Per-map settings resurrect parked features** | modMapSettings saves/loads every `Fields()` entry. Remove a feature's UI without removing its Yield and OLD files silently re-enable it on load - the live CSM did this for weeks. When something "off" acts alive, grep `Fields()` and read the map's txt FIRST. |
| **Occlusion proxies invert inside the box** | cull-raster tests each model's AABB against model-only depth. Camera inside the box = near walls behind the eye, far walls behind the model's own surface: the model culls ITSELF, view-dependently. Guarded now (clip w < 1 -> visible); remember the failure shape for any future proxy test. |
| Clip control | `ZeroToOne` + `DepthClamp` + reversed-Z (`ClearDepth 0`, `Greater`). The CPU/GPU frustum extractions share one property: NO active far plane (REVERSE z' = w - z makes "far" the near shell and "near" vacuous) - distance can never cull, which the 21 km cascades rely on. |
| `gPosition` is VIEW space | Every writer names the varying `worldPosition`; every one stores `view * model * vertex`. (`t_mixer.vert` IS world.) |
| Shader includes | `#include "common.h" //! #include "../common.h"` - the `//!` half is an editor hint. |
| DX index flip | Universal triangle reversal; instance matrices mirror-conjugated at load - verified correct, do not "fix". |
| View-ray vs vertical | Water/depth comparisons measure the VERTICAL column, never the view ray. |
| OnUpdateFrame spins ~127k/s | `DELTA_TIME` is render-dt and wrong there by ~600x; update-loop timing uses its own Stopwatch (`rot_clock`). |
| GL globals leak between passes | Every pass restores what the next stretch assumes. Every indirect-draw site must bind its own DrawIndirectBuffer (all audited to do so; PageLoader's is bound by terrain_vt_pass upstream in the same stretch). |
| New ImGui windows go in Window.vb's UI pass | Always SetNextWindowPos/Size on first open (see draw_vt_debug_key). |
| VRAM pressure eats write-once caches | ~5.6/8 GB triggers eviction; evicted content is UNDEFINED. |
| GPU buffer readback demotes buffers | Route readbacks through ClientStorageBit staging (`parameters_temp`). CPU->GPU SubData writes are fine (outland indirect fill). |
| MaterialProperties is 288 bytes, exactly | Change common.h AND GLMaterial together. |
| Redirected stdout is buffered | Lines can sit unwritten for minutes. Snapshot flushes; diagnostics that must be read externally do `Console.Out.Flush()`. |

## OUTLAND (complete + culled + decimated, owner-verified)

The game-faithful rebuild from previous sessions stands (bake at load, ring
meshes, tilemap nibbles `(idxA,idxB,wA,wB)`, tileScale = metres/repeat,
`1-u` mirror NEVER `fract(-u)`, AG normal decode, -1.5 VS sink). New this
session, in draw order of importance:

- **Draw order**: the outland draws AFTER the playfield terrain
  (modRender). With strict Greater, terrain wins every equal-depth tie by
  construction, and early-Z eats the tucked-under sheet for free.
- **Weld hardening** (patch_outland_heightmap): every welded texel now
  tucks under the EXACT terrain minimum of its bilinear support - every
  terrain board vertex (100/64 m grid, anchored at the footprint corner so
  stepping it hits vertices exactly) is min-scattered into the texels it
  can influence. Point sampling had missed narrow ravines by 8+ m. Lip
  0.15 m at the footprint line -> 0.75 m by 10 m inside.
- **Y-floor extension**: a cascade's authored Y range can sit ABOVE the
  terrain's deepest spots (prohorovka's gorge is ~10 m below it); the tuck
  encode clamped at 0 and the sheet rode its floor through the valley -
  the tell was `pxL=0` in the audit detail. The floor now extends below
  the terrain minimum and the whole map re-encodes; the draw follows
  `theMap.near_y_offset/height` automatically.
- **Crossing audit prints on every load** and is the invariant's guard:
  `outland crossing audit: 0 midpoints above terrain`. Any nonzero count
  is pixels that can z-fight the playfield - treat as a regression.
- **Block frustum cull**: ring indices are emitted in 64x64-quad blocks
  (OUTLAND_CULL_BLOCK) with exact world-XZ bounds from the builder's own
  affine; Draw_outland tests BoxInFrustum per block and issues survivors
  via a small CPU-filled indirect buffer, one MultiDrawElementsIndirect
  per cascade. Wire view draws the same survivor set. Snapshot line shows
  drawn/total.
- **Background decimation** (OutlandDecimator.vb): per cull block, greedy
  edge collapse under an area-normalized quadric threshold with SUBSET
  placement - survivors are original grid vertices, so the result is ONLY
  a new index buffer; the shared vertex buffer and VS height sampling are
  untouched. Frozen: block borders (cull + crack-free seams survive) and
  the whole weld band (the audit invariant survives). Runs as a Task
  kicked at the end of the weld; full grid draws until Draw_outland swaps
  buffers on the GL thread; generation-guarded against map changes.
  Prototyped OFFLINE first (scratchpad decimate_proto.py pattern) because
  the earlier in-load QEM attempt froze the load twice - that rule paid.
  Knobs: `OUTLAND_DECIMATE` (on), `OUTLAND_DECIMATE_EPS` (0.25 m, far
  cascade x2). Prohorovka: near -93.6%, far -69.8%. No UI checkbox yet -
  offered, owner has not asked.
- A raw 16-bit dump of the patched near heightmap goes to
  `%TEMP%\nuTerra\outland_height_near_patched.raw` (header: i32 w,h; f32
  y_off, y_range, scale.xy, center.xy; then u16s) for offline mesh work -
  the PNG dumps are 8-bit and useless for geometry.

Open threads unchanged: detailAlbedoSml still the 1x1 neutral stand-in
(TerrainSettings1.noise_texture the candidate); lakeville's authored
R-channel cutout waiting on its alphaReference; BWWa bodies ~70% unparsed;
the game's to-horizon sea is water_clipmap_instanced.

## Models / culling

- cull.comp does frustum only (planes from per-instance MVP, correct for
  the mirrored matrices; no distance cull possible - see clip-control
  trap). The raster occlusion pass zeroes commands via visibles[] and now
  carries the inside-the-box guard in cull-raster.geom.
- `Array.Resize(MODEL_INDEX_LIST, j)` - the old `j - 1` silently dropped
  the last accepted BSMI instance on every map.
- MapLoader has a LOD-table audit (prints only anomalies; dday and
  caucasus print none): a lods() row with draw_count 0 vanishes a model in
  that distance band; fewer rows than authored lods sends far bands past
  the batch. `lod.junk` is never set anywhere - dead flag.
- Instance vis-mask filter keeps CAPTURE_THE_FLAG only; on dday that drops
  6 DOMINATION-only instances (inside the field) and nothing outside.

## VT (virtual texture) - measured facts and tools

- **Page debug view** (Settings -> VT -> "Page debug view"): tints the LIT
  terrain by the resident page's mip (SampleTable's .y, fallbacks
  included), page-cell checkering, colour-key window with overlay on/off
  and a blend slider, "Show mip blend" (mipfract greyscale), and "Nearest
  mip (test)" which snaps trilinear in REAL rendering. The colours in
  Window.vb's VT_DEBUG_COLORS must match VTDebugColor in common.h.
- `VT_BAKE_TRACE` (default off) logs each bake's page identity + per-frame
  request/touched/toload/bias + camera.
- Measured: a truly still camera is CLEAN - fixed request set (~270-400
  pages), zero re-bakes over minutes. One 150 px orbit gesture re-bakes
  ~108 far pages over the ~3 s glide at UPLOADS_PER_FRAME=1 (the "Uploads
  per frame" slider raises the refinement rate live).
- MipBias sits pegged at MAX (6): the adaptive backoff can never fire
  (`touched >= num_tiles` needs 2048 unique pages/frame; a 32x32 feedback
  plus parents caps near ~1400). Effectively dead code, documented not
  fixed. The draw shaders still do NOT subtract MipBias (feedback at 32x32
  resolution is ~6 mips coarser, which the bias of 6 cancels) - still the
  first item of the old VT re-apply list if sharpness work resumes.
- The grazing-angle band at the horizon requests near-finest pages (the
  minor-axis aniso MipLevel) - the debug view shows it as an orange (mip
  1) fringe. Known, not currently a problem.

## Shadows

- Baked map-wide sun shadow is the ONLY caster path. The live CSM cascades
  are PARKED: no UI, off at startup, and modMapSettings no longer
  saves/applies `shadow_mapping` (see trap #1 - restoring the controls
  means re-adding that Yield AND the checkbox). MSM behind the A/B toggle;
  `terrain2/horizonshadows` still uncracked.
- Camera: OrbitControls damping on rot_clock; rotation now has the same
  rest snap zoom/pan always had (measured: does not change VT bake
  trickle; kept as hygiene).

## FX / lightonly / PBS_tiled_global — unchanged

All previous-session verifications stand: sorted FX bucket with 10 m
hysteresis, real variant selection via alphaFreshnelEnable, distance fade
`sat(sat((d-min)/(max-min)) + base)`, verified lighting math, softFactor
unused, heat haze still the top FX item (needs backdrop distortion).
lightonly split on alphaTestEnable; PBS_tiled_global as transcribed.

## One piece of advice

Six revisions of this file and the rule has only sharpened: every real bug
fell to an instrument, never to an argument. This session's additions, in
the order they were paid for: when a fix changes nothing, SUSPECT YOUR
PROBLEM STATEMENT, not just your code - the flicker survived two correct
fixes because it was three problems wearing one symptom. The owner's
toggles out-instrument your measurements: give him levers (the debug view's
checkboxes) and read what he reports back literally - "CSM shadows render
though shut off" was the whole answer. A false lead ("outland off stops
it") is still data - it localized the z-fights that were real bugs even if
they were not THE bug. When an audit prints a weird detail (pxL=0), that
detail IS the root cause talking. And the offline-first rule for heavy mesh
work went from advice to proof: the decimator that froze the load in-engine
shipped clean the same day it was prototyped in 150 lines of numpy.
