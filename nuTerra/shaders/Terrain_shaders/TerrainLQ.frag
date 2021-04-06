#version 450 core

#extension GL_ARB_shading_language_include : require

#define USE_TERRAIN_CHUNK_INFO_SSBO
#include "common.h" //! #include "../common.h"

layout (location = 0) out vec4 gColor;
layout (location = 1) out vec3 gNormal;
layout (location = 2) out vec4 gGMF;
layout (location = 3) out vec3 gPosition;

layout(binding = 21) uniform sampler2D global_AM;
layout(binding = 22) uniform sampler2DArray textArrayC;
layout(binding = 23) uniform sampler2DArray textArrayN;
layout(binding = 24) uniform sampler2DArray textArrayG;

in VS_OUT {
    vec4 Vertex;
    mat3 TBN;
    vec3 worldPosition;
    vec2 UV;
    vec2 Global_UV;
    flat uint map_id;
} fs_in;


/*===========================================================*/
void main(void)
{
    const TerrainChunkInfo info = terrain_chunk_info[fs_in.map_id];
    if (info.lq == 0) {
        discard;
    }

    vec4 global = texture(global_AM, fs_in.Global_UV);
    // This is needed to light the global_AM.
    vec4 ArrayTextureC = texture(textArrayC, vec3(fs_in.UV, fs_in.map_id) );
    vec4 ArrayTextureN = texture(textArrayN, vec3(fs_in.UV, fs_in.map_id) );
    vec4 ArrayTextureG = texture(textArrayG, vec3(fs_in.UV, fs_in.map_id) );

    // The obvious
    gColor = ArrayTextureC;
    gNormal.xyz = normalize(fs_in.TBN * ArrayTextureN.xyz);
    gGMF = ArrayTextureG;

    gPosition = fs_in.worldPosition;
}
