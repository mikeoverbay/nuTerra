#version 450 core
#extension GL_ARB_shading_language_include : require

// cameraPos comes from the PerView UBO and the exporter's shading is written
// in WORLD space, so this stage needs the block the vertex stage already asks
// for. Without the define common.h leaves the whole block out and cameraPos is
// simply undeclared.
#define USE_PERVIEW_UBO
#include "common.h" //! #include "../common.h"

// Tank shading - the Tank Exporter's mesh.frag, brought over whole.
//
// Milestone 2. The stub this replaces wrote albedo/gloss/metal into the
// G-buffer and let the deferred resolve light the tank like a building; its own
// comment said what was meant to happen next, and this is it: the tank shades
// ITSELF, exactly as the exporter does, and writes GFLAG_UNLIT so the resolve
// passes the pixels through untouched (deferred.frag main(), the
// `(FLAG & GBUF_RENDER_MASK) != 0u` gate - zero render bits means no shading).
//
// WHAT THAT COSTS, stated here rather than discovered later: an UNLIT pixel
// gets no fog, no tone mapping and no lamp glow from the resolve. The tank is
// lit by its own rig and by nothing else in the map. The G-buffer is still
// written in full - gPosition, gNormal, gSurfaceNormals - so everything that
// reads DEPTH rather than lighting still sees the tank: SSR, the particles'
// soft edges, the volumetrics and lamp_fog all keep working.
//
// EVERYTHING BELOW IS THE EXPORTER'S, including the comments explaining why
// each constant is what it is. They are the record of what was measured against
// the game, and rewriting them in my own words would throw that away. Where
// nuTerra's names differ the mapping is noted at the uniform. Three things
// could NOT come across and are marked NOT PORTED where they would have gone.
//
// Lighting formulas adapted directly from tank_fragment.glsl (WoT Tank
// Exporter). IBL uses the split-sum path:
//   diffuse  IBL = irradiance_map  x diffuseColor
//   specular IBL = prefiltered_map x (specColor x brdf.x + brdf.y)
//
// GMM texture channels (WoT convention):
//   R = Gloss   ->  glossiness = pow(R / 0.8, 7.0)   HIGH = shiny, LOW = rubber
//   G = Metal   ->  metallic   = pow(G / 0.5, 5.0)
//   B = Camo mask (reserved for a camo pass - not used here)
//
// Specular on rubber is suppressed by two independent factors from
// tank_fragment.glsl:
//   1.  D *= GMM.r          (NDF weighted by raw gloss channel)
//   2.  specContrib *= perceptualRoughness * 6.0  (gloss-gated scale)
// Both -> near zero when the surface is rough/dark in the gloss map.

#define M_PI 3.141592653589793

in VS_OUT
{
    vec2 TC1;
    vec2 TC2;
    vec3 viewPosition;
    mat3 TBN;
    flat vec3 surfaceNormal;
    vec3 worldPosition;
    mat3 worldTBN;
    vec3 worldNormal;
} fs_in;

layout(location = 0) out vec4 gColor;
layout(location = 1) out vec3 gNormal;
layout(location = 2) out vec4 gGMF;
layout(location = 3) out vec3 gPosition;
layout(location = 4) out vec3 gSurfaceNormals;

// ---- Texture samplers -------------------------------------------------------
// Units 0-3 are the exporter's four maps in NUTERRA's order, which is not the
// exporter's: TankRenderer.vb already binds GMM to 2 and AO to 3, and changing
// that would be churn in VB for no gain. 4 and up are new.
layout(binding = 0) uniform sampler2D   diffuseMap;      // *_AM
layout(binding = 1) uniform sampler2D   normalMap;       // *_ANM, normal in .ag
layout(binding = 2) uniform sampler2D   gmmMap;          // *_GMM r gloss, g metal, b camo
layout(binding = 3) uniform sampler2D   aoMap;           // *_AO
layout(binding = 4) uniform samplerCube irradianceMap;   // Lambertian irradiance
layout(binding = 5) uniform sampler2D   brdfLut;         // GGX split-sum LUT
layout(binding = 6) uniform samplerCube prefilteredMap;  // RAW env cubemap, auto-mipped
layout(binding = 7) uniform sampler2D   detailMap;       // metallicDetailMap scratch noise
layout(binding = 8) uniform sampler2D   crashTileMap;    // shared damage tile

