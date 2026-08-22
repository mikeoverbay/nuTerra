#version 450 core

#extension GL_ARB_bindless_texture : require
#extension GL_ARB_shading_language_include : require

#include "common.h" //! #include "../common.h"

layout (location = 0) out vec4 gColor;
layout (location = 1) out vec3 gNormal;
layout (location = 2) out vec4 gGMF;
layout (location = 3) out vec3 gPosition;
layout (location = 4) out vec3 gSurfaceNormals;

in VS_OUT
{
    vec2 TC;
    vec3 worldPosition;
    vec3 normal;
    vec4 colour;
    flat uvec2 texHandle;
} fs_in;

void main(void)
{
    vec4 albedo = texture(sampler2D(fs_in.texHandle), fs_in.TC);

    // Two things gate the decal: the atlas is cut out around the road, and
    // the colour stream's alpha is a per-vertex mix weight that feathers the
    // patch into the terrain. Multiply them and let the blender do the rest.
    float mix_weight = albedo.a * fs_in.colour.a;
    if (mix_weight < 0.004) {
        discard;               // contributes nothing, so touch no attachment
    }

    const float renderType = GFLAG_ROAD;

    vec3 n = normalize(fs_in.normal);
    if (!gl_FrontFacing) {
        n = -n;
    }

    gColor = vec4(pow(albedo.rgb, vec3(1.0 / 1.3)), mix_weight);
    gNormal = n * 0.5 + 0.5;
    gGMF.r = 0.10;          // gloss
    gGMF.g = 0.0;           // metal
    gGMF.b = renderType;
    gGMF.a = 0.0;
    gPosition = fs_in.worldPosition;
    gSurfaceNormals = n * 0.5 + 0.5;
}
