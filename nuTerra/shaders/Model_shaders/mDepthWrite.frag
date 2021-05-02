#version 450 core

#extension GL_ARB_bindless_texture : require
#extension GL_ARB_shading_language_include : require

#define USE_MATERIALS_SSBO
#include "common.h" //! #include "../common.h"

out vec4 co;

in VS_OUT
{
    flat uint material_id;
    vec2 uv;
} fs_in;

void main(void)
{
    const MaterialProperties thisMaterial = material[fs_in.material_id];

    if (thisMaterial.alphaTestEnable) {
        float alpha = texture(sampler2D(thisMaterial.maps[1]), fs_in.uv).r;
        if (alpha < thisMaterial.alphaReference) {
            discard;
        }
    }
    co = vec4(0.0);
}