uniform ivec4 has_maps;      // AM, ANM, GMM, AO present   (nuTerra's own name)
uniform int   alpha_test;    // exporter: alpha_test_enable
uniform float alpha_ref;
uniform int   normal_dxt1;   // g_useNormalPackDXT1. The exporter spells this the
                             // other way round as is_GA_normal, so the GA decode
                             // is `normal_dxt1 == 0` throughout.

// ---- Everything TankRenderer does not upload yet ---------------------------
// tank_shading is the sentinel. It reads 0 until the VB side is rebuilt - an
// unset uniform is zero - so a shader dropped in ahead of its build still draws
// a correctly lit tank off the four maps that ARE bound, instead of a black
// one. Every fallback below is the exporter's own value.
uniform int   tank_shading;

uniform int   has_detail_map;
uniform vec2  detail_tiling;        // g_detailUVTiling.xy (typical 7,7)

uniform int   has_crash_tile;       // 1 = mesh is /crash/ + PBS_tank_crash
uniform vec4  crash_uv_tiling;      // g_crashUVTiling: xy=mul, zw=offset
uniform float crash_coefficient;    // 0..1, "how damaged"; default 1.0
uniform int   crash_channel_offset; // 0/1/2, rotates which channel each region picks

#define NUM_LIGHTS 3
uniform vec3  light_pos[NUM_LIGHTS];

uniform vec3  u_mflash_pos;
uniform vec3  u_mflash_color;       // 0..N (pre-scaled by tint x intensity)
uniform float u_mflash_intensity;   // 0 = off
uniform float u_mflash_inner;
uniform float u_mflash_outer;

uniform int   alpha_in_normal_red;
uniform int   ao_in_diffuse_alpha;
uniform vec3  armor_color;
uniform int   has_armor_color;
uniform int   has_irradiance;
uniform int   has_brdf_lut;
uniform int   has_prefiltered;

uniform float metal_scale;          // Sun brightness - scales all direct light
uniform float shine_scale;          // A_level : flat ambient fill
uniform int   apply_normal_map;     // exporter: invert_metal  (checkbox: NMap)
uniform int   apply_ao;             // exporter: invert_shine  (checkbox: AO)

// NOT PORTED - wireframe_mode. The exporter paints every fragment flat grey so
// its line pass reads over the top; nuTerra has no tank wireframe pass for it
// to read against.
//
// NOT PORTED - v_wheel_state, the red/green/blue/yellow wheel tints. That is
// the exporter's suspension debug, driven by TankPhysics, which does not exist
// here - there is no per-vertex wheel state for the vertex stage to carry.
//
// NOT PORTED - the exporter's orbit toggle for the light rig. The positions
// come across; what moves them is the app's business, not the shader's.

// ---- Constants --------------------------------------------------------------
const float MAX_REFLECTION_LOD = 4.0;
const float c_MinRoughness     = 0.02;

// =============================================================================
// PBRInfo  (from tank_fragment.glsl -- unchanged struct layout)
//
// IMPORTANT: perceptualRoughness stores GLOSSINESS (WoT convention).
//   High value = shiny/smooth.  Low value = rough/matte/rubber.
//   Actual material roughness = (1 - perceptualRoughness).
// =============================================================================
struct PBRInfo {
    float NdotL;
    float NdotV;
    float NdotH;
    float LdotH;
    float VdotH;
    float perceptualRoughness;  // WoT: GLOSSINESS  (high = shiny)
    float metalness;
    vec3  reflectance0;         // F0 at normal incidence
    vec3  reflectance90;        // F at grazing
    float alphaRoughness;       // (1 - gloss)^2 -- actual material roughness squared
    vec3  diffuseColor;
    vec3  specularColor;
};

