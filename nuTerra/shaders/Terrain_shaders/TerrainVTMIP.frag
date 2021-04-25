#version 450 core

#extension GL_ARB_shading_language_include : require

#define USE_COMMON_PROPERTIES_UBO
#define USE_VT_FUNCTIONS
#include "common.h" //! #include "../common.h"

layout (location = 0) out vec3 gColor;

in VS_OUT {
    vec2 UV;
} fs_in;

uniform float MipBias;

void main(void)
{
    float mipCount = log2(props.PageTableSize);
    float mip = floor(MipLevel(fs_in.UV, props.VirtualTextureSize) - MipBias);
    mip = clamp(mip, 0, mipCount);
    vec2 offset = floor(fs_in.UV * props.PageTableSize);
    gColor = vec3(floor(vec3(offset / exp2(mip), 1.0 + mip)));
}
