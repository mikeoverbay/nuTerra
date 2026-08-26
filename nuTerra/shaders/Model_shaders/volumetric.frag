#version 450 core

#extension GL_ARB_bindless_texture : require
#extension GL_ARB_shading_language_include : require

#define USE_MATERIALS_SSBO
#include "common.h" //! #include "../common.h"

// Transcription of the game's volumetric_effect_vtx pixel shader (fxo
// disassembly, ps blob 8), minus the vis-tunnel dither (a vehicle-blocking-
// view fade a map viewer has no use for) and the engine's exposure/fog
// hookups, which nuTerra applies in its own passes.
//
// The pass draws premultiplied with blend (ONE, ONE_MINUS_SRC_ALPHA) so one
// multidraw can serve both blend modes: alpha materials output (rgb*a, a),
// additive materials (alphaTestEnable slot) output (rgb*a, 0).

layout (location = 0) out vec4 outColor;

in VS_OUT
{
    vec4 diffUV_negAmt;
    vec2 distUV;
    vec4 litColor;
    float viewDist;
    flat uint material_id;
} fs_in;

void main(void)
{
    const MaterialProperties mat = material[fs_in.material_id];
    const sampler2D diffuseMap = sampler2D(mat.maps[0]);
    const sampler2D distortionMap = sampler2D(mat.maps[1]);

    // Warp the diffuse lookup by the scrolling distortion (velocity) map -
    // this is what makes the smoke billow instead of sliding as a sheet.
    const vec2 dist = texture(distortionMap, fs_in.distUV).xy;
    const vec2 uv = dist * fs_in.diffUV_negAmt.zw + fs_in.diffUV_negAmt.xy;
    const vec4 tex = texture(diffuseMap, uv);

    // Alpha. Distance fade-in over the authored fadeMin/MaxDistance window
    // (g_atlasIndexes.xy; register defaults 0.01/1.0 saturate past a metre,
    // but backdrop sheets author real ranges - SmokeBotton 150..400 - so
    // they exist only at distance). The fade-base add is SATURATED, matching
    // the fxo's add_sat: unclamped it doubled the vertex-alpha weight on
    // every fade=1 material and made fog and smoke cores far too dense.
    //
    // alphaFreshnelEnable (dirtParams.w) selects between the fxo's two
    // compiled pixel variants:
    //   on  (ps blob 8): alpha = sat((texA + vertA*fade - 1) * gain)
    //   off (ps blob 9): alpha = sat(texA * vertA * fade) - no remap, no
    //       gain (materials built for this variant author gain as junk;
    //       forcing them through the remap made Abbey's smoke invisible).
    float fade = clamp((fs_in.viewDist - mat.g_atlasIndexes.x)
                     / (mat.g_atlasIndexes.y - mat.g_atlasIndexes.x), 0.0, 1.0);
    fade = clamp(fade + mat.g_tileUVScale.x, 0.0, 1.0);
    float alpha;
    if (mat.dirtParams.w != 0.0) {
        alpha = clamp((tex.a + fs_in.litColor.a * fade - 1.0) * mat.g_tileUVScale.y, 0.0, 1.0);
    } else {
        alpha = clamp(tex.a * fs_in.litColor.a * fade, 0.0, 1.0);
    }

    const vec3 rgb = tex.rgb * fs_in.litColor.rgb;

    if (mat.alphaTestEnable) { // slot carries alphaAdditiveEnable
        outColor = vec4(rgb * alpha, 0.0);
    } else {
        outColor = vec4(rgb * alpha, alpha);
    }
}
