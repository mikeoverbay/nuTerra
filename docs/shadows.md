# Shadows

Three casters live in this renderer. Two are baked and current; one is parked.

| method | class | status | casts | cost |
|---|---|---|---|---|
| **Baked sun map** | `MapSunShadow` | the only sun caster | terrain, static models, trees | one ortho depth render at map load |
| **Baked lamp cubes** | `MapLampShadow` | current | same three, per `.campath` lamp | 6 faces per lamp at load — 13-16 ms measured for one |
| Live CSM cascades | `ShadowMappingFBO` | **PARKED** | historically trees only | per-frame, every 4th frame |

Both bakes exist for the same reason: **nothing they shadow moves.** Terrain and
static models are fixed, the sun is fixed per map, and the lamps are authored in
Path Studio. The per-frame shadow re-render a general engine needs is wasted
work here, so the cost is paid once at load and the per-frame cost is the taps.

---

## 1. Baked sun map — `MapSunShadow`

One orthographic depth render of the whole map from the sun, sampled per frame
in `deferred.frag`.

- **Box** fitted to the map's silhouette *at the current sun angle*, then
  squared up. A non-square box on a square texture gives anisotropic texels and
  the coarse axis is what a shadow edge staircases along. Near/far bracket the
  geometry rather than standing three times deeper than it — that is what left
  a 16-bit buffer with about eleven usable bits.
- **Size** comes from `TARGET_TEXEL` (0.05 m), *not* `MAX_SIZE` — that only
  caps it. Then it steps back down a power of two at a time against a VRAM
  budget: 25% of total **and** 50% of what is actually free when the bake runs.
  The free-memory test matters because a quarter of an 8 GB card is exactly what
  32768 costs, with the VT atlas and models already resident.
- **D16**, deliberately. With near/far fitted, the range is ~1988 m, so a level
  is ~0.03 m — finer than the texel footprint at any size this reaches. 32F was
  buying precision nothing downstream could resolve, at double the memory.
- **MSM** behind an A/B toggle: four power moments in RGBA32F, blurred and
  mipmapped, capped at 4096. Moments must be cleared *separately* to
  `(1,1,1,1)` — the depth clear does not touch the colour attachment, and
  undefined memory there does not stay local: the blur and mip chain spread it
  over every texel.
- **Outland excluded.** Those are the distant cliffs ringing the arena, 160 m
  tall on Abbey; enclosing them blows the box up and costs texel density
  everywhere that matters. It buys no depth precision — the box is fitted from
  terrain heights, so no model ever influenced near/far.
- **Re-bake** whenever the sun moves. The result is valid for one sun direction.

### Why it is sampled, not baked into VT pages

It started as a bake into the terrain's virtual-texture pages, and that was
reverted. A page is built once, long before anything is drawn on top of it, so a
shadow written there landed **ahead of the projected decals** and **ahead of the
ambient/direct split** — which is what made shade read as black instead of
sky-lit — and it could not reach the static models at all. Sampling in
`deferred.frag` costs four taps per lit pixel. The old way was free and wrong in
three places.

This precedent generalises: **"paint it into the surface" keeps landing at the
wrong point in the pipeline.** See also the note on lightmaps below.

---

## 2. Baked lamp cubes — `MapLampShadow`

One depth cube per `.campath` lamp. Before this, `path_lights()` had no
visibility term of any kind — distance, `N·L`, falloff, and nothing else — so
every lamp lit through every wall. Measured, a range-50 lamp lit **65% of the
frame** instead of making a pool.

### A cube, not a cone

A lamp 6 m up with a 50 m range throws light almost horizontally at the edge of
its own radius. A 120° cone from that height covers about 11 m of ground, so the
outer 39 m of the pool would still shine through walls. There is no cone wide
enough — it is the whole sphere or nothing.

### Layout and cost

- One cube per lamp, `TextureCubeMapArray`, `DepthComponent16`, hardware compare
  (`CompareRefToTexture` / `LEQUAL`), so one fetch is already a 2×2 PCF average.
- `FACE_SIZE` 512 → **3 MiB per lamp**. Only as many layers as there are lamps
  are allocated, so 32 lamps would be 96 MiB and a map with no lamps pays
  nothing — `Bake()` returns on the empty list before allocating.
- Texel footprint: ~0.2 m at 50 m, ~0.05 m at 12 m. A lamp pool is soft-edged by
  nature, so resolution buys less here than it does for the sun.
- Far plane is **the lamp's own range**; near is `NEAR_M` = 0.5 m. Perspective
  depth spends most of its precision just past the near plane, so this wants to
  be as far out as the geometry allows — at 0.1 the far half of a 50 m range
  loses most of its resolution.

