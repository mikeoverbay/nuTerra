# Deferred lighting

`shaders/Final_render/deferred.frag` (~1130 lines). This is where every lit
pixel of terrain, models and trees is resolved. Order matters more than any
single term here, and most of the bugs in this area were ordering, not maths.

Shading maths verified against the source 2026-09-04. The game-side comparison
in section 5 is against [GAME_LIGHTING_MODEL.md](GAME_LIGHTING_MODEL.md), which
was transcribed from World of Tanks' own compiled `resolve_lighting`.

## Order

```
 1  N, L, Position                         geometry, all view space
 2  sun_shadow = sun_shadow_factor(Pos)    BAKED map-wide lookup, not the
                                           cascades - see shadows.md
 3  direct_light = N.L * sun_shadow        how much sun actually lands
 4  Ambient_level  = SH irradiance * AMBIENT
 5  Ambient_level *= (1 - direct_light)    ambient fills what the sun misses
 6  final_color    = Ambient_level         ambient is the base
 7  += lambertTerm * albedo * sun * sun_shadow
 8  += 1 - exp(-specular * sun_shadow)     saturating, see below
 9  *= BRIGHTNESS                          pre-exposure gain
10  += path_lights(), peak-rolled           the .campath lamps - AFTER
                                           BRIGHTNESS on purpose, see below
11  lut_color_correction()                 the map's own grading LUT
12  grey level, fog
13  outColor = correct(final_color, tonemap_exposure, 1.2)
```

Step 10 is after step 9 deliberately. BRIGHTNESS is a pre-exposure gain on the
SCENE - sun, ambient, sky. A lamp is its own source and does not get brighter
because the frame was turned up; before the multiply, raising Bright Level to
see the scene raised the lamps with it and their balance never changed.

It is also rolled off on its PEAK CHANNEL rather than per channel. `gColor` is
Rgba8, so a lamp bright enough to look at clips red first, then green, then
blue, and arrives white - the authored colour survived only in the fringe.
Scaling all three by one factor preserves the ratio, which is the colour. That
roll-off is also what gives each light's authored `level` something to do:
two levels that both saturate produce identical pixels.

Lamps are shadowed against a baked depth cube each - [shadows.md](shadows.md).

Steps 2 and 3 **must** precede step 5. Weighting ambient on facing alone leaves a
wall that faces the sun but stands in shadow with neither term - it gets no sun
because it is occluded, and no ambient because it is "facing the light". Black.

Shadow is a factor on the direct light, not a filter on the finished pixel. It
used to sit after tone mapping as `mix(outColor * 0.5, outColor, shadowDepth)`,
which dimmed the ambient too - the one thing that should still be there in shade.

Step 8 is worth noticing: the specular is passed through `1 - exp(-x)` **on its
own**, before the global tone curve, so a highlight saturates toward 1 per
channel no matter how large the lobe gets. Diffuse and ambient are not treated
this way. It is a local guard against blown highlights, not a tone mapper.

## 1. Read the channels before touching anything

This is the single most productive place to be careful, because two of the
names lie.

```glsl
GM_in = gGMF.xya          // NOT .rgb
```

| you read | you actually get |
|---|---|
| `GM_in.r` | **gloss** - and the local variable named `metal` is assigned from it |
| `GM_in.g` | **metal** - currently spent as `INTENSITY` in the legacy path |
| `GM_in.b` | `gGMF.a` |

`model.frag` writes `gGMF.rg = gm.rg` straight from the `metallicGlossMap`, and
the PBS_tank decode ([game_PBS_tank.md](game_PBS_tank.md)) proves that map is
**(gloss, metallic)**. So the shader's `metal` local is gloss, and real metal is
being used as a specular intensity.

The PBR block deliberately declares its own `g_gloss` / `g_metal` from the right
channels so the existing mistake cannot ride along into it. **Do not "tidy" that
duplication away** - it is the fix, not redundancy.

## 2. Diffuse

Plain Lambert, and that is all:

```glsl
float NdotL = max(dot(N, L), 0.0);
final_color.xyz += max(NdotL * albedo * sunColor, 0.0) * sun_shadow;
```

No `/PI`, no Burley, and - the consequential one - **no metal energy
conservation**. The game multiplies its diffuse by `1 - min(metal² * 3.2, 1)`,
so a metal gets almost no diffuse. Ours gives a metal full Lambert *and* a
specular lobe on top. Metals therefore read too bright and too flat here.

`GM_in.g` used to be the Lambert exponent. Trees write metal 0, so `pow(NdotL,
0)` was 1.0 at every angle facing the light and 0.0 the instant it was not - a
step function, and the hard terminator round surfaces like trunks were showing.

