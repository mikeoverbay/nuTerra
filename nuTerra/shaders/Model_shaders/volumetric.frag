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

// Scene position for the soft-particle fade, view space despite the name its
// writers use. Unit 3, matching deferred.frag.
layout (binding = 3) uniform sampler2D gPosition;

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
    // alphaFadeAmountFresnel.y is the remap gain (fxo: mul_sat r0.w, r0.w,
    // cb0[80].y). Abbey's smoke authors 1, the fires 2..60.
    const float gain = mat.g_tileUVScale.y;

    // The two compiled pixel variants, verified against volumetric_effect_vtx
    // blobs 8 and 9, selected by ALPHA TRIM (g_atlasIndexes.z) - the fxo's
    // annotations label alphaAdditiveEnable "Use Alpha Trim", and that is the
    // switch, not alphaFreshnelEnable. Trim on is the fire cutout; trim off is
    // soft smoke.
    // Soft particles. The game fades a card out as it approaches whatever is
    // behind it, which is what stops a sheet cutting a hard straight line
    // where it meets the ground:
    //     softFade = sat((sceneDepth - viewDist) / softFactor)
    // Nothing drawn at this pixel leaves gPosition at zero - that is sky, not
    // geometry one metre away, so it must read as infinitely far or every card
    // seen against the sky would vanish.
    const vec3 scenePos = texelFetch(gPosition, ivec2(gl_FragCoord.xy), 0).xyz;
    const float sceneDist = (abs(scenePos.z) < 1e-6) ? 1e30 : -scenePos.z;
    const float softFade = clamp((sceneDist - fs_in.viewDist)
                                 / max(mat.g_atlasIndexes.w, 1e-4), 0.0, 1.0);

    // The vertex-alpha term both variants share, softened.
    const float lit = fs_in.litColor.a * fade * softFade;

    float alpha;
    if (mat.g_atlasIndexes.z != 0.0) {
        alpha = clamp((tex.a + lit - 1.0) * gain, 0.0, 1.0);
    } else {
        alpha = clamp(tex.a * lit, 0.0, 1.0);
    }

    // Highlight compression, hue preserving.
    //
    // These materials author light multipliers from x2 to x60, so a fire
    // legitimately produces colours near (10, 3.5, 0). gColor is Rgba8 with no
    // HDR path, so writing that clips PER CHANNEL to (1, 1, 0): flat yellow,
    // and the hue the artist authored is destroyed. Measured against a capture
    // of the game at the same fire, 35.4% of our fire pixels had R and G both
    // pinned against 0.0% of the game's.
    //
    // Reinhard on LUMINANCE rather than per channel divides all three by the
    // same factor, so the ratio between them survives: (10, 3.5, 0) becomes
    // about (1, 0.62, 0) - orange that clips only in red - where per-channel
    // clipping gave pure yellow. The game reaches this with a real HDR buffer
    // and a filmic curve; this is the closest an 8-bit target gets without
    // restructuring the frame, and it is judged against that capture rather
    // than by eye.
    vec3 fx_rgb = tex.rgb * fs_in.litColor.rgb;
    const float fx_lum = dot(fx_rgb, vec3(0.2126, 0.7152, 0.0722));
    fx_rgb /= (1.0 + fx_lum);
    const vec3 rgb = fx_rgb;

    if (mat.alphaTestEnable) { // slot carries alphaAdditiveEnable
        outColor = vec4(rgb * alpha, 0.0);
    } else {
        outColor = vec4(rgb * alpha, alpha);
    }
}
