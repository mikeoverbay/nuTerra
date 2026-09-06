# Handoff — lamp lighting, shafts, and the light catalogue

Covers `54d98f95` .. `50b30724` plus uncommitted work. Written 2026-09-06.

Everything here was measured. Where a number appears, it came from a render or
a readback, not from reasoning — and several times the measurement contradicted
the reasoning, which is the reason for the rules at the bottom.

---

## 1. What shipped

| commit | what |
|---|---|
| `54d98f95` | lamp colour and level survive: peak-channel roll-off, gain 20→8 |
| `1ddf01c8` | lamp shadows — a baked depth cube per lamp |
| `7d49cc87` | `docs/shadows.md` |
| `a98e0453` | soft shadow edges, 12-tap Poisson disc |
| `d2dc9ced` | penumbra measured in **texels**, not metres |
| `6b4da145` | volumetric shafts — light in the air |
| `fa8c3ff3` | shaft intensity, own falloff, camera locked while capturing |
| `9d824326` | every light in the game found; the Light Bulb Placer |
| `50b30724` | placer list, measured panel placement, `docs/ui_panels.md` |

### The lamps themselves

`path_lights()` in `deferred.frag` had **no visibility term at all** — distance,
`N·L`, falloff, nothing else. Every lamp lit through every wall; a range-50 lamp
lit **65% of the frame**.

Two things were wrong beyond that, both the same bug in different passes:

- **Colour clipped to white.** `gColor` is Rgba8 and clips per channel, so an
  authored `1.00, 0.85, 0.63` drove red over 1 first, then green, then blue.
  Measured: hottest 200 px at `250.7, 250.1, 235.3` (gain 10) and
  `255, 255, 254.6` (gain 20), while the pool's mid-tones averaged
  `1.00 : 0.85 : 0.64` — exactly the file. The picker was choosing the colour of
  the *fringe*.
- **`level` looked ignored** for the same reason: two levels that both saturate
  give identical pixels.

Fixed by rolling off on the **peak channel** — one factor scales all three, so
the ratio, which *is* the colour, survives at any intensity. Same fix later
applied to the fog. Measured after: **zero clipped pixels**.

Gain 20 → 8, because doubling the light at gain 10 moved the pool **1.38×**
where 2.00× is linear; at gain 3 the same doubling measures **1.88×**.

### Shadows — `MapLampShadow`

One depth **cube** per lamp, baked at load. 512² a face, D16, 3 MiB a lamp,
**13-16 ms** for one. Nothing moves, so it is baked once and never re-rendered.

- **A cube, not a cone.** A lamp 6 m up with a 50 m range throws light almost
  horizontally at its rim; a 120° cone covers ~11 m of ground, leaving 39 m
  shining through walls. No cone is wide enough.
- **The 0..1 depth remap is NOT the sun's.** `M33 *= 0.5 ; M43 = (M43+1)*0.5` is
  right for an *orthographic* matrix where `w` is 1. Perspective divides by
  `w = -z`, so it becomes `M33 = M33*0.5 - 0.5 ; M43 = M43*0.5`. The wrong one
  gives depths that are monotonic, in range, and wrong everywhere.
- `t` in the encoding is the **major-axis** distance, not `length(d)`.
- **The self-test earns its keep.** `Bake()` reads the texel straight down and
  reports it against the lamp's authored height — one number covering face
  order, handedness, the remap and the encoding. It reads 6.52 m against 6.55 m.

Penumbra is a 12-tap Poisson disc measured in **shadow-map texels**, not metres.
A fixed metre radius spans a different texel count at every distance and bites
hardest close to the lamp, where texels are smallest and detail is finest — at
0.15 m it stopped being a soft edge and became lost detail. In texels the blur
scales with the thing it smooths.

### Shafts — `MapLampFog`

One additive sphere per lamp, after the fog, marching the view ray and testing
the shadow cube at each step. That test carves the beam. Four bugs, three silent:

