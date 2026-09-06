# Volumetric fog — what the field does, and where the lamp shafts sit

A reading of the published techniques against `MapLampFog` / `lamp_fog.frag`,
written 2026-09-06. Nothing here was rendered; it is literature plus a read of
the shader. Where a number is quoted it is from the cited source, and where it
is from memory it says so.

The shipped pass: one additive sphere per `.campath` lamp, drawn after the fog,
marching the view ray inside the sphere, testing the baked shadow cube at every
step, Henyey-Greenstein phase, Beer-Lambert extinction on both journeys
(eye→sample and lamp→sample), stopped at scene depth, jittered by a fixed 4×4
Bayer, peak-channel roll-off. See `HANDOFF_2026-09-06_lights.md` for how it got
there and `shadows.md` for the cube it samples.

---

## 1. Three families

### Froxel grids — the industry default

Wronski's Assassin's Creed 4 fog (SIGGRAPH 2014) and Hillaire's Frostbite
version (SIGGRAPH 2015). A camera-aligned 3D texture — roughly 160×90×64 in
Wronski's talk *as remembered from the slides*; Flax's reimplementation reports
150×80×64; Frostbite uses 8×8-pixel tiles by 64 depth slices — has scattering
injected per light (local lights as additive volumes, the sun through its shadow
map), then a compute pass accumulates extinction slice by slice, then a
full-screen pass looks the result up per pixel. Costs: ~1.1 ms on a GeForce 840M
(Flax), ~2.95 ms on PS4 at 900p (Frostbite: 0.45 voxelise, 2.0 scatter, 0.4
integrate).

Temporal filtering is built in: Halton-jittered sample positions inside each
voxel, reprojection of last frame's scattering and extinction, a 5–7% blend of
the new frame. The published cost of that is **trails** on moving lights and
animated media.

The published weakness is the one this project hit. Voxels are tens of
centimetres to metres across, so the guidance is to *blur shadow edges above the
froxel Nyquist limit* and accept soft results. Frostbite's "volumetric shadow
maps" are for the medium shadowing itself, not for thin geometry. A froxel fog
gives haze and broad shadowing from buildings; it was never meant to carve a
beam around a lamp fixture. The reverted 64³ per-lamp volume failed for exactly
the reason the literature predicts, and `bake_volumes` still running in
`MapLampShadow.Bake` is dead weight by that standard.

### Per-light ray march against a shadow map — what nuTerra does

Every published recipe has the skeleton `lamp_fog.frag` already has: march the
view ray, test the shadow map per step, weight by Henyey-Greenstein, attenuate
eye→sample and light→sample by Beer-Lambert, stop at scene depth. It is the
right family for fixture-carved shafts because the shadow map resolves ~0.08 m at
10 m where a froxel resolves ~0.6 m.

One trap the cube sidesteps: the Adam demo notes that exponential shadow maps
*leak light right behind the caster* and had to move to variance maps. The
hardware compare on a D16 cube has no such leak.

### Analytic single scattering — unused here

Sun, Ramamoorthi, Narasimhan and Nayar (SIGGRAPH 2005) give a closed form for
single scattering from an *isotropic point light in a homogeneous medium*,
depending only on distance to the light, viewing angle and extinction, evaluated
with a few small lookup tables. It has **no occluders**. Yusov (Intel, GDC 2013)
adds a semi-analytical point-light integral plus 1D min/max binary trees over the
shadow map so a march can skip spans that are entirely lit or entirely shadowed,
with epipolar sampling to shade far fewer pixels.

---

## 2. What the literature would change in `lamp_fog.frag`

Ranked by value for effort.

1. **Integrate each step; do not point-sample it.** The loop adds
   `scatter * trans * dt`. Frostbite replaced that with the integral of
   transmittance across the step,

   ```
   S * (1 - exp(-sigma_t * D)) / sigma_t        then  * trans
   ```

   At 48 steps and density 0.07 the difference is small, but it is what keeps
   the result stable when steps get long or density goes up, and it is two
   lines.

2. **The jitter choice is defensible, with a caveat.** The fixed Bayer keeps
   recorded flights from crawling, which is right: the literature's blue noise
   animated by the golden ratio per frame only works *with* a temporal filter to
   resolve it, and without one animation is grain. But a static 64×64 blue-noise
   texture beats a 4×4 Bayer spatially at the same step count — Bayer has 16
   levels, and the visible shade count is steps+1 either way.

3. **The falloff is not physical.** In-scatter from a point light falls as
   inverse-square times phase. The shader reuses the surfaces' windowed
   `1 / (1 + falloff·s²)` shape so shaft and pool agree about where light stops.
   A deliberate choice, and the reason no single gain can be matched to a
   physical reference. Know this before tuning further.

4. **Use the analytic airlight for the unshadowed part.** Lamps with no bake
   (`lamp_index = -1`) and the halo term inside a lit sphere are exactly the
   homogeneous, occluder-free case the closed form covers. That frees the march
   to spend every step on the shadowed beam.

5. **Half resolution with a depth-aware upsample** is how every shipped
   implementation pays for the march. The sphere pass is already bounded by
   screen coverage; this matters once several lamps fill the frame.

6. **Trails are the froxel family's problem, not this one's.** Static lamps and a
   per-frame march have nothing to reproject, which is one more reason the
   current approach fits recorded flights.

---

## Sources

- Wronski, *Assassin's Creed 4: Black Flag — Lighting, Weather and Atmospheric
  Effects*, Digital Dragons 2014 slides:
  <https://bartwronski.com/wp-content/uploads/2014/05/assassin_s-creed-4-digital-dragons-2014-no_notes.pdf>
- Wronski, *Atmospheric scattering and volumetric fog algorithm, part 1*:
  <https://www.gamedeveloper.com/programming/atmospheric-scattering-and-volumetric-fog-algorithm-part-1>
- SIGGRAPH 2014 Advances in Real-Time Rendering:
  <https://advances.realtimerendering.com/s2014/>
- Hillaire, *Physically Based and Unified Volumetric Rendering in Frostbite*,
  SIGGRAPH 2015:
  <https://www.slideshare.net/slideshow/physically-based-and-unified-volumetric-rendering-in-frostbite/51840934>
- Flax Facts #14, Volumetric Fog:
  <https://flaxengine.com/blog/flax-facts-14-volumetric-fog/>
- Unity Adam demo, volumetric lighting (ESM vs VSM note):
  <https://github.com/Unity-Technologies/VolumetricLighting>
- demofox, *Ray Marching Fog With Blue Noise*:
  <https://blog.demofox.org/2020/05/10/ray-marching-fog-with-blue-noise/>
- Heckel, *Real-time volumetric lighting with post-processing and raymarching*:
  <https://blog.maximeheckel.com/posts/shaping-light-volumetric-lighting-with-post-processing-and-raymarching/>
- Sun, Ramamoorthi, Narasimhan, Nayar, *A Practical Analytic Single Scattering
  Model for Real Time Rendering*, SIGGRAPH 2005:
  <https://history.siggraph.org/learning/a-practical-analytic-single-scattering-model-for-real-time-rendering-by-sun-ramamoorthi-narasimhan-and-nayar/>
- Yusov, *Practical Implementation of Light Scattering Effects Using Epipolar
  Sampling and 1D Min/Max Binary Trees*, GDC 2013:
  <https://gdcvault.com/browse/gdc-13/play/1017716>
- Yusov, *Outdoor Light Scattering Sample Update* (Intel):
  <https://www.intel.com/content/dam/develop/external/us/en/documents/outdoor-light-scattering-update.pdf>
