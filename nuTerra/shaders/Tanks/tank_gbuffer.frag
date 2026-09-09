#version 450 core
#extension GL_ARB_shading_language_include : require

#include "common.h" //! #include "../common.h"

// Tank G-buffer pass, fragment stage. Writes what model.frag writes, in the
// same convention, so the deferred pass lights the tank like any model:
//   gColor    albedo, alpha 0 (no fake shadow)
//   gNormal   view-space bumped normal * 0.5 + 0.5
//   gGMF      r gloss, g metal (the GMM map's .rg, raw), b GFLAG_MODEL, a 0 (no occlusion)
//   gPosition view-space position
// Milestone 1 only. The owner's tank shader replaces this at milestone 2 and
// writes GFLAG_UNLIT so the resolve leaves its pixels alone.

layout(binding = 0) uniform sampler2D diffuseMap;     // *_AM
layout(binding = 1) uniform sampler2D normalMap;      // *_ANM, normal in .ag
layout(binding = 2) uniform sampler2D gmmMap;         // *_GMM, gloss in .r, metal in .g
layout(binding = 3) uniform sampler2D aoMap;          // *_AO

uniform ivec4 has_maps;      // AM, ANM, GMM, AO present
uniform int   alpha_test;
uniform float alpha_ref;
uniform int   normal_dxt1;   // g_useNormalPackDXT1: normal in .rgb instead of .ag

layout(location = 0) out vec4 gColor;
layout(location = 1) out vec3 gNormal;
layout(location = 2) out vec4 gGMF;
layout(location = 3) out vec3 gPosition;
layout(location = 4) out vec3 gSurfaceNormals;

in VS_OUT
{
    vec2 TC1;
    vec2 TC2;
    vec3 viewPosition;
    mat3 TBN;
    flat vec3 surfaceNormal;
} fs_in;

void main(void)
{
    vec4 albedo = (has_maps.x != 0) ? texture(diffuseMap, fs_in.TC1) : vec4(0.5, 0.5, 0.5, 1.0);
    if (alpha_test != 0 && albedo.a < alpha_ref) discard;

    vec3 bump = vec3(0.0, 0.0, 1.0);
    if (has_maps.y != 0) {
        vec4 nm = texture(normalMap, fs_in.TC1);
        if (normal_dxt1 != 0) {
            bump = nm.rgb * 2.0 - 1.0;
        } else {
            bump.xy = nm.ag * 2.0 - 1.0;
            float dp = min(dot(bump.xy, bump.xy), 1.0);
            bump.z = clamp(sqrt(1.0 - dp), -1.0, 1.0);
        }
        bump = normalize(bump);
    }
    vec3 n = normalize(fs_in.TBN * bump);

    vec2 gm = (has_maps.z != 0) ? texture(gmmMap, fs_in.TC1).rg : vec2(0.2, 0.0);

    gColor = vec4(albedo.rgb, 0.0);
    gNormal = n * 0.5 + 0.5;
    gGMF = vec4(gm.r, gm.g, GFLAG_MODEL, 0.0);
    gPosition = fs_in.viewPosition;
    gSurfaceNormals = fs_in.surfaceNormal;
}