// =============================================================================
// sRGB -> linear  (IEC 61966-2-1 -- from tank_fragment.glsl SRGBtoLINEAR)
// Named apart from deferred.frag's SRGBtoLINEAR: that one lives in the resolve,
// this stage does not include it, and two identical names in one codebase that
// are not the same function is how the wrong one gets edited.
// =============================================================================
vec4 tank_SRGBtoLINEAR(vec4 srgbIn)
{
    vec3 bLess  = step(vec3(0.04045), srgbIn.xyz);
    vec3 linOut = mix(srgbIn.xyz / vec3(12.92),
                      pow((srgbIn.xyz + vec3(0.055)) / vec3(1.055), vec3(2.4)),
                      bLess);
    return vec4(linOut, srgbIn.w);
}

// =============================================================================
// Normal-map decode  (WoT GA-channel compressed format)
//
// Returned in TANGENT space; the caller picks which TBN takes it out. The
// `n.x *= -1.0` is the exporter's and was NOT in the stub this replaces - that
// one read .ag and rebuilt z but never flipped x, so every GA normal map was
// mirrored along the tangent.
// =============================================================================
vec3 unpackNormal(vec2 uv)
{
    vec4 tn = texture(normalMap, uv);
    vec3 n;
    if (normal_dxt1 == 0) {         // exporter: is_GA_normal == 1
        n.xy = tn.ga * 2.0 - 1.0;
        n.z  = sqrt(clamp(1.0 - dot(n.xy, n.xy), 0.0, 1.0));
        n.x *= -1.0;
    } else {
        n = tn.rgb * 2.0 - 1.0;
    }
    return normalize(n);
}

// =============================================================================
// IBL contribution  (adapted from tank_fragment.glsl getIBLContribution)
// LOD mapping: roughness = (1 - glossiness)  -> higher roughness = higher LOD.
// At lod=0 we hit raw mip 0 (sharp mirror); higher lods walk the mip chain.
// =============================================================================
vec3 getIBLContribution(PBRInfo pbr, vec3 N_dir, vec3 R_dir)
{
    float roughness    = 1.0 - pbr.perceptualRoughness;       // gloss -> roughness
    float lod          = roughness * MAX_REFLECTION_LOD;

    // IRRADIANCE, APPROXIMATED. The exporter binds a real Lambertian-convolved
    // cube here; nuTerra has no such map - the engine carries its ambient as
    // nine SH coefficients instead - so TankRenderer binds the SAME raw cube to
    // units 4 and 6 and this samples it deep in the mip chain. A box-blurred
    // cube is not a cosine convolution, but at this LOD it is a wide average of
    // the sky and reads as ambient. Swap in a real irradiance cube and drop the
    // LOD to 0 the day nuTerra bakes one.
    //
    // BOTH SAMPLES ARE sRGB DECODED, which the exporter's are not.
    //
    // The exporter bakes its own cube and it is already linear, so mesh.frag
    // reads it straight. nuTerra's is the map's sky cube and it is sRGB
    // encoded - deferred.frag wraps SRGBtoLINEAR around every one of its six
    // reads of this exact texture. Reading it raw treats 0.5 as linear when it
    // means 0.21, which is about 2.4x too bright, and it lands on the diffuse
    // and the reflection together: paint washes toward white and every metal
    // rim goes to chrome. Same class of mistake as the LUT axes below - use
    // the encoding the texture actually has, not the one the shader came from.
    const float IRRADIANCE_LOD = 6.0;
    vec3 diffuseLight  = tank_SRGBtoLINEAR(
                             textureLod(irradianceMap, N_dir, IRRADIANCE_LOD)).rgb;
    vec3 specularLight = tank_SRGBtoLINEAR(
                             textureLod(prefilteredMap, R_dir, lod)).rgb;

    // THE LUT AXES ARE TRANSPOSED FROM THE EXPORTER'S, deliberately.
    //
    // The exporter bakes its own split-sum LUT and reads it (NdotV, roughness).
    // We are not sampling that texture - we are sampling the GAME's
    // system/maps/env_brdf_lut.dds, and deferred.frag:1316 reads that one as
    // (alphaRoughness, NdotV). Sampling a texture with someone else's axis
    // order silently returns the wrong Fresnel-weighted specular everywhere, so
    // match the texture actually bound, not the shader it came from. If the
    // specular ever looks inverted across grazing angles, this line is where to
    // look first - deferred.frag:1341 samples the same LUT a THIRD way and its
    // own comment admits that one is neither axis.
    vec2 brdf_val      = texture(brdfLut, vec2(roughness, pbr.NdotV)).rg;

    vec3 diffuse  = diffuseLight  * pbr.diffuseColor;
    vec3 specular = specularLight * (pbr.specularColor * brdf_val.x + brdf_val.y);

    // !! Intentionally NOT scaled by metal_scale.  The Light slider controls
    // the direct (sun) light intensity only -- the environment contribution is
    // a property of the scene, not the sun.
    return diffuse + specular;
}

