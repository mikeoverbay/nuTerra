#version 450 core

#extension GL_ARB_shading_language_include : require

#define USE_PERVIEW_UBO
#define USE_COMMON_PROPERTIES_UBO
#define USE_TERRAIN_CHUNK_INFO_SSBO
#define USE_VT_FUNCTIONS
#include "common.h" //! #include "../common.h"

layout (triangles, equal_spacing) in;

layout(binding = 0) uniform usampler2D PageTable;
layout(binding = 2) uniform sampler2DArray NormalTextureAtlas;

in TCS_OUT {
    vec3 n;
    vec3 t;
    vec3 b;
    vec2 uv;
    flat uint map_id;
} tes_in[];

out TES_OUT {
    vec3 n;
    vec3 t;
    vec3 b;
    flat uint map_id;
} tes_out;


void main(void)
{
    const TerrainChunkInfo chunk = chunks[tes_in[0].map_id];

    // forward
    tes_out.map_id = tes_in[0].map_id;

    const vec2 uv = gl_TessCoord.x * tes_in[0].uv +
                    gl_TessCoord.y * tes_in[1].uv +
                    gl_TessCoord.z * tes_in[2].uv;

    const vec2 Global_UV = chunk.g_uv_offset + (uv * props.map_size);

    const uvec2 page = SampleTable(PageTable, Global_UV, 0);
    const float height = SampleAtlas(NormalTextureAtlas, page, Global_UV).w;

    gl_Position = gl_TessCoord.x * gl_in[0].gl_Position +
                  gl_TessCoord.y * gl_in[1].gl_Position +
                  gl_TessCoord.z * gl_in[2].gl_Position;

    tes_out.n = gl_TessCoord.x * tes_in[0].n +
                gl_TessCoord.y * tes_in[1].n +
                gl_TessCoord.z * tes_in[2].n;

    gl_Position.xyz += height * tes_out.n;

    tes_out.t = gl_TessCoord.x * tes_in[0].t +
                gl_TessCoord.y * tes_in[1].t +
                gl_TessCoord.z * tes_in[2].t;

    tes_out.b = gl_TessCoord.x * tes_in[0].b +
                gl_TessCoord.y * tes_in[1].b +
                gl_TessCoord.z * tes_in[2].b;
}
