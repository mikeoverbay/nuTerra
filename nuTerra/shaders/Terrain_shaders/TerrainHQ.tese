#version 450 core

#ifdef GL_SPIRV
#extension GL_GOOGLE_include_directive : require
#else
#extension GL_ARB_shading_language_include : require
#endif

#define USE_PERVIEW_UBO
#define USE_COMMON_PROPERTIES_UBO
#define USE_TERRAIN_CHUNK_INFO_SSBO
#define USE_VT_FUNCTIONS
#include "common.h" //! #include "../common.h"

layout (triangles, equal_spacing) in;

layout(binding = 0) uniform usampler2D PageTable;
layout(binding = 2) uniform sampler2DArray NormalTextureAtlas;

layout(location = 5) uniform mat3 normalMatrix;

layout(location = 0) in TCS_OUT {
    vec3 vertexNormal;
    vec3 vertexTangent;
    vec2 UV;
    flat int map_id;
} tes_in[];

layout(location = 0) out TES_OUT {
    mat3 TBN;
    vec3 worldPosition;
    vec2 Global_UV;
} tes_out;


void main(void)
{
    const TerrainChunkInfo chunk = chunks[tes_in[0].map_id];

    vec4 pos = gl_TessCoord.x * gl_in[0].gl_Position +
               gl_TessCoord.y * gl_in[1].gl_Position +
               gl_TessCoord.z * gl_in[2].gl_Position;

    const vec2 uv = gl_TessCoord.x * tes_in[0].UV +
                    gl_TessCoord.y * tes_in[1].UV +
                    gl_TessCoord.z * tes_in[2].UV;
                    
    tes_out.Global_UV = chunk.g_uv_offset + (uv * props.map_size);

    const uvec2 page = SampleTable(PageTable, tes_out.Global_UV, 0);
    const float height = SampleAtlas(NormalTextureAtlas, page, tes_out.Global_UV).w;

    //-------------------------------------------------------
    // Calculate biNormal
    vec3 VT, VB, VN ;
    VN = normalize(gl_TessCoord.x * tes_in[0].vertexNormal +
                   gl_TessCoord.y * tes_in[1].vertexNormal +
                   gl_TessCoord.z * tes_in[2].vertexNormal);

    VT = normalize(gl_TessCoord.x * tes_in[0].vertexTangent +
                   gl_TessCoord.y * tes_in[1].vertexTangent +
                   gl_TessCoord.z * tes_in[2].vertexTangent);

    VT = VT - dot(VN, VT) * VN;
    VB = cross(VT, VN);
    //-------------------------------------------------------

    // Tangent, biNormal and Normal must be trasformed by the normal Matrix.
    vec3 worldNormal = normalMatrix * VN;
    vec3 worldTangent = normalMatrix * VT;
    vec3 worldbiNormal = normalMatrix * VB;

    // make perpendicular
    worldTangent = normalize(worldTangent - dot(worldNormal, worldTangent) * worldNormal);
    worldbiNormal = normalize(worldbiNormal - dot(worldNormal, worldbiNormal) * worldNormal);

    // Create the Tangent, BiNormal, Normal Matrix for transforming the normalMap.
    tes_out.TBN = mat3(worldTangent, worldbiNormal, normalize(worldNormal));
    
    pos.xyz += height * VN;

    tes_out.worldPosition = vec3(view * pos);

    gl_Position = viewProj * pos;
}