// =============================================================================
// Fresnel  (from tank_fragment.glsl specularReflection)
// Exponent 2.0 instead of Schlick's 5.0 -- softer grazing highlight,
// significantly less specular "halo" on rough/rubber surfaces.
// =============================================================================
vec3 specularReflection(PBRInfo pbr)
{
    return pbr.reflectance0
         + (pbr.reflectance90 - pbr.reflectance0)
         * pow(clamp(1.0 - pbr.VdotH, 0.0, 1.0), 2.0);
}

// =============================================================================
// Geometric occlusion  (from tank_fragment.glsl geometricOcclusion)
// Hardcoded r = 0.3 -- conservative, prevents specular blow-out on rough mats.
// =============================================================================
float geometricOcclusion(PBRInfo pbr)
{
    float NdotL = pbr.NdotL;
    float NdotV = pbr.NdotV;
    const float r = 0.3;
    float attL = 2.0 * NdotL / (NdotL + sqrt(r*r + (1.0 - r*r) * NdotL * NdotL));
    float attV = 2.0 * NdotV / (NdotV + sqrt(r*r + (1.0 - r*r) * NdotV * NdotV));
    return attL * attV;
}

// =============================================================================
// NDF  (from tank_fragment.glsl microfacetDistribution)
// Hardcoded roughnessSq = 0.05 -- tight lobe, controlled via gloss scaling.
// =============================================================================
float microfacetDistribution(PBRInfo pbr)
{
    const float roughnessSq = 0.05;
    float f = (pbr.NdotH * roughnessSq - pbr.NdotH) * pbr.NdotH + 1.0;
    return roughnessSq / (M_PI * f * f);
}

// =============================================================================
// Lambertian diffuse  (from tank_fragment.glsl)
// =============================================================================
vec3 diffuseTerm(PBRInfo pbr)
{
    return pbr.diffuseColor / M_PI;
}

// =============================================================================
// ACES filmic tonemap  (Narkowicz 2015)
// =============================================================================
vec3 ACESFilm(vec3 x)
{
    return clamp((x * (2.51 * x + 0.03)) / (x * (2.34 * x + 0.30) + 0.001), 0.0, 1.0);
}

