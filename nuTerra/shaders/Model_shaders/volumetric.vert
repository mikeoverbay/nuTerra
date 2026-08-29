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
        vec3 ambient = (sh_enabled != 0)
                     ? max(fx_sh_irradiance(N), vec3(0.0))
                     : props.ambientColorForward * (0.6 + 0.4 * max(N.y, 0.0));
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