- **CullFace was backwards.** Keeping the sphere's *near* faces means they fall
  behind the camera the moment it enters the lamp's radius — exactly where a
  shaft fills the screen. At 68 m it worked; at 18.5 m, just inside a 19.9 m
  lamp, the frame was **bit-identical** to the pass being off. Every "I don't
  see any beams" was this.
- **`BlendFunc(One, One)` never restored** → every later blended pass went
  additive and the minimap rendered solid white. Disabling blending does not
  undo the function.
- **No extinction.** A plain sum grows without bound with path length, so one
  gain could not work both inside and outside a lamp. Beer-Lambert, and *both*
  journeys matter: eye→sample and lamp→sample.
- **The debug view wrote `final_color` and returned before `outColor` was
  assigned** — an undefined, black frame. It had already been used to conclude a
  lamp was "walled in". **A diagnostic that fails to black is indistinguishable
  from the thing it measures returning zero.**

Tried and reverted: baking the light field into a 3D volume by compute. It
verified *exactly* — the two paths agreed to within 1/255 — at 0.8 MiB against 9
and under a millisecond. But 64³ over a lamp's bounding cube is ~0.6 m a voxel
and a lamp **pole** is 0.3-0.5 m. Below the resolution entirely. The bake is
kept; the march samples the cube.

### The catalogue

`nuTerra.exe scanlights=<path>` walks every installed space from **space.bin
only** — no terrain, no VT, no GL — and writes one table per map.

**72 scanned, 50 with lights, 12,733 instances, 129 models. 9,115 street lamps
in 19 models across 35 maps.**

Classifying took three attempts and needs **both** signals:

- **Filename alone** put 2,877 of 5,376 fires wrong: firewood, woodpiles, fire
  stairs, fire shields, a fire tower, and 210 **fireplugs**, which are hydrants.
- **Material FX alone** was worse — 24,446 — because a burning house *contains*
  volumetric fire sheets among its walls. Fixed by requiring **every** render set
  to be volumetric.
- Still not enough: `volumetric_effect.fx` is the shader for all GFX sheets, so
  foam, dust and oil came through.

Final rule: **a volumetric material AND a burning name.** `FX_glow`
(`glow.fx`, an unlit emissive card) is its own kind and is the closest thing in
the data to "this model emits light".

Two files, deliberately apart:

- `nuTerra/lights/light_catalogue.xml` — what a human tunes. One row per model,
  keyed on the `.primitives` path, the only identity that survives across maps.
  Carries `type` (point/spot/area), `yaw`/`pitch`/`cone`/`blend` for sconces and
  cowled lamps, plus colour, level, range, bulb, `vol_fade`, `vol_mix`.
- `nuTerra/lights/map_lights.xml` — machine-regenerated positions per map.

**Every value except name/kind/primitives is a guess.** `bulb` is 6.50 m for all
19 street lamp models, from one lamp on one map. That is what the Light Bulb
Placer exists to replace.

---

## 2. Uncommitted, and where it stands

`Window.vb`, `MapLoader.vb`, `modTypeStructures.vb`, `MapLampView.vb`.

**The Light Bulb Placer is mid-rebuild and deliberately stripped.** It is now a
splitter window: an empty left pane, a draggable 6 px bar, and a right pane
holding a GL surface sized to the pane. The four ortho views, cursor sliders and
readouts are in git at `50b30724` and come back into the right pane.

- **Preloading removed.** `build_lamp_mesh` is no longer called at map load. It
  is still there, uncalled, to be driven by a list selection.
- `LampMesh` / `LAMP_MESHES` — a slim position+normal copy per light model,
  24 bytes a vertex against the main `ModelVertex`'s 56. **A copy taken
  alongside the main upload, never instead of it**; main rendering is untouched.
  Must be taken at load, because the CPU arrays are `Erase`d the moment they
  reach the card.
- `MODEL_GEOM` was removed — the slim meshes replaced it.

### The trap that broke the whole UI, and the shape of the fix

`MapLampView.Render` bound framebuffer **0** on the way out and left depth test
on and blending off. Harmless while it early-returned with no model; the moment
it ran every frame it took the entire UI down — ImGui draws blended, depth test
off, into whatever framebuffer is bound, and the engine renders into MainFBO,
not 0.

