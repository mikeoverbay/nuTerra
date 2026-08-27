#version 450 core

#extension GL_ARB_bindless_texture : require
#extension GL_ARB_shading_language_include : require

#include "common.h" //! #include "../common.h"

in Block
{
    vec2 uv;
    flat uvec2 texHandle;
    flat uint flags;
} fs_in;

// Moments for the MSM path. Discarded by the pipeline when the bake FBO has no
// colour attachment - see sun_depth_terrain.frag.
layout(location = 0) out vec4 moments;

void main(void)
{
    // Without this a leaf card casts the shadow of a rectangle. The atlas is
    // mostly empty space, so the cutout is what makes the shadow leaf shaped.
    // Bark (flag bit 0) is exempt - trunks are opaque and some species' bark
    // alpha is a spec mask, not coverage.
    if ((fs_in.flags & 1u) == 0u &&
        texture(sampler2D(fs_in.texHandle), fs_in.uv).a < 0.5) {
        discard;
    }

    float z  = gl_FragCoord.z;
    float z2 = z * z;
    moments = vec4(z, z2, z2 * z, z2 * z2);
}
