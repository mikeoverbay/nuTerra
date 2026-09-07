# How the game renders water

Decoded 2026-09-07 from `shaders.pkg`, `shaders/water/*.10.dx11.fxo`, by the
route in `terrain_blend.md` ("Reading the game's shaders"). `tools/fxo_dump.py`
does the extraction now - it lists package entries, pulls the DXBC blobs out of
an `.fxo`, dumps the header strings, and disassembles each blob with `fxc`.

    python tools/fxo_dump.py list water/
    python tools/fxo_dump.py strings water/gbuffer_flat.10
    python tools/fxo_dump.py dump    water/gbuffer_flat.10  <out_dir>

**This is the game's shader, not ours.** It is a specification and evidence, in
the same class as `game_PBS_tank.md` and `game_deferred_decal.md`. Nothing here
is implemented in nuTerra.

Every claim below is marked. **MEASURED** means read out of the disassembly.
**INFERRED** means read off a name, a size or a binding without following the
arithmetic - treat those as leads, not facts.

## The headline

Water is not a transparent surface blended over the frame. It is a **deferred
surface with its own G-buffer**, lit in a **separate half-resolution pass** that
has the sun shadow atlases, the cloud shadows and a hierarchical Z map bound,
and composited afterwards. Twenty effects take part.

## The passes

`shaders/water/`, the `.10` (deferred) builds, by size. Sizes are MEASURED, the
roles are INFERRED from names and bindings except where noted.

| effect | bytes | what it looks like |
|---|---:|---|
| `water_clipmap_instanced` | 1321155 | the surface for the clipmap (distant/ocean) path |
| `water_flat` | 1278525 | the surface for a flat water plane - the lake case |
| `half_screen` | 435452 | **lighting and shadowing at half res** - 193 blobs |
| `gbuffer_clipmap` | 114035 | clipmap surface into the water G-buffer |
| `gbuffer_flat` | 50155 | **flat surface into the water G-buffer** - decoded below |
| `forward` | 25654 | a forward fallback |
| `make_stencil` | 22257 | stencil mask marking water pixels |
| `colored_clipmap` / `colored_flat` | 20668 / 18928 | flat-coloured variants |
| `water_probe` | 20509 | probe/reflection capture |
| `depth_flat` | 19799 | depth-only prepass |
| `wet` | 19512 | wetness on the shore |
| `composition` | 19376 | the final blend - **blobs are 25 lines**, so it is a copy, not the shading |
| `ramp` | 18383 | depth ramp lookup |
| `under_water` | 18016 | the view from beneath |
| `copy_back_buffer` | 17988 | grabs the scene for refraction |
| `excluded_water` | 16625 | the exclusion volumes that cut water out |
| `terrain_height_renderer` | 16496 | terrain height under the water |
| `surface` | 14615 | - |
| `water_depth_map` | 14514 | water depth from the two heights |

## The water G-buffer (`gbuffer_flat.10`, blob 05) - MEASURED

Nine blobs: one `vs_4_0` and **eight `ps_4_0` permutations** of ~1400
instructions each. Blob 05 is the largest.

The pixel shader's whole input is:

```
SV_Position
TEXCOORD0.xyz      the world position
```

and it writes **three render targets**:

```
o0.xyz = g_viewMat * worldPos          VIEW SPACE POSITION, not albedo
o0.w   = <scalar, not traced>
o1.xyz = normalize(...)                the surface normal
o1.w   = 0
o2.xyzw = float(waterID) * (1/255)     an ID buffer, replicated to 4 channels
```

`cb3` is `PerView` and `cb3[0..3]` is `g_viewMat` - confirmed against the
cbuffer table, so `o0.xyz` really is a transformed position. `waterID` is an
`int` at offset 784 of `$Globals`, read as `cb0[49].x`.

That third target is the interesting one: **every water body carries an id**,
written per pixel, so a later pass can look up which water's parameters apply
to a given pixel. Nothing in nuTerra has an equivalent.

### What builds the surface - MEASURED

