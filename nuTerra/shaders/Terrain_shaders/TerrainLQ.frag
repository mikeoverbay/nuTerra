#version 450 core

#extension GL_ARB_shading_language_include : require

#define USE_COMMON_PROPERTIES_UBO
#define USE_MIPLEVEL_FUNCTION
#define USE_VT_FUNCTIONS
#include "common.h" //! #include "../common.h"

layout (location = 0) out vec4 gColor;
layout (location = 1) out vec3 gNormal;
layout (location = 2) out vec4 gGMF;
layout (location = 3) out vec3 gPosition;

layout(binding = 0) uniform usampler2D PageTable;
layout(binding = 1) uniform sampler2DArray ColorTextureAtlas;
layout(binding = 2) uniform sampler2DArray NormalTextureAtlas;

in VS_OUT {
    mat3 TBN;
    vec3 worldPosition;
    vec2 Global_UV;
} fs_in;


/*===========================================================*/
void main(void)
{
    float miplevel = MipLevel(fs_in.Global_UV, props.VirtualTextureSize);
    miplevel = clamp(miplevel, 0, log2(props.PageTableSize) - 1);

    const float mip1 = floor(miplevel);
    const float mip2 = mip1 + 1;
    const float mipfrac = miplevel - mip1;

    const uvec2 page1 = SampleTable(PageTable, fs_in.Global_UV, mip1);
    const uvec2 page2 = SampleTable(PageTable, fs_in.Global_UV, mip2);

    const vec4 color_sample1 = SampleAtlas(ColorTextureAtlas, page1, fs_in.Global_UV);
    const vec4 color_sample2 = SampleAtlas(ColorTextureAtlas, page2, fs_in.Global_UV);
    gColor = mix(color_sample1, color_sample2, mipfrac);
    
    const vec3 out_n = SampleAtlas(NormalTextureAtlas, page1, fs_in.Global_UV).xyz;
    gNormal.xyz = normalize(fs_in.TBN * out_n);

    const float specular = 0.0; // TODO
    gGMF = vec4(0.2, specular, 128.0/255.0, 0.0);

    gPosition = fs_in.worldPosition;
}