Back faces used to get sun, too: `max(dot(N, L), 0.001)` with a fractional
exponent gave a face pointing straight away from the light `pow(0.001, 0.2)` =
0.25, a quarter of full sun.

## 3. Specular - two models behind one switch

`uniform int pbr_spec`, driven by `PBR_SPEC` (Settings -> "PBR specular (game
model)", per-map persisted). **At 0 not one instruction of the new block runs**,
which is the bar the port was held to - checked by sha256 of the frame, not by
eye.

### pbr_spec = 0, the shipped path

```glsl
float spec = pow(dot(V, R), POWER) * SPECULAR * INTENSITY;   // Phong
vec4  brdf = texture(env_brdf_lut, vec2(1.0 - NdotL*0.25, 1.0 - metal));
specular   = vec3(spec) * brdf.x + brdf.y;
```

Phong against the mirror direction, then a split-sum BRDF LUT indexed on
**neither of its axes**. The LUT is `(alphaRoughness, NdotV)`; this feeds it a
function of `NdotL` and of `metal` - which, per section 1, is really gloss. The
numbers it returns are not wrong so much as unrelated to what was asked.

Half of this path is defensible, so be precise about which half:

```glsl
float metal     = GM_in.r;                     // :552  really GLOSS
      POWER     = max(GM_in.r * 30.0, 3.0);    // :558  gloss -> exponent, fine
      INTENSITY = GM_in.g;                     // :559  metal -> lobe scale, not fine
```

Driving the Phong exponent from gloss is reasonable. Scaling the lobe's
*intensity* linearly by metal is not - metal belongs in F0, and a dielectric
(metal 0) ends up with `INTENSITY = 0`, i.e. no specular at all. Terrain writes
`gGMF.r = 0.2`, so dry ground runs at `POWER = 6`.

### pbr_spec = 1, the game's model

A faithful port of GAME_LIGHTING_MODEL sections 3-4:

```glsl
alphaR  = 1 - gloss²
specTint = mix(1, albedo / (max(albedo) + eps), sat(metal² * 3.2))
a  = alphaR² + max(0.3 - 1.3*gloss, 0)        // low-gloss floor
D  = m⁴ / (PI * (NdotH²(m⁴-1) + 1)²)          // GGX
F  = specTint*metal + (1 - metal*specTint) * exp2((-5.55473*LdotH - 6.98316)*LdotH)
Vis = 0.25 / ((NdotV(1-k) + k) * (NdotL(1-k) + k)),  k = a²/2
specular = NdotL * D * Vis * F * SPECULAR
```

Metal is the **F0 magnitude** and `specTint` the hue, which is why a coloured
metal keeps its colour in its highlight while a dielectric does not. This part
is correct and matches the game term for term.

## 4. Environment specular is computed and thrown away

**In both paths.** `prefilteredColor` is assigned at deferred.frag:873 (PBR:
the game's full `specAmbient`) and at :879 (legacy: a cube tap blended with the
Phong lobe) — and **never read again**. Only `specular`, the analytic sun lobe,
reaches `sun_add` and the output.

So `specAmbient` still reaches nothing. For **terrain and general surfaces** that
is the whole story: no environment reflection, and what you see instead is
`ssr.frag` marching the frame, plus the separate water path.

### Map MODELS are the exception, since `48c8b096`

With `tank_mat` on - and on the PBR path, and only on models - a **second,
separate** environment term is built and added straight to `final_color`. It does
not go through `prefilteredColor`, which is why §4's claim survives around it.
The model path is the tank shader's own IBL: the mip walked by roughness over
four levels, the split-sum LUT on `(alphaRoughness, NdotV)`, then weighted

```
env_w = mix(NdotV * gloss, 1.0, metal)
```

as a DIELECTRIC for a wall - a rough one gets almost none, no grazing flare - but
at FULL weight for a METAL, because a rough metal with no diffuse left has
nothing else to show. Occluded by the model's baked AO and scaled by Ambient
Level and by `tank_env`; not shadowed by the sun, because it is the sky.

`gmm_curve` picks how the gloss/metal map is read: 0 raw bytes, 1 the Tank
Exporter's curves, 2 the game's (pow 2.2).

### The cube IS PMREM-encoded - this doc said otherwise and was wrong

The cube on disk is the game's `probes/global/pmrem.dds`: DXT5 with the HDR
exponent in alpha. Monastery's spans alpha 25..255 and decodes to a mean of
**2.8**, against **0.64** read as sRGB - so reading it as sRGB is a quarter of
the light, not a neutral simplification. `env_pmrem` selects:

