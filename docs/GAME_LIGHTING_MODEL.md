# How the game lights terrain and buildings

Transcribed from World of Tanks' own compiled shaders, 2026-08-28.

Source: `shaders/std_effects/resolve_lighting.10.dx11.fxo` in `shaders.pkg`.
The `.11` twin externalises its pixel shaders (two ~450 byte stubs); the `.10`
twin ships all **385 permutations inline**. Cracking recipe: the fxo is a ZIP,
read the `effect` entry, scan for `DXBC` magic with the u32 blob size at +24,
then `fxc /dumpbin` (Win10 SDK 10.0.19041.0 x64).

Two variants were read end to end:

| blob | size | what it is |
|------|------|-----------|
| `rl10_315` | 321 instr | minimal permutation - no shadow map, no SSR, no ice, global SH only. The clean core. |
| `rl10_1` | 1250 instr | full permutation - SH probe grid, SSR, screen-space shadows, ice refraction. |

Everything below is verified in `rl10_315` unless marked otherwise.

This is a **deferred** renderer, so terrain and buildings are lit by the same
shader. Material differences arrive through the G-buffer, not through separate
lighting code.

---

## 1. G-buffer layout

| channel | contents |
|---------|----------|
| **GB0** | `.x` metal (gamma-encoded) &nbsp; `.y` gloss (linear) &nbsp; `.z` per-material mask (transmission / wetness) &nbsp; `.w` packed byte |
| **GB1** | `.xyz` normal, `*2-1` &nbsp; `.w` baked occlusion |
| **GB2** | `.xyz` albedo (gamma-encoded) &nbsp; `.w` material parameter (subtype, ice amount, LUT index) |
| **GB4** | `.xyz` emissive, stored `/5` |
| depth | hierarchical-Z, world position via the inverse view-projection |

`GB0.w * 255` is a packed byte:

```
bits 7,6,5 = flags
bits 4,3   = sub-type   (0..3)
bits 2,1,0 = material id (0..7)
```

Material ids seen branching in the resolve: `0` standard (terrain, most
buildings), `1`,`2`,`3` special, `4`,`5`,`6` the vegetation family (`6` carries
leaf transmission), `7` forces `metal = 0.231373` - the byte that encodes the
0.04 dielectric F0. Ids 1..7 also index a properties LUT
(`g_speedTreePropertiesSampler`) at `(GB2.w, 0.5)`.

Decode:

```
albedo   = pow(GB2.rgb, g_gammaCorrection.x)
emissive = pow(GB4.rgb * 5, g_gammaCorrection.x)
N        = normalize(GB1.xyz * 2 - 1)
gloss    = GB0.y
metal    = pow(GB0.x, g_gammaCorrection.x)
occl     = GB1.w * g_SSAOParams[0].y
```

---

## 2. Ambient - a directional SH probe field

There is **no ambient level constant**. `g_sunLight.m_ambient` exists in the
cbuffer and is never read by any variant. The level *is* the probe data.

Ambient is L2 spherical harmonics packed into 7 float4s, evaluated against the
surface normal, from two sources blended together:

* **local** - a 3D probe grid texture (`g_SH3DSml`). The third axis indexes the
  7 coefficient vectors as slices at `(2k+1)/16`, so one probe costs 7 taps.
  Present only in the richer permutations.
* **global** - the `g_sh9[7]` constants. The only source in `rl10_315`.

```
gridUV      = worldPos.xz * g_shGridSize.zw - g_shGridSize.xy
outOfBounds = any(gridUV < 0) || any(gridUV > 1)
heightFade  = saturate((worldPos.y - probe[6].w) * g_shGridFade)
blend       = (outOfBounds ? 1 : 0) * (1 - heightFade) + heightFade

for i in 0..6:
    sh[i] = lerp(sampleProbeGrid(gridUV, i), g_sh9[i], blend)
```

So you fall back to the global SH when you leave the grid in XZ **or** when you
climb above the probe's stored reference height (`probe[6].w` is a height, not a
coefficient). Reflections and ambient both stay sane above rooftops.

Evaluation - the classic 7-vector packing, one dot product per band:

```
irradiance(n):
    C = (sh[0].x, sh[1].x, sh[2].x)                       # constant band
    L = (dot(sh[0].wyz, n), dot(sh[1].wyz, n),
         dot(sh[2].wyz, n))                               # linear band
    q = (n.y*n.x, n.z*n.y, n.z*n.z, n.x*n.z)
    Q = (dot(sh[3], q), dot(sh[4], q), dot(sh[5], q))
    Q += sh[6].xyz * (n.x*n.x - n.y*n.y)                   # quadratic band
    return max(C + L + Q, 1e-4)

E_front = irradiance(N)
E_back  = C - L + Q          # same terms, linear band negated - free back side
```

`E_back` is the free by-product used for leaf transmission.

**The ambient term, and where the tint lives:**

```
aoVis   = 1 - min(pow(occl, g_bakedAOPower), 1)
ambient = aoVis * E_front * g_sunLight.m_ambientTint
diffuseAmbient = albedo * ambient
```

