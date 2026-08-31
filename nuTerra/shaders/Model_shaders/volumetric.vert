#version 450 core

#extension GL_ARB_bindless_texture : require
#extension GL_ARB_shader_draw_parameters : require
#extension GL_ARB_shading_language_include : require

#define USE_PERVIEW_UBO
#define USE_COMMON_PROPERTIES_UBO
#define USE_MODELINSTANCES_SSBO
#define USE_CANDIDATE_DRAWS_SSBO
#define USE_MATERIALS_SSBO
#include "common.h" //! #include "../common.h"

// Transcription of the game's volumetric_effect_vtx vertex shader (fxo
// disassembly, vs blob 0). Material parameters ride the generic GLMaterial
// vec4 slots - the mapping lives in MapLoader.load_materials and MUST stay
// in lockstep:
//   g_colorTint   = TintlColor
//   dirtParams    = diffuseUVSpeedAlphaOffset (xy scroll, z alphaOffset,
//                   w = alphaFreshnelEnable variant selector)
//   dirtColor     = distortion_UV_Speed_Amount (xy scroll, zw warp amounts)
//   g_tile0Tint   = lightMultipliers
//   g_tile1Tint   = selfIllumLight
//   g_tile2Tint   = FreshnelColor
//   g_tileUVScale = alphaFadeAmountFresnel (x fade base, y gain, z fresnel exp, w fresnel alpha)

layout(location = 0) in vec3 vertexPosition;
layout(location = 1) in vec4 vertexNormal;
layout(location = 4) in vec2 vertexTexCoord1;
layout(location = 6) in vec4 vertexColour;

uniform float fx_time;

// The ambient probe, same coefficients the deferred pass lights the scene
// with, so smoke and the ground it sits on agree about the sky.
uniform vec3 sh_ambient[9];
uniform int  sh_enabled;

// Ramamoorthi & Hanrahan irradiance, identical to deferred.frag's. The game
// evaluates only the constant and linear bands here (a dp4 against
// (1, n.x, n.y, n.z)); using the full set costs nothing and keeps this in
// lockstep with the scene rather than introducing a second ambient.
vec3 fx_sh_irradiance(vec3 n)
{
    const float c1 = 0.429043, c2 = 0.511664, c3 = 0.743125;
    const float c4 = 0.886227, c5 = 0.247708;

    return c4 * sh_ambient[0]
         + 2.0 * c2 * (sh_ambient[1] * n.y + sh_ambient[2] * n.z + sh_ambient[3] * n.x)
         + 2.0 * c1 * (sh_ambient[4] * n.x * n.y
                     + sh_ambient[5] * n.y * n.z
                     + sh_ambient[7] * n.x * n.z)
         + c3 * sh_ambient[6] * n.z * n.z
         - c5 * sh_ambient[6]
         + c1 * sh_ambient[8] * (n.x * n.x - n.y * n.y);
}

// ---------------------------------------------------------------------------
// The baked probe FIELD, the same texture and the same evaluation the deferred
// pass runs. Ported so a smoke column standing in a shaded courtyard and the
// ground under it read the SAME probe, instead of the smoke reading one flat
// global probe while the ground is already field-lit. Unit 11, as there.
//
// TWO uniforms deliberately do NOT carry the deferred pass's values, and
// draw_fx says so where it uploads them:
//   sh_grid_enabled - also gated on USE_SH_GRID_FX, which defaults off.
//   sh_grid_offset  - sent as 0. The 1.5 m push exists to bias a WALL's lookup
//                     out of the near-black probes baked inside buildings. A
//                     smoke card is already in open air, and its normals are
//                     not a coherent billboard, so the push would scatter
//                     neighbouring vertices' lookups by up to a whole probe
//                     cell and manufacture mottling that is not in the field.
// Everything else is byte-identical to the deferred uniforms on purpose: if
// they ever diverge, smoke and ground are lit by two different fields and the
// whole point of this is lost.
// ---------------------------------------------------------------------------
layout(binding = 11) uniform sampler3D sh_grid;
uniform int   sh_grid_enabled;
uniform vec4  sh_grid_uv;     // xy = offset, zw = 1/size, the game's packing
uniform float sh_grid_fade;   // 1 / fade distance in metres
uniform float sh_grid_offset; // metres to push the lookup along the normal
uniform float sh_grid_edge;   // uv width of the ease-out at the box edge
uniform vec3  sh_grid_sh9[9]; // the FIELD's own companion probe, not the global
uniform float sh_grid_mix;