```
env_pmrem = 1    c.rgb * c.rgb * exp2(9 * c.a) * 0.125     the game's decode
env_pmrem = 0    SRGBtoLINEAR(c)                            what the tank shader does
```

The older claim here - "ours is a plain 8-mip sRGB cube, so connecting env
specular means fixing the cube first" - was a wrong premise under a right
conclusion. The term had indeed been pulled from the composite after it threw
colours on wet terrain; the reason given was the encoding, and the encoding was
never the problem. **The same wrong premise is still in `deferred.frag` at the
`specAmbient` block**, where the comment says the cube "is not PMREM-encoded".
That block genuinely does read sRGB, so it describes its own behaviour
correctly - it is the justification that is false.

**So connecting §4 for general surfaces no longer means fixing the cube.** The
decode exists and is in use on models. What is left is deciding the weighting for
surfaces that are not models, and whether `specAmbient` or the model path's shape
is the one to keep.

## 5. Ours against the game, term by term

| term | the game | nuTerra |
|---|---|---|
| diffuse | Burley / PI | Lambert |
| metal energy conservation | `Fd *= 1 - min(metal²·3.2, 1)` | **absent** - metals get full diffuse |
| specular D | GGX | same, `pbr_spec = 1` only |
| specular F | Schlick-Gaussian, metal = F0 magnitude | same, `pbr_spec = 1` only |
| specular Vis | Smith-Schlick | same, `pbr_spec = 1` only |
| legacy specular | — | Phong + BRDF LUT on the wrong axes |
| BRDF LUT axes | `(alphaR, NdotV)` | correct in PBR path, wrong in legacy |
| environment specular | PMREM × split-sum LUT | **discarded** on general surfaces; a separate term on MODELS under `tank_mat` (§4) |
| PMREM decode | `rgb² · 2^(9a) / 8` | applied under `env_pmrem = 1`; the cube on disk is the game's `pmrem.dds` |
| gloss/metal curve | Tank Exporter's | `gmm_curve` 0 raw / 1 exporter / 2 game |
| reflection occlusion | `env *= min(local/global irradiance, 1)` | not implemented |
| ambient | always added, scaled by AO only | multiplied by `(1 - direct_light)` |
| ambient tint | `m_ambientTint`, ambient only | `AMBIENT_SAT` toward probe luminance |
| sun colour | `m_color * HDRParams.y`, no blend | blended toward white by `SUN_TINT` |
| baked AO | one channel, two curves | no AO for map content (see below) |
| emissive | `GB4.rgb * 5`, added late | not resolved here |

Three of these are ours by choice rather than by omission - `SUN_TINT`,
`AMBIENT` as a scalar, and the `(1 - direct_light)` ambient gate. The game gets
away without that gate because its ambient is genuinely directional; ours needed
it. **The real fix is directional ambient, not a harder gate.**

On AO: there are no AO maps for map content in the game either - it uses runtime
depth-only HBAO. See [wot-ao-architecture] in the session notes; nuTerra's own
SSAO attempt was rejected and removed.

## 6. Ambient comes from the map's SH probe

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

There is also an SH probe **grid** path (`eval_sh_grid`) with its own curve and
floor uniforms - see the probe-grid notes; the global probe above is the
fallback the game uses when you leave the grid.

## 7. Tone curve

`correct()` is `1 - exp(-x * exposure)`, which asymptotes to 1 and can never
exceed it. It used to be followed by `* 1.6`, so anything above about 0.7 input
clipped to flat white - and a lit surface reaches that on the sun term alone.
That is why the ambient and brightness sliders appeared to do nothing.

The gain now lives in `tonemap_exposure`, where it cannot clip. Contrast is a
separate thing, in the two `pow` exponents that multiply to about 0.916.

Note `pow(mapped, 1.0 / props.GAMMA_LEVEL * 0.5)` parses as `(1/GAMMA) * 0.5`,
not `1/(GAMMA*0.5)`. It lands somewhere sane at the 0.345 default, but it means
the Gamma slider runs backwards from what you would expect.

Everything here is Rgba8 - see [nuTerra has no HDR path] in the session notes.
`gColor` cannot hold a value above 1, so highlights clip before water and FX ever
see them. Widening `gColor` to Rgba16f and tone mapping once at the end is the
structural fix, and it is not done.

## 7a. The gun flashes are in the lamps' array

A tank firing puts a real point light at the muzzle, in the SAME array the map's
lamps and the Path Studio lights use - `pl_pos` / `pl_color_level` / `pl_dir` /
`pl_kind_blend`, uploaded by `upload_path_lights` in `modRender.vb`. There is one
light loop in `deferred.frag` and a second one would be a second set of falloff,
gain and shadow packing, drifting apart from the first.

