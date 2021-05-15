#version 450 core

#extension GL_ARB_shading_language_include : require

#define USE_PERVIEW_UBO
#define USE_TERRAIN_CHUNK_INFO_SSBO
#include "common.h" //! #include "../common.h"

layout(location = 0) in vec2 vertexPosition;

layout(location = 0) out VS_OUT {
    vec3 vertexPosition;
    vec3 vertexNormal;
    vec2 UV;
} vs_out;

void main(void)
{
    const TerrainChunkInfo chunk = outland[gl_InstanceID];

    vs_out.UV =  vertexPosition.xy +50.0;
    vec4 v;
    v.xy = vertexPosition.xy;
    v.xz *= 100.0;
    v.z = 0.0;// this will come from height map
    vs_out.vertexPosition = v.xyz;
    //th8is will come from normal texture
    //vs_out.vertexNormal = vertexNormal.xyz;
    gl_Position = viewProj * chunk.modelMatrix * v;

}
