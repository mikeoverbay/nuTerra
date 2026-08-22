# WoT `deferred_decal.fx` — recovered

Source: `res/packages/shaders.pkg` → `shaders/decals/deferred_decal.11.dx11.fxo`

## Container

The `.fxo` is a **ZIP**. Members:

| member       | contents |
|--------------|----------|
| `effect`     | `ARIEDX11` blob — the real payload |
| `depends N`  | packed-section records: source `.fx` / `.fxh` name + MD5 |
| `hash`       | 32-byte digest |
| `version`    | `"wgFX compiler 1.0.3"` |

`depends 1` names the original source: **`shaders/decals/deferred_decal.fx`**.

The `ARIEDX11` blob is chunked: `STRT` (string/param table), `TECH`, `DATA`,
`CODE`, `RSTD` (render states), `SSTD` (sampler states), then N × `ARISDX11`
sub-blobs each wrapping a raw **DXBC**. `deferred_decal.11` holds 8 shaders
(3 VS + 5 PS). Disassemble with `fxc /dumpbin`.

Techniques present: `Main`, `PBSDisplacement`, `PBSParallax`, **`PBSRoad`**, **`RoadMesh`**.

## Instance data

`cbuffer DecalInstancingBuffer { float4 g_decalInstancingStream[125]; }` —
**8 float4 per decal** (`ishl id, 3`), so 15 decals per draw:

| slot | contents |
|------|----------|
| `[0].xyz` | compressed quaternion; `w = sign([0].w) * sqrt(1 - dot(xyz,xyz))` |
| `[0].w`   | **sign** = quaternion handedness, **integer magnitude** = decal *type id* |
| `[1].xyz` | translation.  `[1].w` → alpha term |
| `[2].xyz` | scale (shader divides by it).  `[2].w` → alpha term |
| `[3]`     | **atlas rect**: `.xy` = tile offset, `.zw` = tile size |
| `[4]`     | **packed**: `floor(v)/255` = 4 corner alphas, `frac(v)*2.004` = fade distances |
| `[5..7]`  | (unused by this permutation) |

Two quantities share one float in `[0].w` and again in `[4]` — integer part is one
field, fractional part is another. Same idiom as the road_map fade values.

## Vertex shader

Rebuilds the 3×3 rotation from the compressed quat, applies scale + translation,
emits the inverse decal basis (`o1..o4`), the atlas rect (`o5`), the fade
distances (`o6`), and

```
o3.w = saturate(bilerp(corner alphas over quad uv)) * 0.999 + floor(|q.w|)
//     \_______________ per-decal opacity ________/          \_ type id _/
```

## Pixel shader (technique `Main`)

```hlsl
// t0 g_atlasMap1   t1 g_bitwiseLUTMap   t2 g_hierarchicalZMap   t3 g_texObjKind

float2 uv  = SV_Position.xy * g_screen.zw;
float  z   = 1.0 / abs(g_hierarchicalZMap.SampleLevel(pointClamp, uv, g_depthMipSkip).x);
float3 P   = reconstructWorld(uv, z);            // via g_worldReconstructionMat
float3 d   = mul(P, decalBasis) + decalOrigin;   // decal space

// analytic derivatives: the same reconstruction is run for the +1,+0 and +0,+1
// neighbours so the atlas fetch gets true anisotropic gradients
float2 duvdx, duvdy;
float4 albedo = g_atlasMap1.SampleGrad(s1, frac(d.xy) * atlas.zw + atlas.xy, duvdx, duvdy);

uint kind = round(g_texObjKind.SampleLevel(pointClamp, uv, 0).a * 255);
kind -= (kind >= 128) ? 128 : 0;                 // strip flag bits
kind -= (kind >=  64) ?  64 : 0;
kind -= (kind >=  32) ?  32 : 0;

float a = saturate(d.z * 2.0);                   // 1. soft fade along projection axis
a *= (uv stayed inside its atlas tile) ? 1 : 0;  // 2. tile clamp reject
a *= distanceFade(fadeParams, d);                // 3. quadratic near/far fade
a *= frac(typeField) * 1.001;                    // 4. per-decal opacity
a *= albedo.a;                                   // 5. texture alpha
a *= g_bitwiseLUTMap.Load(floor(typeField)/255,  // 6. is this decal allowed on
                          kind/7).x;             //    this surface kind?

SV_Target0 = float4(albedo.rgb, a);              // albedo, blended by a
SV_Target1 = 0;
```

The low-quality permutation collapses all of that to
`SV_Target0 = float4(albedo.rgb, fade * albedo.a)`.

## Points that matter

**The pixel shader applies no colour transform at all.** No tint, no gamma, no
additive offset — `SV_Target0.rgb` is the atlas texel verbatim. Every bit of the
decal's appearance is carried in the **alpha**, which is the product of six
independent terms.

**Surface masking is a 2-D lookup table**, `g_bitwiseLUTMap[decalType][surfaceKind]`,
not a hardcoded comparison. `g_texObjKind.a * 255` is the kind byte; bits 128/64/32
are flags and get stripped before the lookup.

**Decal UVs `frac()` before the atlas remap**, so a decal can tile within its own
atlas tile. The clamp test afterwards kills any texel whose gradient walked it out
of the tile.