### The remap trap

The sun bake converts its projection to 0..1 depth with

```
M33 *= 0.5 ; M43 = (M43 + 1) * 0.5
```

That is correct **only for an orthographic matrix**, where `w` comes out as 1 and
`z_ndc` is a plain `z*M33 + M43`. A perspective matrix divides by `w = -z`, so
the substitution has to happen inside the quotient:

```
z01 = 0.5*z_ndc + 0.5 = (z*(0.5*M33 - 0.5) + 0.5*M43) / -z
→   M33 = M33 * 0.5 - 0.5 ; M43 = M43 * 0.5
```

Using the ortho form here yields depths that look entirely plausible —
monotonic, in range — and are wrong everywhere. Worked through, the stored value
is

```
z01 = (F - F*N/t) / (F - N)
```

where **`t` is the distance along the major axis**, not `length(d)`. That is what
the face's own projection saw, and `path_lights` inverts exactly this expression
to build its comparison reference.

### The self-test

`Bake()` ends by reading the texel straight down from a lamp and reporting the
distance it holds next to the lamp's authored height above the terrain — the
same number by definition. That single comparison covers **face order, the up
vectors' handedness, the perspective remap and the depth encoding at once**:

```
lamp shadow: lamp 0 ground below reads 6.52 m, authored height 6.55 m
```

The 3 cm is D16 quantisation plus the polygon offset. Do not remove this check;
a rendered frame only looks *slightly* off when any of those four is wrong.

It has already earned its keep for a second reason: it stayed correct while the
first test lamp came out completely black, which proved the lookup was fine and
the lamp was simply walled in. A correct shadow on a badly placed lamp is
indistinguishable from a broken shadow until something says otherwise.

### Bias

Two controls, doing different jobs:

- `LAMP_SHADOW_BIAS` — constant, in the 0..1 stored encoding. Raise if lit
  surfaces stipple.
- `LAMP_SHADOW_NORMAL_BIAS` — metres along the surface normal, and the one that
  matters. A depth bias has to grow with the slope to stop acne on grazing
  surfaces, and by the time it is large enough there, the contact shadow has
  lifted off everywhere else. Moving the *sample point* off the surface fixes
  the grazing case and leaves face-on alone.

Bake-side, `PolygonOffset(2.5, 8.0)` — steeper than the sun's `1.5, 4.0`,
because a lamp sits metres from what it lights rather than kilometres, so the
same depth slope covers far fewer texels.

### Penumbra

`LAMP_SHADOW_SOFT` is the blur radius **in shadow-map texels**, sampled as a
12-point Poisson disc in the plane perpendicular to the lookup. `sd` runs from
the lamp to the pixel, so adding a perpendicular vector of length *s* moves the
sampled point *s* metres sideways at that pixel's distance — world units, no
division needed — and the radius is built from the texel size at that distance:

```
radius = LAMP_SHADOW_SOFT * t * 2/FACE_SIZE
```

**Texels, not metres, and this was got wrong first.** The artefact is a
texel-scale staircase, so the cure has to be texel-scale. A fixed metre radius
spans a different number of texels at every distance, and worst where it hurts
most: texels are *smallest* close to the lamp, which is exactly where the shadow
detail is finest. Shipped at 0.15 m it stopped reading as a soft edge and
started reading as lost detail. In texels the blur scales with the thing it is
smoothing.

The reference depth is deliberately **not** recomputed per tap. That is what
makes it a PCF average of one receiver depth against twelve neighbouring
occluder depths, rather than twelve separate shadow tests. The slope error that
leaves is what the normal bias is for.

The disc is irregular and **not** rotated per pixel. A regular ring of the same
count bands visibly along a soft edge, and the usual fix — rotating by a
per-pixel hash — trades banding for grain that *crawls* as the camera moves.
These are recorded flights, so a fixed irregular set is the right trade.

Not PCSS: the blur does not widen with the occluder's distance. That needs a
blocker search per pixel, and constant width is the part of a penumbra that
actually reads at these throws.

Measured over three lamps: **no cost at all** — 177 fps with the taps and 177
without. `lampsoft=0` restores the single fetch, which makes the control its own
A/B and its own cost measurement.

1 texel is a gentle smoothing on top of the hardware 2×2; past about 3 it starts
to spread rather than soften.

### Re-baking

`MapCamPath.Load` sets `lights_dirty`; `modRender` checks it at the top of the
frame and re-bakes. Deliberately a flag rather than a call from each of the four
places that re-read the file (`FLY`, `Show Path`, `Show Lights`, `Reload Cam
Path`) — those are UI code, a bake binds its own framebuffer and rewrites global
depth state, and the fifth caller that forgets is only a matter of time.

