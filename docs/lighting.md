# Deferred lighting

`shaders/Final_render/deferred.frag`. Order matters more than any single term
here, and most of the bugs in this area were ordering, not maths.

## Order

```
 1  N, L, Position                         geometry, all view space
 2  sun_shadow = sun_shadow_factor(Pos)    cascaded shadow lookup
 3  direct_light = N.L * sun_shadow        how much sun actually lands
 4  Ambient_level  = SH irradiance * AMBIENT
 5  Ambient_level *= (1 - direct_light)    ambient fills what the sun misses
 6  final_color    = Ambient_level         ambient is the base
 7  += lambertTerm * albedo * sun * sun_shadow
 8  += specular * sun_shadow, reflections, water
 9  *= BRIGHTNESS                          pre-exposure gain
10  lut_color_correction()                 the map's own grading LUT
11  grey level, fog
12  outColor = correct(final_color, tonemap_exposure, 1.2)
```

Steps 2 and 3 **must** precede step 5. Weighting ambient on facing alone leaves a
wall that faces the sun but stands in shadow with neither term - it gets no sun
because it is occluded, and no ambient because it is "facing the light". Black.

Shadow is a factor on the direct light, not a filter on the finished pixel. It
used to sit after tone mapping as `mix(outColor * 0.5, outColor, shadowDepth)`,
which dimmed the ambient too - the one thing that should still be there in shade.

## Ambient comes from the map's SH probe

Every space ships `environments/<env>/probes/global/rem_sh.xml`: a packed section
holding `sh0`..`sh8` as RGB triples plus `dominant_vector` and `max_lum`. Nine L2
spherical harmonic coefficients baked from that map's sky.

All 64 installed spaces have one, and a `pmrem.dds` beside it, so the no-probe
fallback never fires against shipped content.

`ResMgr.openXML` already reads packed sections, so loading is just
`vector3_from_string` on each key. Read in `TerrainBuilder.load_sh_ambient`.

Two things to get right:

- **Evaluate against a world-space normal.** The probe is baked in world space
  but `N` is view space, because `normalMatrix` is built from `modelView`. Using
  one against the other rotates the whole ambient environment with the camera -
  the sky's blue lands on whatever faces up *on screen*.
- **The blue is real.** Abbey's `sh1` is `[-0.135, 0.431, 0.989]`. Sky fill is
  genuinely what lights a shadow. `AMBIENT_SAT` desaturates toward the probe's
  own luminance when it reads too strong, keeping level and direction.

## Tone curve

`correct()` is `1 - exp(-x * exposure)`, which asymptotes to 1 and can never
exceed it. It used to be followed by `* 1.6`, so anything above about 0.7 input
clipped to flat white - and a lit surface reaches that on the sun term alone.
That is why the ambient and brightness sliders appeared to do nothing.

The gain now lives in `tonemap_exposure`, where it cannot clip. Contrast is a
separate thing, in the two `pow` exponents that multiply to about 0.916.

Note `pow(mapped, 1.0 / props.GAMMA_LEVEL * 0.5)` parses as `(1/GAMMA) * 0.5`,
not `1/(GAMMA*0.5)`. It lands somewhere sane at the 0.345 default, but it means
the Gamma slider runs backwards from what you would expect.

## Traps found here

**`GM_in` is `gGMF.xya`, not `.rgb`.** So `GM_in.g` is `gGMF.y` - metal for
models, the specular sample for terrain. It was being used as the Lambert
exponent, and trees write metal 0, so `pow(NdotL, 0)` was 1.0 at every angle
facing the light and 0.0 the instant it was not. A step function, and the hard
terminator that round surfaces like trunks were showing. The diffuse is plain
`N.L` now.

**Back faces used to get sun.** `max(dot(N, L), 0.001)` with a fractional
exponent gave a face pointing straight away from the light `pow(0.001, 0.2)` =
0.25 - a quarter of full sun.

**Array uniforms are reported as `name[0]`.** `GL.GetActiveUniformName` returns
`sh_ambient[0]`, so asking the shader cache for `sh_ambient` missed and got -1
back. That is indistinguishable from an optimised-out uniform, and
`glUniform3fv(-1, ...)` is a defined no-op - so the SH array silently stayed at
zero. `ShaderLoader` now registers the bare name against the same location.

**`prefilteredColor` is dead code.** It samples the PMREM, gets blended, and is
never added to `final_color` - only `water_reflect`, `specular` and
`G_prefilteredColor` are. General surfaces get no environment reflection.

## Sliders

Settings -> Lighting Settings. All persist and are clamped to their range on
load. Ranges are deliberately matched between the slider and the load clamp, so
changing one means changing the other.

| slider | range | what it does |
|---|---|---|
| Ambient Level | 0 - 0.4 | scale on the SH irradiance |
| Ambient Sat | 0 - 1 | 0 flattens the probe to its luminance, 1 keeps its colour |
| Sun Tint | 0 - 1 | 0 white sun, 1 the map's `sunLightColor` at full chroma |
| Sun Strength | 0 - 3 | level of the direct light |
| Tone Exposure | 0.5 - 4 | gain of the tone curve; cannot clip |
| Bright Level | 0 - 2 | pre-exposure gain on the whole composite |

`SUN_STRENGTH`, `SUN_TINT` and `AMBIENT_SAT` reuse pad floats appended to the end
of the `CommonProperties` UBO. All three are now spoken for - anything further
needs the block extended by a `vec4`, in both `modOpenGL.vb` and `common.h`, and
in the same order.

## See also

- [terrain_blend.md](terrain_blend.md) - the terrain layer blend
- [game_deferred_decal.md](game_deferred_decal.md) - reading the game's compiled shaders
