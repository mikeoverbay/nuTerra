﻿#version 450 core

#extension GL_ARB_bindless_texture : require
#extension GL_ARB_shading_language_include : require

#define USE_MATERIALS_SSBO
#include "common.h" //! #include "../common.h"

in VS_OUT
{
    flat uint material_id;
    vec2 uv;
} fs_in;

void main(void)
{
    const MaterialProperties thisMaterial = material[fs_in.material_id];

    if (thisMaterial.alphaTestEnable
        && (thisMaterial.shader_type == 1 || thisMaterial.shader_type == 2 || thisMaterial.shader_type == 7)) {
        float alpha = texture(sampler2D(thisMaterial.maps[1]), fs_in.uv).r;
        if (alpha < thisMaterial.alphaReference) {
            discard;
        }
    }
}
