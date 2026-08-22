#version 450 core

#extension GL_ARB_bindless_texture : require
#extension GL_ARB_shading_language_include : require

#define USE_COMMON_PROPERTIES_UBO
#include "common.h" //! #include "../common.h"

// Same three targets t_mixer writes, in the same conventions.
layout (location = 0) out vec4 gColor;
layout (location = 1) out vec4 gNormal;
layout (location = 2) out float gSpecular;

in VS_OUT {
    vec2 UV;
    vec4 colour;
    flat uvec2 texHandle;
    flat uvec2 nrmHandle;
} fs_in;

uniform int page_mip;

void main(void)
{
    vec4 albedo = texture(sampler2D(fs_in.texHandle), fs_in.UV);

    // Two gates: the atlas is cut out around the road, and the colour stream's
    // alpha is the per-vertex mix weight that feathers the patch into the
    // terrain. Their product is what the blender uses.
    //
    // The cut-out has to be sharpened as the page gets coarser. A mip 9 page
    // covers hundreds of metres through the same 256 texels, so the road atlas
    // is sampled from deep in its own mip chain, where its alpha has averaged
    // toward the mean - the road quietly fades out with distance. Pushing the
    // alpha back toward 0 or 1 restores the coverage the averaging removed.
    float sharpen = 1.0 + float(page_mip) * 0.75;
    float cutout = clamp((albedo.a - 0.5) * sharpen + 0.5, 0.0, 1.0);

    float mix_weight = cutout * fs_in.colour.a;
    if (mix_weight < 0.004) {
        discard;
    }

    gColor = vec4(albedo.rgb, mix_weight);

    // The page stores a tangent space normal - TerrainLQ applies the TBN
    // later - and the road lies flat on the terrain, so its own tangent space
    // lines up and the map can go in as it is. Same AG packed convention the
    // terrain layers use, so it is unpacked the same way.
    vec3 n = vec3(0.0, 0.0, 1.0);
    if (fs_in.nrmHandle != uvec2(0)) {
        vec4 nm = texture(sampler2D(fs_in.nrmHandle), fs_in.UV);
        n.xy = clamp(nm.ag * 2.0 - 1.0, -1.0, 1.0);
        n.z = clamp(sqrt(max(1.0 - dot(n.xy, n.xy), 0.0)), -1.0, 1.0);
        n = normalize(n);
        n.x *= -1.0;
    }

    // w carries t_mixer's splat height, which the road must not disturb, so
    // the alpha written here is only the blend factor - the blend func keeps
    // the destination alpha.
    gNormal = vec4(n * 0.5 + 0.5, mix_weight);

    gSpecular = 0.1;
}
