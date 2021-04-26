﻿#version 450 core

#extension GL_ARB_shading_language_include : require

#define USE_COMMON_PROPERTIES_UBO
#define USE_VT_FUNCTIONS
#include "common.h" //! #include "../common.h"

layout (location = 0) out vec4 gColor;
layout (location = 1) out vec3 gNormal;
layout (location = 2) out vec4 gGMF;
layout (location = 3) out vec3 gPosition;

layout(binding = 0) uniform usampler2D PageTable;
layout(binding = 1) uniform sampler2DArray TextureAtlas;

in VS_OUT {
    vec3 worldPosition;
    vec2 Global_UV;
    vec2 UV;
} fs_in;


// This function samples the page table and returns the page's
// position and mip level.
uint SampleTable(vec2 uv, float mip)
{
    const vec2 offset = fract(uv * props.PageTableSize) / props.PageTableSize;
    return textureLod(PageTable, uv - offset, mip).r;
}

// This functions samples from the texture atlas and returns the final color
vec4 SampleAtlas(uint page, vec2 uv)
{
    const float mipsize = exp2(page & 31);
    uv = fract(uv * props.PageTableSize / mipsize);
    const uint layer = (page >> 5);
    return texture(TextureAtlas, vec3(uv, layer));
}


/*===========================================================*/
void main(void)
{
    float miplevel = MipLevel(fs_in.Global_UV, props.VirtualTextureSize);
    miplevel = clamp(miplevel, 0, log2(props.PageTableSize) - 1);

    const float mip1 = floor(miplevel);
    const float mip2 = mip1 + 1;
    const float mipfrac = miplevel - mip1;

    const uint page1 = SampleTable(fs_in.Global_UV, mip1);
    const uint page2 = SampleTable(fs_in.Global_UV, mip2);

    const vec4 sample1 = SampleAtlas(page1, fs_in.Global_UV);
    const vec4 sample2 = SampleAtlas(page2, fs_in.Global_UV);

    gColor = mix(sample1, sample2, mipfrac);
    gPosition = fs_in.worldPosition;
}