// Copy of deferred.frag's eval_sh_grid_fallback. Separate from
// fx_sh_irradiance above on purpose: the working flat-probe path is left byte
// for byte alone.
vec3 fx_sh_grid_fallback(vec3 n)
{
    const float c1 = 0.429043, c2 = 0.511664, c3 = 0.743125;
    const float c4 = 0.886227, c5 = 0.247708;

    return c4 * sh_grid_sh9[0]
         + 2.0 * c2 * (sh_grid_sh9[1] * n.y + sh_grid_sh9[2] * n.z + sh_grid_sh9[3] * n.x)
         + 2.0 * c1 * (sh_grid_sh9[4] * n.x * n.y
                     + sh_grid_sh9[5] * n.y * n.z
                     + sh_grid_sh9[7] * n.x * n.z)
         + c3 * sh_grid_sh9[6] * n.z * n.z
         - c5 * sh_grid_sh9[6]
         + c1 * sh_grid_sh9[8] * (n.x * n.x - n.y * n.y);
}

// Copy of deferred.frag's eval_sh_grid, character for character in the maths.
// Do NOT "improve" it here - in particular the SH bands are evaluated against
// an UNMIRRORED n while the LOOKUP is mirrored (modRender's negative scale_x).
// That is a real defect and it is already in the deferred path; fixing it on
// one side only would make smoke and ground disagree, which is exactly what
// this change exists to stop.
vec3 fx_eval_sh_grid(vec3 world_pos, vec3 n)
{
    vec3 sample_pos = world_pos + n * sh_grid_offset;
    vec2 uv = sample_pos.xz * sh_grid_uv.zw - sh_grid_uv.xy;

    // Slice centres of an 8 deep texture, so a Linear filter never straddles
    // two coefficient vectors.
    const float S = 1.0 / 8.0;
    vec4 c0 = textureLod(sh_grid, vec3(uv, 0.5 * S), 0.0);
    vec4 c1 = textureLod(sh_grid, vec3(uv, 1.5 * S), 0.0);
    vec4 c2 = textureLod(sh_grid, vec3(uv, 2.5 * S), 0.0);
    vec4 c3 = textureLod(sh_grid, vec3(uv, 3.5 * S), 0.0);
    vec4 c4 = textureLod(sh_grid, vec3(uv, 4.5 * S), 0.0);
    vec4 c5 = textureLod(sh_grid, vec3(uv, 5.5 * S), 0.0);
    vec4 c6 = textureLod(sh_grid, vec3(uv, 6.5 * S), 0.0);

    vec2  edge    = min(uv, 1.0 - uv);
    float outside = 1.0 - smoothstep(0.0, sh_grid_edge, min(edge.x, edge.y));

    // c6.w is the probe's stored reference height in metres, not a
    // coefficient. NOTE this is the dominant term for smoke: a card floating a
    // few metres above the ground is already partway through the fade to the
    // companion probe, where the ground under it is barely faded at all.
    // Expect a plume to read dimmer and flatter with altitude - that is the
    // field's own behaviour, faithfully ported, not a bug in the port.
    float height_fade = clamp((world_pos.y - c6.w) * sh_grid_fade, 0.0, 1.0);
    float blend = outside * (1.0 - height_fade) + height_fade;

    // The game's own pre-convolved packing - one dot per band.
    vec3 lin  = vec3(dot(c0.wyz, n), dot(c1.wyz, n), dot(c2.wyz, n));
    vec4 q    = vec4(n.y * n.x, n.z * n.y, n.z * n.z, n.x * n.z);
    vec3 quad = vec3(dot(c3, q), dot(c4, q), dot(c5, q))
              + c6.xyz * (n.x * n.x - n.y * n.y);
    vec3 local = max(vec3(c0.x, c1.x, c2.x) + lin + quad, vec3(0.0));

    // Both ends of the blend evaluated the same way, or the mix would step.
    return mix(local, max(fx_sh_grid_fallback(n), vec3(0.0)), blend);
}

out VS_OUT
{
    vec4 diffUV_negAmt;   // xy scrolled diffuse UV (+half warp), zw -warp amount
    vec2 distUV;          // scrolled distortion UV
    vec4 litColor;        // vertex colour x lighting x tint, alpha shaped
    float viewDist;       // clip-space w, the PS distance fade input
    flat uint material_id;
} vs_out;