Re-baking is per lamp, so nudging one lamp in Path Studio costs one lamp's work.

### What it cannot do

- **Nothing dynamic.** Vehicles and particles cast nothing.
- **No bloom or glow** around the lamp fixture itself — separate problem.
- A lamp inside or behind geometry goes fully dark, correctly, and looks exactly
  like a bug. Check the debug view before assuming the shadow is broken.

---

## 3. Live CSM cascades — parked

The pass, FBO and shaders are all still here and still work, but nothing calls
them by default: off at startup, and `modMapSettings` no longer saves or applies
`shadow_mapping`. Restoring the controls means re-adding that Yield **and** the
checkbox. `terrain2/horizonshadows` is still uncracked.

`mDepthWrite_light.geom` belongs to this path — `invocations = 4`,
`lightSpaceMatrices[gl_InvocationID]`, `gl_Layer`. It is a layered depth render
and would be the template if the lamp cubes ever need all six faces in one pass
instead of six.

---

## Traps shared by both bakes

1. **Reversed-Z is global.** The engine runs `DepthFunc(Greater)` with
   `ClearDepth(0.0)`. Both bakes switch to `Less` / `ClearDepth(1.0)` and **must
   put them back**. Leaving `ClearDepth` at 1.0 makes every later clear fail
   `Greater` and the whole scene disappears behind the sky.
2. **`CullFace` off.** WoT models are hollow shells with no bottom faces; with
   culling on, a lamp beside a building reads the inside of its roof.
3. **A shadow sampler must always be bound.** Leaving one unbound is not a way to
   switch a feature off, it is an illegal GL state the driver reports on every
   draw (131222, *shadow sampler on a non-depth texture*). Hence `dummy_shadow()`
   and `dummy_lamp_shadow()` — 1×1 stand-ins that are never read, gated in the
   shader by `has_sun_shadow` / `lamp_shadow_count` being 0.
4. **Texture units are nearly full.** 7 cascades, 8 sun depth, 9 sun moments,
   11 SH grid, **12 lamp cubes**. `unbind_textures(n)` at the end of the deferred
   pass must cover them — it was 12 and left unit 12 bound into the next pass.

---

## Controls

**Menu → Shadow Mapping**

| control | effect |
|---|---|
| Cascades (live) | the parked CSM path |
| Baked (map-wide) | the sun bake; toggling re-bakes |
| ↳ store as moment maps (A/B) | MSM vs PCF, same bake either way |
| ↳ moment bias | raise if reconstruction goes unstable over flat ground |
| **Lamp shadows (baked)** | the lamp cubes; off keeps them allocated and stops sampling, so it is a free A/B |
| ↳ lamp depth bias / lamp normal bias | see Bias above |
| ↳ lamp penumbra (m) | blur radius at the receiver; 0 is one fetch and a hard edge |

**Menu → Flight Recorder → Reload Cam Path** re-reads the route and its lamps
after a Path Studio save, and the cubes re-bake on the next frame.

**Headless** (see `Program.vb`)

| argument | effect |
|---|---|
| `nolampshadow` | the null control for the whole feature — a run with and without differ only by the shadow term |
| `lampdebug` | draw the shadow term itself: white visible, black occluded, blue out of every lamp's range |
| `lampbias=` / `lampnbias=` / `lampsoft=` | sweep the biases and the penumbra without a rebuild |
| `lightgain=` / `falloff=` | the lamp intensity controls, same reason |

A lamp pool that comes out wrong is two questions — *is light reaching here* and
*is the shading of it right* — and only `lampdebug` separates them.

---

## Not done: lightmaps

Painting lamp shadows into the buildings' textures comes up because the UV set
for it already exists — `model.frag` uses UV2 for a per-model blend mask, which
has to be a clean non-overlapping unwrap.

It does not work here, and the blocker is not UVs but **instancing**. 19_monastery
draws 6,179 model instances from 253 batches — about 24 copies of each mesh
sharing one material and one set of bindless textures. Texture painting is
indexed by *surface*; a shadow is a property of *placement*. Painting a lamp's
shadow into a building paints it into all 24 copies of that building across the
map. A shadow map survives instancing precisely because it is indexed by world
position instead.

Doing it properly means a per-*placement* chart, an atlas packed over all 6,179
of them, and a re-bake of that atlas every time a lamp moves — which is the
opposite of the current iteration loop.

The terrain is the exception: it has a unique global XZ→map UV and no instancing,
so painting pools onto the ground is tractable. Given the VT-page precedent
above, that would want to be a separate overlay sampled in `deferred.frag`, not
baked into the pages.