Seven textures, seven samplers:

| slot | texture |
|---|---|
| t0 | `g_flowMapAmplitudes` |
| t1 | `g_flowMapDirections` |
| t2 | `g_waveHeightTexture1` |
| t3 | `g_waveHeightTexture2` |
| t4 | `g_normalMap1` |
| t5 | `g_normalMap2` |
| t6 | `g_afAtlas3` (with `cb4` = `ActionFieldConstants`) |

The sampling order in the disassembly:

1. `t0` and `t1` are sampled **once, at the same UV** - the flow field is read
   first, as a direction and an amplitude.
2. `t4` and `t5` are then sampled **six times each**, at six different UVs
   derived from that flow. This is flow-map advection - the standard cure for
   a scrolling normal map's visible repeat, where several time-phased samples
   are cross-faded so the texture appears to move *along* the flow.
3. `t2` and `t3` are sampled with `sample_l` - an **explicit LOD**, computed
   rather than derived from screen derivatives.
4. `t6`, the action field atlas, is sampled last and only under a branch.

So the normal is not a scrolled tile. It is a flow-mapped, six-tap, two-layer
construction with wave height on top and a dynamic deformation field over it.
`ActionFieldConstants` and an "action field" atlas are almost certainly the
wakes and shell splashes - INFERRED, from the name and from it being the only
input that could carry transient per-object disturbance.

## Where water is lit (`half_screen.10`) - MEASURED bindings

193 blobs. The largest binds:

```
t0  g_asmDistantIndex      t1  g_asmIndex          adaptive shadow map indices
t2  g_flowMapAmplitudes    t3  g_flowMapDirections
t4..t7  g_waveHeightTexture1, 2, 21, 22            FOUR wave height maps
t8  g_afAtlas3                                     action field
t9  g_cloudsShadowMap0Sml  t10 g_cloudsShadowMap1Sml   animated cloud shadows
t11 g_hierarchicalZMap                             screen-space tracing
t12 g_asmDepthAtlas        t13 g_csmDepthAtlas     sun shadows
```

Three things worth noting. The pass runs at **half resolution** - the name says
so and the cost of 193 permutations explains why. It has **both** shadow
systems bound, ASM and CSM, so water is shadowed by the same machinery as
everything else rather than being left unshadowed. And a **hierarchical Z map**
is bound, which is what a screen-space reflection or refraction march needs.

## Parameters worth knowing about

From the header strings of `water_flat.10` and `gbuffer_flat.10` - the names
are MEASURED, the meanings INFERRED:

```
g_flowMapParameters, g_flowMapParameters2   flow rate / period / phase
g_useFlowmap, g_useWave2                    permutation switches
g_foamEnabled, g_foamContrastScale
g_foamScrollAndFlowParameters
coastFoamMap, g_foamTiled                   two foam sources: coast and tiled
g_foamParticles, g_underWaterParticles      particle layers, above and below
g_rampDepth, g_softDepth                    depth ramp, soft intersection
waterDepth, g_depthBasedEffectsTexture
reflectionMap, reflectionTexture
waterReflectionAttenuation
g_underWater
smallWavesTexture
g_staticDirtNormalMap
m_color, m_colorEdgeFog, m_scatterColorSunExp
g_fogColorAndInvDepth
g_normalsGGXRough
waterID
```

`m_scatterColorSunExp` and `m_colorEdgeFog` say the water colour is a
**scattering** model with a sun-facing exponent and a separate edge-fog colour,
not a flat tint - INFERRED from the names, not followed through the assembly.

`g_normalsGGXRough` says the surface uses **GGX**, the same specular model the
rest of the game's PBR uses.

## Why ours reads too light and too transparent - MEASURED

The owner's two complaints have one cause between them, and it is not a
constant that needs tuning.

**Their water is lit with the scene. Ours is painted on after it is finished.**

```
deferred.frag:1579   final_color = mix(final_color, f_color, ...fog_level)   FOG
deferred.frag:1592   outColor    = correct(final_color, tonemap_exposure)    TONE CURVE
modRender.vb:253     SSR            "after the resolve"
modRender.vb:278     water.draw()   "forward over the lit frame"
```