void main(void)
{
    const CandidateDraw thisDraw = draw[gl_BaseInstanceARB];
    const ModelInstance thisModel = models[thisDraw.model_id];
    const MaterialProperties mat = material[thisDraw.material_id];

    vs_out.material_id = thisDraw.material_id;

    const vec4 worldPos = thisModel.matrix * vec4(vertexPosition, 1.0);
    gl_Position = thisModel.cached_mvp * vec4(vertexPosition, 1.0);
    vs_out.viewDist = gl_Position.w;

    // Fresnel against the world normal - the game raises 1-|V.N| to
    // alphaFadeAmountFresnel.z and uses it to pull the vertex colour toward
    // FreshnelColor and to thin the vertex alpha at grazing angles.
    // alphaFreshnelEnable selects a compiled variant WITHOUT this block
    // (fxo vs blobs alternate on it) - Abbey's fire smoke authors it off.
    const vec3 N = normalize(mat3(thisModel.matrix) * vertexNormal.xyz);
    const vec3 V = normalize(cameraPos - worldPos.xyz);
    float fres = pow(max(1.0 - abs(dot(V, N)), 0.0), mat.g_tileUVScale.z);
    fres *= mat.dirtParams.w; // 0 when the fresnel variant is off

    vec4 vcol = vertexColour;
    vcol.rgb = mix(vcol.rgb, mat.g_tile2Tint.rgb, fres);
    vcol.a = vcol.a * (1.0 - fres * (1.0 + mat.g_tileUVScale.w));

    // UV animation. The warp amount is scaled by |alphaOffset - vertexAlpha|,
    // so transparent edge vertices billow harder than the solid core - that
    // is the game's trick, kept verbatim. The +0.5*amt bias centres the warp
    // because the PS applies dist.rg * (-amt).
    const float amtScale = abs(mat.dirtParams.z - vcol.a);
    const vec2 amt = amtScale * mat.dirtColor.zw;
    vs_out.diffUV_negAmt.xy = fx_time * mat.dirtParams.xy + vertexTexCoord1 + 0.5 * amt;
    vs_out.diffUV_negAmt.zw = -amt;
    vs_out.distUV = fx_time * mat.dirtColor.xy + vertexTexCoord1;

    // Lighting, through the same lightMultipliers roles the game uses
    // (y sun, z ambient). The ambient comes from the SH probe, as it does in
    // the game - the flat ambientColorForward stand-in that used to be here
    // landed on almost exactly ground brightness (smoke 67 against ground 69),
    // so the smoke had no contrast to be seen by however correct its alpha was.
    vec3 light = vec3(1.0);
    if (mat.g_enableAO) { // slot carries enableLighting
        // The ternary became an if/else so the field lands on the PROBE branch
        // only. deferred.frag nests it the same way, and with SH ambient off
        // the stand-in below must stay the flat stand-in: mixing the baked
        // field onto ambientColorForward would be a third ambient that exists
        // nowhere else in the renderer, and use_sh_ambient is a persisted
        // per-map key, so that state is reachable from a settings file.
        //
        // This is a RESHAPE of the two existing outcomes, not a change to
        // either - both are preserved byte for byte.
        vec3 ambient;
        if (sh_enabled != 0) {
            ambient = max(fx_sh_irradiance(N), vec3(0.0));
            // The same three lines deferred.frag uses. The field answers the
            // same question with POSITION as well as normal, so where it
            // exists it replaces the single global probe. worldPos is already
            // in scope from above and is the same space deferred reconstructs
            // with invView, so no new varying is needed. With sh_grid_enabled
            // at 0 not one instruction below runs.
            if (sh_grid_enabled != 0) {
                vec3 grid_amb = max(fx_eval_sh_grid(worldPos.xyz, N), vec3(0.0));
                ambient = max(mix(ambient, grid_amb, sh_grid_mix), vec3(0.0));
            }
        } else {
            ambient = props.ambientColorForward * (0.6 + 0.4 * max(N.y, 0.0));
        }
        light = max(ambient, vec3(0.0001)) * mat.g_tile0Tint.z
              + mat.g_tile0Tint.y * props.sunColor;
    }
    light += mat.g_tile1Tint.rgb; // selfIllumLight

    vs_out.litColor = vec4(vcol.rgb * light, vcol.a) * mat.g_colorTint;

    // lightMultipliers.x is a FINAL gain on the whole lit colour, applied
    // after the tint and OUTSIDE the lighting branch, so it counts even for
    // an unlit material. The fxo's vertex shader ends on exactly this:
    //     mul o3.xyz, r1.xyzx, cb0[77].xxxx
    // rgb only - alpha is untouched. Leaving it out made every fire material
    // render at the same brightness, though Abbey authors them from x2 to
    // x60, which is what flattened the flames.
    vs_out.litColor.rgb *= mat.g_tile0Tint.x;
}