`m_ambientTint` multiplies **only** the ambient. It never touches the sun.

---

## 3. Environment specular

```
R      = reflect(-V, N)
Rdom   = lerp(N, R, saturate(gloss * 1.35))        # dominant direction
alphaR = 1 - gloss*gloss
mip    = (alphaR*alphaR * g_PMREMMipsNumber.x + g_PMREMMipsNumber.y)
       * (min(2*NdotV, 1) * 0.5 + 0.5)             # grazing angles blur

c   = texCubeLod(PMREM, Rdom, mip)
env = c.rgb * c.rgb * exp2(9 * c.a) * 0.125        # HDR decode
brdf = tex2D(EnvBRDFLut, (alphaR, NdotV))          # scale / bias
specAmbient = env * (specTint * brdf.x + brdf.y)
```

In grid variants the cubemap is global, so it gets darkened by how much darker
the local probe is than the global one:

```
env *= min(localIrradiance / globalIrradiance, 1)
```

That is how a reflection ends up shadowed under a bridge without ray marching.

---

## 4. Sun

```
sunColor = g_sunLight.m_color * g_HDRParams.y
L        = -g_sunLight.m_dir
```

**There is no sun-tint blend.** No `lerp(white, sunColor, tint)` anywhere. The
sun's colour is `m_color`, full stop. `m_ambientTint` is a *separate* constant
for the ambient.

The occlusion gate:

```
shadow = min(max(albedo.r, albedo.g, albedo.b),
             1 - saturate(occl * g_bakedAOToShadowsMult))
         # richer variants also multiply the screen-space shadow map here
```

Note the same occlusion channel feeds ambient and sun through **two different
curves**: `bakedAOPower` shapes it for ambient, `bakedAOToShadowsMult` for the
sun.

```
if (shadow > 0):                                   # fully occluded skips the block
    H     = normalize(V + L)
    NdotL = dot(N, L);  NdotH = sat(dot(N, H))
    LdotH = dot(L, H);  NdotV = abs(dot(N, V))

    a = (1 - gloss*gloss)^2 + max(0.3 - 1.3*gloss, 0)   # low-gloss floor
    m = max(a, 0.015979)

    # GGX / Trowbridge-Reitz
    D = m^4 / (PI * (NdotH*NdotH * (m^4 - 1) + 1)^2)

    # Schlick-Gaussian Fresnel - metal is the F0 magnitude, specTint the hue
    Fexp = exp2((-5.55473 * LdotH - 6.98316) * LdotH)
    F    = specTint * metal + (1 - metal * specTint) * Fexp

    # Smith-Schlick visibility, or a cheap one at low quality
    k   = a * a * 0.5
    Vis = 0.25 / ((NdotV * (1-k) + k) * (NdotL * (1-k) + k))
    Vis_low = 0.25 / max(NdotV, NdotL)

    # Burley diffuse, or Lambert at low quality
    Fd = burley(a, LdotH, NdotV, NdotL) / PI
    Fd_low = 1 / PI
    Fd *= 1 - min(metal*metal * 3.2, 1)            # energy conservation

    sun  = albedo * NdotL * Fd  +  NdotL * D * Vis * F
    sun += transmission                            # vegetation, quality < 2
    sun *= sunColor

    color += sun * shadow                          # one scalar gates everything
```

`specTint` is a hue-preserving normalisation of the albedo,
`lerp(1, albedo / (max(albedo) + eps), saturate(metal^2 * 3.2))`.

---

## 5. Composite and fog

```
color += emissive * g_envLumMultipliers.w

# height + distance fog, with the sun scattering into it
scatter = pow(saturate(dot(viewDir, -g_sunLight.m_dir)),
              g_fogParams.scatterColorSunExp.w)
        * g_fogParams.scatterColorSunExp.rgb

out.rgb = color * fogTransmittance + fogColor
```

---

## 6. What this means for nuTerra

1. **`sun_tint` is ours, not theirs.** The game never blends the sun toward
   white. If we want to match it, the sun is simply `m_color * HDRParams.y` and
   the tint slider belongs on the *ambient*, matching `m_ambientTint`.

2. **`AMBIENT` as a level is ours too.** `m_ambient` is dead in every variant -
   the level comes from the probe bake. Our scalar is a stand-in for probe data
   we do not parse yet.

3. **The game does NOT make sun and ambient mutually exclusive.** Ambient is
   always added, scaled only by AO; the sun is added on top, scaled by shadow.
   It gets away with this because its ambient is *directional*: a face turned
   away from the sun already receives less SH irradiance, so form survives
   without a hard split. Our flat-ish ambient is why we needed the hard rule -
   the real fix is directional ambient, not a harder gate.

4. **One occlusion channel, two curves.** Worth copying: `bakedAOPower` for
   ambient, `bakedAOToShadowsMult` for the sun.

5. **Reflection occlusion is nearly free** - scale the cubemap by
   `local / global` irradiance rather than tracing anything.

6. Parked leads: the SH probe grid texture (a 3D texture we do not load), the
   per-material properties LUT, and `terrainBakedShadowFactor` in
   `g_simplifiedDeferredParams`, which no read variant consumes.
