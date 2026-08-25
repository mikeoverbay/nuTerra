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
// Unit 1 - the albedo atlas, for its alpha only. The eval stage does not
// shade, but the wetness mask lives in that alpha and displacement has to
// know about it. Same unit VirtualTexture.Bind() already sets up.
layout(binding = 1) uniform sampler2DArray ColorTextureAtlas;
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
    vec3 worldNormal;
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
    float height = SampleAtlas(NormalTextureAtlas, page, tes_out.Global_UV).w;

    // Water levels what it sits in. The page's albedo alpha is the wetness
    // mask (t_mixer), and the game scales displacement by 0.8 - wetness, so a
    // puddle fills the relief rather than rippling over it. Costs one extra
    // fetch in the eval stage; without it, tessellated ground stays bumpy
    // under standing water, which is the tell that it is painted on.
    {
        const float wet = SampleAtlas(ColorTextureAtlas, page, tes_out.Global_UV).a;
        height *= max(0.8 - wet, 0.0);
    }

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
    tes_out.worldNormal = worldNormal;

    // make perpendicular
    worldTangent = normalize(worldTangent - dot(worldNormal, worldTangent) * worldNormal);
    worldbiNormal = normalize(worldbiNormal - dot(worldNormal, worldbiNormal) * worldNormal);

    // Create the Tangent, BiNormal, Normal Matrix for transforming the normalMap.
    tes_out.TBN = mat3(worldTangent, worldbiNormal, normalize(worldNormal));
    
    // The game's displacement envelope: at most g_tessDisplaceDist = 1 m,
    // faded to nothing approaching the 60 m tessellation range. The fade is
    // what makes the HQ-to-LQ handover at 60 m invisible - by the time a
    // chunk swaps to the flat path its displacement is already zero, so
    // there is nothing to pop.
    {
        float d = length(vec3(view * pos));
        float fade = 1.0 - smoothstep(40.0, 60.0, d);
        pos.xyz += clamp(height, -1.0, 1.0) * fade * VN;
    }

    tes_out.worldPosition = vec3(view * pos);

    gl_Position = viewProj * pos;
}