Two changes, both needed:

1. `Render` now **saves and restores everything it touches** — framebuffer
   binding, viewport, blend, depth test, cull face — by asking what was there.
   Never assume 0.
2. The GL work moved **out of the UI-building pass** entirely. The panel records
   the pane size; `OnRenderFrame` renders before `_controller.Update`. One
   frame of lag on a splitter drag, invisible.

### Open

- Left pane list of the current map's lamps; click loads that model on demand.
- The lamp list came back **empty on 19_monastery**, which has lamps. Not yet
  diagnosed — likely the name test or the timing of `rebuild_lamp_list`.
- A loader that reads `light_catalogue.xml` and actually places the lights.
- **The X sign is unresolved.** The loader negates X for bounding boxes, so a
  lamp reads as `187.5` or `-187.5`. `map_lights.xml` stores it raw and says so.
  Settle it before auto-placement: place one light and see whether it lands at a
  pole or 375 m away.
- `209_wg_epic_suburbia` fails to scan — `'colorTex' was not present in the
  dictionary`, a material-parse bug predating this work.
- Occlusion, glow around the fixture itself, and `gColor` → Rgba16f.

---

## 3. Rules

These are not style preferences. Each one is here because ignoring it cost time.

**Walk the code before every edit.** Read the function you are about to change
and the ones it calls. `SHADOW_MAP_LOD = Math.Min(1, MAX_LOD_ID)`,
single-argument `Selectable` only, `Erase` on the CPU vertex arrays — none of
these are guessable and all of them change the answer.

**Anchors must be present *and* unique.** Dry-run every patch. An index-based
cut once deleted `ModelBatch`, the region markers and `DECAL_INDEX_LIST` because
`.index()` found the wrong `''' <summary>`. Use `tools/vbsplice.py`, which
refuses on 0 or 2+ matches and only ever does whole-string replacement.

**The `'''` collision.** VB doc comments end a Python triple-quoted string. VB
text goes in its own `.txt` file, written with a plain file write, and is
spliced by `tools/vbsplice.py`. It never appears inside a Python literal.

**Measure. Then measure the null control.** Reasoning was wrong repeatedly:
about the lamp being walled in, about the volume being smoother, about the cull
face. `lightgain=0` was a *bad* control — the loop still ran and did all the
work. A control must remove the thing, not scale it to zero.

**Distrust a diagnostic that returns zero.** Black output and a broken
diagnostic look identical.

**Verify the build landed, not that it exited 0.** Check the artifact — the
timestamp *or* a symbol. `.NET string literals are UTF-16`; an ASCII grep of a
dll reports MISSING for strings that are present.

**Never build the `.vbproj` with `-p:Platform=x64`** — it writes `bin\x64\Debug`,
which has its own `user.config` and therefore no game path, and stalls on a
modal dialog that looks exactly like a hung map load. Build the `.sln`.

**Change one multiplicative control at a time.** Dropping the shaft falloff
12→2 raises average scattering ~5×; raising gain 2.7× in the same edit made the
frame ~13× brighter than intended and blew out three lamps.

**`imgui.ini` beats `ImGuiCond.FirstUseEver`.** See `docs/ui_panels.md`.

**Restore global GL state.** Reversed-Z (`DepthFunc(Greater)`, `ClearDepth(0)`)
is global; the bakes switch it and must switch it back. So is `BlendFunc`, which
`Disable(Blend)` does *not* undo. Restore the framebuffer that *was* bound.

**Read the owner's snapshot BEFORE starting a render.** `snap`/`snapquit` write
`%TEMP%\nuTerra\snapshot.txt` too, and an agent render overwrites the camera he
just saved. It happened three times.

**Never invent an input he said he supplied.** A substituted camera is invisible
in the output — six renders were compared against the wrong view before he
spotted the angle. Say it is missing and stop.

**The owner pushes. The agent never does** — the shell has no key.