// =============================================================================
// Main
// =============================================================================
void main()
{
    // ---- The sentinel, resolved once ------------------------------------------
    bool  wired     = (tank_shading != 0);
    float sun_level = wired ? metal_scale : 1.0;
    float amb_level = wired ? shine_scale : 1.0;
    bool  use_nmap  = wired ? (apply_normal_map == 1) : true;
    bool  use_ao    = wired ? (apply_ao == 1) : true;

    // ---- Sample textures -------------------------------------------------------
    vec4 diff_samp = (has_maps.x != 0) ? texture(diffuseMap, fs_in.TC1)
                                       : vec4(0.5, 0.5, 0.5, 1.0);
    vec4 norm_samp = texture(normalMap, fs_in.TC1);

    // Alpha test (threshold in sRGB space, before linearise -- matches WoT).
    // Done BEFORE we touch diff_samp.rgb so the alpha threshold is unchanged.
    float alpha = (alpha_in_normal_red == 1) ? norm_samp.r : diff_samp.a;
    if (alpha_test != 0 && alpha < alpha_ref) discard;

    // ---- AM darken: multiply the diffuse sample by itself ---------------------
    // x*x in sRGB space pushes midtones down hard while leaving bright spots
    // mostly intact:  0.2 -> 0.04  (5x darker),  0.5 -> 0.25  (2x darker),
    // 0.9 -> 0.81  (barely changed).  Kept separate from the real sRGB->linear
    // below so it acts as an extra contrast/saturation boost on top of it.
    diff_samp.rgb *= diff_samp.rgb;

    // ---- Damage layer  (PBS_tank_crash.fx) ------------------------------------
    // crash_tile.dds packs THREE GRAYSCALE damage variants into R, G and B.  The
    // shader picks ONE channel per region of the tank and uses it as a
    // multiplicative mask between AM and black:
    //     mask=1  ->  AM unchanged  (clean spot)
    //     mask=0  ->  AM goes black (full damage / scorch)
    // No colour from the tile reaches the diffuse - everything stays in the
    // original tank-paint hue, just darkened in the damaged regions.
    //
    // Channel selection is by coarse world-space hash so neighbouring surfaces
    // share the same pattern (no per-pixel chatter) but hull / turret / chassis
    // end up on different channels, breaking the obvious tile repeat.  The 0.6 m
    // grid is roughly one armour-panel chunk.
    float damage_mask = 0.0;
    if (has_crash_tile == 1) {
        vec2 tiled_uv = fs_in.TC1 * crash_uv_tiling.xy + crash_uv_tiling.zw;
        vec3 damage_rgb = texture(crashTileMap, tiled_uv).rgb;

        // Standard Quake-style sin-hash constants; good distribution across
        // small integer inputs.  crash_channel_offset shifts every region's pick
        // so the panel that came up "channel R" on one damaged load comes up G
        // the next and B the next, rotating through all three authored variants.
        vec3  hash_pos = floor(fs_in.worldPosition * (1.0 / 0.6));
        float h        = fract(sin(dot(hash_pos,
                              vec3(12.9898, 78.233, 37.719)))
                          * 43758.5453);
        int   region_pick = int(h * 3.0);   // 0/1/2 from the hash
        int   chan        = (region_pick + crash_channel_offset) % 3;
        float mask;
        if      (chan == 0) mask = damage_rgb.r;
        else if (chan == 1) mask = damage_rgb.g;
        else                mask = damage_rgb.b;

        float effective_mask = mix(1.0, mask,
                                   clamp(crash_coefficient, 0.0, 1.0));
        diff_samp.rgb *= effective_mask;
        damage_mask = clamp(1.0 - effective_mask, 0.0, 1.0);
    }

    // ---- AO  (WoT: applied to raw sRGB color BEFORE linearisation) ------------
    // ao_in_diffuse_alpha: skinned/track meshes store AO in diffuse alpha.
    // has_maps.w: standalone AO texture, sample .g channel (WoT aoMap.g).
    float ao = 1.0;
    if (use_ao) {
        if (ao_in_diffuse_alpha == 1) {
            ao = diff_samp.a;
        } else if (has_maps.w != 0) {
            ao = texture(aoMap, fs_in.TC1).g;
        }
    }
    vec4 color = diff_samp;
    color.rgb *= ao;              // WoT: color.rgb *= AO.g  (sRGB domain)

    // ---- Nation armor color tinting -------------------------------------------
    // Luminance-preserving multiplicative tint.  armor_color is divided by its
    // own Rec.601 luminance so the tint vector has perceived brightness ~1;
    // multiplying the diffuse by it shifts HUE without dimming the surface, and
    // texture detail is fully preserved.
    if (has_armor_color == 1) {
        float armor_luma = dot(armor_color, vec3(0.299, 0.587, 0.114));
        vec3  armor_tint = armor_color / max(armor_luma, 0.05);
        color.rgb       *= armor_tint;
    }

    // ---- sRGB -> linear  (WoT: after AO + armor tint) ------------------------
    color = tank_SRGBtoLINEAR(color);

    // ---- GMM material parameters  (WoT tank_fragment.glsl layout) ------------
    // GMM.r = gloss:  HIGH = shiny.  perceptualRoughness stores GLOSSINESS.
    // GMM.g = metal:  metallic boosted 1.5x then clamped (WoT convention).
    float perceptualRoughness = 0.2;    // gloss default (moderate shine)
    float metallic            = 0.0;
    float gloss_raw           = 0.2;    // raw GMM.r, used to scale the NDF
    if (has_maps.z != 0) {
        vec3 gmm            = texture(gmmMap, fs_in.TC1).rgb;
        gloss_raw           = gmm.r;
        perceptualRoughness = clamp(pow(gmm.r / 0.8, 7.0), c_MinRoughness, 1.0);
        metallic            = clamp(pow(gmm.g / 0.5, 5.0) * 1.5, 0.0, 1.0);
    }
    // Damage suppresses chrome/shine - scuffed dirty surfaces lose metallic and
    // gloss in proportion to the damage coverage.  Without this the crash tile
    // reads as "shiny dirt".  Gloss is attenuated harder than metallic because
    // rust and scratches roughen a surface faster than they strip its metallic
    // character.
    if (damage_mask > 0.0) {
        metallic            *= (1.0 - damage_mask * 0.85);
        gloss_raw           *= (1.0 - damage_mask * 0.95);
        perceptualRoughness *= (1.0 - damage_mask * 0.95);
    }
    // alphaRoughness = (1 - gloss)^2  (actual material roughness squared)
    float roughness      = 1.0 - perceptualRoughness;
    float alphaRoughness = roughness * roughness;

    // ---- PBR material setup  (from tank_fragment.glsl) -----------------------
    vec3 f0            = vec3(0.04);
    vec3 diffuseColor  = color.rgb * (vec3(1.0) - f0) * (1.0 - metallic);
    vec3 specularColor = mix(f0, color.rgb, metallic);

    float reflectance   = max(max(specularColor.r, specularColor.g), specularColor.b);
    float reflectance90 = clamp(reflectance * 25.0, 0.0, 1.0);
    vec3  specEnvR0     = specularColor;
    vec3  specEnvR90    = vec3(reflectance90);

    // ---- Shading normals ------------------------------------------------------
    // N_geom : the un-perturbed geometry normal.  Used by the IBL reflection
    //          vector so cubemap reflections sweep with the BIG SHAPE of the part
    //          instead of jittering across normal-map detail (matches
    //          tank_fragment.glsl, where R comes from v_Normal, not n).
    // N      : the (possibly normal-map-perturbed) shading normal used by all
    //          per-fragment lighting math and by the sSpec Phong vector below.
    vec3 tangent_n = vec3(0.0, 0.0, 1.0);
    bool bumped    = (has_maps.y != 0) && use_nmap;
    if (bumped) tangent_n = unpackNormal(fs_in.TC1);

    vec3 N_geom = normalize(fs_in.worldNormal);
    vec3 N      = bumped ? normalize(fs_in.worldTBN * tangent_n) : N_geom;

    // ---- View vector (light-independent) -------------------------------------
    vec3  v     = normalize(cameraPos - fs_in.worldPosition);   // toward camera
    float NdotV = abs(dot(N, v)) + 0.001;

    // tank_fragment.glsl uses directional lights (no 1/d^2 fall-off).  We do the
    // same so the three "fill" lights give consistent brightness across the whole
    // mesh regardless of distance, matching WoT's three-directional setup.
    //
    // Unwired, the rig rides with the CAMERA so the tank is never black before
    // TankRenderer is rebuilt: three directions 120 degrees apart about the view
    // vector, lifted, which is the shape the exporter's turntable rig makes.
    vec3  l_arr[NUM_LIGHTS];
    float NdotL_arr[NUM_LIGHTS];
    float NdotL_max = 0.001;
    for (int i = 0; i < NUM_LIGHTS; ++i) {
        vec3 l;
        if (wired) {
            l = normalize(light_pos[i] - fs_in.worldPosition);
        } else {
            float a = float(i) * (2.0 * M_PI / float(NUM_LIGHTS));
            l = normalize(v + vec3(cos(a), 0.6, sin(a)));
        }
        l_arr[i]     = l;
        NdotL_arr[i] = clamp(dot(N, l), 0.001, 1.0);
        NdotL_max    = max(NdotL_max, NdotL_arr[i]);
    }

    // PBRInfo for IBL.  Only NdotV / perceptualRoughness / colours are consulted
    // by getIBLContribution; the other slots receive NdotL_max as a placeholder.
    PBRInfo pbrInputs = PBRInfo(
        NdotL_max, NdotV, NdotL_max, NdotL_max, NdotL_max,
        perceptualRoughness, metallic,
        specEnvR0, specEnvR90, alphaRoughness,
        diffuseColor, specularColor
    );

    // ==========================================================================
    // 1. Ambient fill  (tank_fragment.glsl: diffuse * A_level * 0.25 * (1+NdotL))
    //    Uses the brightest light's NdotL so the lit side still gets the
    //    wraparound boost even with multiple fill lights.
    // ==========================================================================
    vec3 base_diffuse = diffuseTerm(pbrInputs);
    vec3 Lo_ambient   = base_diffuse * amb_level * 0.25 * (1.0 + NdotL_max);
    vec3 result       = Lo_ambient;

    // ==========================================================================
    // 2. IBL contribution  (tank_fragment.glsl: NdotV * IBL * gloss)
    //    Rough surfaces (low gloss) receive less environment light - further
    //    suppresses rubber reflections.
    //
    //    Diffuse uses the perturbed N (a wide cosine average - bumps do not
    //    shimmer the way they do for specular).  Specular uses the GEOMETRY
    //    normal so glass / chrome / headlight lenses get clean reflections
    //    instead of mushy bumpy ones.
    // ==========================================================================
    if (has_irradiance == 1 && has_prefiltered == 1 && has_brdf_lut == 1)
    {
        vec3 R = reflect(-v, N_geom);
        result += getIBLContribution(pbrInputs, N, R)
                  * NdotV * perceptualRoughness;
    }

    // ==========================================================================
    // 3. Direct light contribution -- looped over NUM_LIGHTS sources.
    //
    //    sSpec is a Phong "scratch" highlight modulated by detail and metallic.
    //    The detail texture tiles ~7x across the surface and provides the
    //    per-pixel variation that gives chrome / lens surfaces their punchy
    //    sparkle.  Uses the BUMPED reflection so micro-detail flickers across the
    //    highlight (intentional, matches WoT).
    //
    //    metal_scale acts as a single sun-brightness knob.  The accumulated
    //    3-light sum is divided by NUM_LIGHTS so total brightness stays in the
    //    same neighbourhood as a single-light setup at slider = 1.
    // ==========================================================================
    vec3 R_bump = reflect(-v, N);
    float detail_rg;
    if (has_detail_map == 1) {
        vec4 detail = texture(detailMap, fs_in.TC1 * detail_tiling);
        detail_rg = detail.r * detail.g;
    } else {
        detail_rg = 0.4;     // collapses scrach to metallic
    }
    float scrach = pow(detail_rg / 0.4, 2.0) * metallic;

    vec3 Lo_direct = vec3(0.0);
    for (int i = 0; i < NUM_LIGHTS; ++i) {
        vec3  l     = l_arr[i];
        vec3  h     = normalize(v + l);
        float NdotL = NdotL_arr[i];
        float NdotH = clamp(dot(N, h), 0.0, 1.0);
        float LdotH = clamp(dot(l, h), 0.0, 1.0);
        float VdotH = clamp(dot(v, h), 0.0, 1.0);

        PBRInfo pbr_i = PBRInfo(
            NdotL, NdotV, NdotH, LdotH, VdotH,
            perceptualRoughness, metallic,
            specEnvR0, specEnvR90, alphaRoughness,
            diffuseColor, specularColor
        );

        vec3  F_i = specularReflection(pbr_i);
        float G_i = geometricOcclusion(pbr_i);
        // D weighted by raw gloss; rubber / matte surfaces stay matte
        float D_i = microfacetDistribution(pbr_i) * gloss_raw;

        vec3 diff_i  = (1.0 - F_i) * diffuseTerm(pbr_i);
        vec3 spec_i  = F_i * G_i * D_i / (4.0 * NdotL * NdotV);
        vec3 sSpec_i = vec3(1.0) * pow(max(dot(R_bump, l), 0.0), 10.0) * scrach;

        Lo_direct += NdotL
                   * (sSpec_i + diff_i + spec_i * perceptualRoughness * 6.0);
    }
    result += Lo_direct * (10.0 * sun_level / float(NUM_LIGHTS));

    // ---- Muzzle-flash omni contribution -------------------------------------
    // WoT's gun_effects.xml <light> block is an omnidirectional point at
    // HP_gunFire: position plus inner/outer radius, no direction field.  Added
    // BEFORE the tonemap so it tonemaps alongside the rest.  No-op when
    // intensity <= 0.  Lambertian NdotL handles the angular falloff, so
    // back-facing surfaces stay dark for free.
    if (u_mflash_intensity > 0.0) {
        vec3 dL = u_mflash_pos - fs_in.worldPosition;
        float d = length(dL);
        if (d > 1e-4 && d < u_mflash_outer) {
            vec3 L = dL / d;
            float falloff = 1.0 - smoothstep(u_mflash_inner, u_mflash_outer, d);
            float ndl = max(dot(N, L), 0.0);
            result += (u_mflash_color * u_mflash_intensity * falloff * ndl
                       * diffuseColor);
        }
    }

    // ACES filmic tonemap then sRGB gamma
    result = ACESFilm(result);
    result = pow(clamp(result, 0.0, 1.0), vec3(1.0 / 2.2));

    // ---- Out, into the G-buffer -----------------------------------------------
    // gColor carries the FINISHED colour and gGMF.b is GFLAG_UNLIT, so the
    // resolve hands it to the screen untouched. The rest of the G-buffer is
    // still filled in properly: gNormal in VIEW space - its own TBN, not the
    // world one - gPosition in view space, gSurfaceNormals flat. Everything
    // downstream that reads depth rather than lighting keeps working.
    vec3 n_view = bumped ? normalize(fs_in.TBN * tangent_n)
                         : normalize(fs_in.surfaceNormal);

    gColor          = vec4(result, 0.0);
    gNormal         = n_view * 0.5 + 0.5;
    gGMF            = vec4(gloss_raw, metallic, GFLAG_UNLIT, 0.0);
    gPosition       = fs_in.viewPosition;
    gSurfaceNormals = fs_in.surfaceNormal;
}