By the time `MapWater.draw` runs, `gColor` has already been fogged and put
through `correct()`. It is display space. The water then alpha-blends an
authored colour straight over it, and so receives **no exposure, no tone curve
and no distance fog** - none of the three transforms every other surface in the
frame went through.

That is the whole of "the colour is wrong, too light":

- Lakeville's body authors `deep_color = (0.19, 0.51, 0.66)` (MEASURED, from
  the load log). In the game that number is an input to a scattering model in
  linear HDR which the composition pass then tone-maps. Here it is used as the
  final display-space colour: `col = mix(deep_color.rgb, refl, F)`,
  `water.frag:171`, never linearised and never tone-mapped.
- Distance fog is the visible half. The scene fades toward the fog colour with
  distance; the water does not, because the fog was applied a pass earlier. So
  a far lake stays vivid while the land around it goes flat, which reads as the
  water being too bright even when the near water looks right.

And "far too transparent":

- Our opacity is a hand-picked constant - `alpha = clamp(0.72 + 0.28 * F)`,
  `water.frag:175`.
- The depth extinction that should be driving it (`column = exp(-water_depth *
  fog_inv_depth)`, `water.frag:282`) is real and correctly modelled on
  `g_fogColorAndInvDepth`, but it sits inside `if (scene_v.z < 0.0)` - it only
  runs where the G-buffer holds a scene position. It is a correction applied to
  a guess, rather than the thing that decides opacity.
- The game does not have an alpha at all. Transmittance is
  `exp(-depth * invDepth)` against the **refracted** scene - hence
  `copy_back_buffer` - evaluated in HDR before any tone curve, with depth from
  a whole sub-pipeline (`water_depth_map`, `terrain_height_renderer`, `ramp`,
  `g_rampDepth`, `g_softDepth`) rather than from whatever the G-buffer happens
  to hold.

There is also a model gap underneath the plumbing. Theirs has
`m_scatterColorSunExp` (a sun-facing scattering exponent), `m_colorEdgeFog` (a
separate edge colour) and `g_normalsGGXRough`. Ours is one flat `deep_color`
lerped toward the reflection by Fresnel, so our water colour does not change
with sun angle, view angle or depth - only its opacity does.

**The honest fix is architectural**: water has to be resolved with the scene
rather than after it, so it inherits exposure, tone curve and fog. Short of
that, anything done to `deep_color` or to the alpha constant is compensating in
display space for a transform that was never applied, and will only hold at one
exposure and one distance.

## What this means for nuTerra

Ours is a different architecture, so none of this drops in. The gaps this
decode makes concrete, worth weighing against `open_threads.md` sections 6 and 7:

- **Flow maps.** The game advects its normals along an authored flow field with
  six taps. If a map ships flow map textures, they are currently unread.
- **A water id per pixel.** Their composition can vary parameters per water
  body; we have one global water.
- **Water is shadowed.** Both shadow atlases are bound in their lighting pass.
- **Two foam systems**, coast and tiled, plus particle layers above and below.
- **Depth ramp** (`g_rampDepth`, `g_softDepth`, `ramp.fxo`, `water_depth_map`,
  `terrain_height_renderer`) - a whole sub-pipeline for water depth, which is
  what drives shore colour gradients and soft intersections. `open_threads` §7
  records our rim as hard-edged; this is how the game avoids that.

## Not done

- `o0.w` in the water G-buffer is not traced. It is one `mad` from three
  registers and would take an afternoon to follow back.
- `water_flat` and `water_clipmap_instanced`, the two 1.3 MB effects, are not
  disassembled at all. They are the surface shaders proper and are where the
  scattering model actually lives.
- The `.11` builds were not looked at. On the model shaders those hold forward
  last-LOD variants only; whether that holds for water is unchecked.
- No claim is made about which pass writes which target, or the order the
  passes run in. That needs a frame capture, not a disassembly.
