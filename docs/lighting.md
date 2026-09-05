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
 2  sun_shadow = sun_shadow_factor(Pos)    cascaded shadow lookup
 3  direct_light = N.L * sun_shadow        how much sun actually lands
 4  Ambient_level  = SH irradiance * AMBIENT
 5  Ambient_level *= (1 - direct_light)    ambient fills what the sun misses
 6  final_color    = Ambient_level         ambient is the base
 7  += lambertTerm * albedo * sun * sun_shadow
 8  += 1 - exp(-specular * sun_shadow)     saturating, see below
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

So general surfaces get **no environment reflection at all** today. What you see
instead is `ssr.frag`, which marches the frame, plus the separate water path.

This is deliberate in its current state, and the reason is worth knowing before
"fixing" it: **nuTerra's cubemap is not PMREM-encoded.** The game decodes

```
env = c.rgb * c.rgb * exp2(9 * c.a) * 0.125
```

from a DXT5 HDR cube. Ours is a plain 8-mip sRGB cube, so the port keeps the
game's *mip curve* but leaves the *decode* alone - and feeding an undecoded cube
into the reflection threw colours off badly enough on wet terrain that the whole
environment term was pulled out of the composite rather than shipped wrong.

**Connecting env specular therefore means fixing the cube first, not the
shader.** That is the largest single gap between this renderer and the game's.

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
| environment specular | PMREM × split-sum LUT | **computed, discarded** (§4) |
| PMREM decode | `rgb² · 2^(9a) / 8` | not applied; plain sRGB cube |
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

`SUN_STRENGTH`, `SUN_TINT` and `AMBIENT_SAT` reuse pad floats appended to the end
of the `CommonProperties` UBO. All three are now spoken for - anything further
needs the block extended by a `vec4`, in both `modOpenGL.vb` and `common.h`, and
in the same order.

## 10. If you are picking this up

Ranked by how much they would change the image, largest first:

1. **PMREM-decode the cubemap and connect §4.** General surfaces have no
   environment reflection at all. This is the biggest visible gap and the
   shader side of it is already written.
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