A flash is kind 0 (lit everywhere), no cone, and layer -1 so the kind packs as
`0 + 8 * 0` and it never reaches for a shadow cube.

**They take their slots FROM the lamps, and are counted first.** There are
`MAX_PATH_LIGHTS` = 32 slots. `gather_gun_lights` runs before `visible_lights`
and the lamps are then asked for `32 - guns`. Appending the flashes afterwards
instead would work on every map with room to spare and silently light nothing on
a map with 32 lamps in reach - which is indistinguishable from the feature being
broken.

**Colour and level come apart here.** `gun_effects.xml` animates the two together
- `255 150 0` at x20 into `255 200 100` at x25 - and `BlastSpec.Sample` folds
them into one vector because that is what a sprite wants. A point light wants
them separate: the deferred pass takes a unit colour and a level, and a colour
already twenty times over range clips to white before it has travelled a metre.
`SampleColour` and `SampleMult` give the two halves. The multiplier is divided by
25, the brightest key in the table, so `TANK_GUN_LIGHT_LEVEL` reads as "a flash
at full is this bright".

The light lasts what the game says it lasts - the timeline key the effect's
`endKey` names, 0.09 s on every tank gun - which is a different and much shorter
clock than the flame sprite's `timeline.end` of 0.5 s. See `TankBlast.vb`.

Controls are under **TANKS!**, not Lighting Settings: `Gun lights` and
`Gun light level`.

## 8. Traps found here

**`prefilteredColor` is dead in both paths.** See §4. It looks like a working
environment term in a diff; it is not connected.

**`GM_in` is `gGMF.xya`, not `.rgb`.** See §1. The names lie in two places.

**Array uniforms are reported as `name[0]`.** `GL.GetActiveUniformName` returns
`sh_ambient[0]`, so asking the shader cache for `sh_ambient` missed and got -1
back. That is indistinguishable from an optimised-out uniform, and
`glUniform3fv(-1, ...)` is a defined no-op - so the SH array silently stayed at
zero. `ShaderLoader` now registers the bare name against the same location.

**Shaders validate at runtime only.** A clean build says nothing about GLSL.
Launch and check stdout for `Shaders Built.` and `didn't compile`. Shader source
is `nuTerra/shaders/`; `bin/.../shaders` is build output, and editing the bin
copy takes effect immediately - which makes a half-applied change easy to miss.

## 9. Sliders

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
| PBR specular | on/off | swaps §3's two models; off is byte-identical to shipped |

Under **TANKS!** rather than Lighting Settings, because they belong to the
vehicles rather than to the map:

| control | range | what it does |
|---|---|---|
| Gun lights | on/off | a point light at each muzzle for the flash, §7a |
| Gun light level | 0 - 30 | brightness of a flash at its peak key |

`SUN_STRENGTH`, `SUN_TINT` and `AMBIENT_SAT` reuse pad floats appended to the end
of the `CommonProperties` UBO. All three are now spoken for - anything further
needs the block extended by a `vec4`, in both `modOpenGL.vb` and `common.h`, and
in the same order.

## 10. If you are picking this up

Ranked by how much they would change the image, largest first:

1. **Connect §4 for general surfaces.** Terrain and non-model geometry still
   have no environment reflection; models got one under `tank_mat` in
   `48c8b096`. The decode is no longer the blocker - `env_pmrem` applies the
   game's, and the cube on disk is the game's `pmrem.dds`. What is left is the
   weighting for surfaces that are not models, and whether to keep
   `specAmbient` or the model path's shape.
2. **Widen `gColor` to Rgba16f.** Everything downstream clips at 1 today.
3. **Metal energy conservation in the diffuse** - one multiply, and metals stop
   reading as bright plastic.
4. **Fix the legacy BRDF LUT axes**, or retire the legacy path once `pbr_spec`
   has been shipped on by default.
5. **Directional ambient**, which is what would let the `(1 - direct_light)`
   gate go away.

Rules this file assumes: **measure, then claim**; run the null control (same
build twice, same camera - if that is not 0 pixels the comparison proves
nothing); and never A/B lighting by eye through a screenshot.

## See also

- [GAME_LIGHTING_MODEL.md](GAME_LIGHTING_MODEL.md) - the game's resolve, decoded
- [game_PBS_tank.md](game_PBS_tank.md) - the G-buffer packing the channels come from
- [terrain_blend.md](terrain_blend.md) - the terrain layer blend
- [FX_PIPELINE.md](FX_PIPELINE.md) - what happens to the frame after this shader
- [game_deferred_decal.md](game_deferred_decal.md) - reading the game's compiled shaders
