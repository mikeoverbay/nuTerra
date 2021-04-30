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
layout(binding = 3) uniform sampler2DArray SpecularTextureAtlas;


in VS_OUT {
    mat3 TBN;
    vec3 worldPosition;
    vec2 Global_UV;
} fs_in;


void main(void)
{
    // CALC MIP LEVEL
    float miplevel = MipLevel(fs_in.Global_UV, props.VirtualTextureSize);
    miplevel = clamp(miplevel, 0, log2(props.PageTableSize) - 1);

    const float mip1 = floor(miplevel);
    const float mip2 = mip1 + 1;
    const float mipfract = miplevel - mip1; // FRACTAL PART OF MIPLEVEL

    // GET PAGES FOR TRILINEAR FILTERING
    // PAGE1 : MIP1
    // PAGE2 : MIP1 + 1
    const uvec2 page1 = SampleTable(PageTable, fs_in.Global_UV, mip1);
    const uvec2 page2 = SampleTable(PageTable, fs_in.Global_UV, mip2);

    // TRILINEAR FILTERING BETWEEN MIP1 AND MIP2
    const vec4 color_sample1 = SampleAtlas(ColorTextureAtlas, page1, fs_in.Global_UV);
    const vec4 color_sample2 = SampleAtlas(ColorTextureAtlas, page2, fs_in.Global_UV);
    gColor = mix(color_sample1, color_sample2, mipfract);

    // Q: DO WE NEED TRILINEAR FILTERING FOR NORMALS? I'M NOT SURE
    const vec3 out_n = SampleAtlas(NormalTextureAtlas, page1, fs_in.Global_UV).xyz;
    gNormal.xyz = normalize(fs_in.TBN * (out_n * 2.0 - 1.0)) * 0.5 + 0.5;

    // TRILINEAR FILTERING BETWEEN MIP1 AND MIP2
    const float specular_sample1 = SampleAtlas(SpecularTextureAtlas, page1, fs_in.Global_UV).r;
    const float specular_sample2 = SampleAtlas(SpecularTextureAtlas, page2, fs_in.Global_UV).r;
    gGMF = vec4(0.2, mix(specular_sample1, specular_sample2, mipfract), 128.0/255.0, 0.0);

    gPosition = fs_in.worldPosition;
}
