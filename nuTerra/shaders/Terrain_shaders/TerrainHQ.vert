#version 450 core

#ifdef GL_SPIRV
#extension GL_GOOGLE_include_directive : require
#else
#extension GL_ARB_shading_language_include : require
#endif

#extension GL_ARB_shader_draw_parameters : require

#define USE_PERVIEW_UBO
#define USE_TERRAIN_CHUNK_INFO_SSBO
#define USE_COMMON_PROPERTIES_UBO
#include "common.h" //! #include "../common.h"

layout(location = 0) in vec3 vertexPosition;
layout(location = 1) in vec2 vertexTexCoord;
layout(location = 2) in vec4 vertexNormal;
layout(location = 3) in vec3 vertexTangent;

layout(location = 0) out VS_OUT {
    vec3 vertexPosition;
    vec3 vertexNormal;
    vec3 vertexTangent;
    vec2 UV;
    flat int map_id;
    float fLevel;
} vs_out;

void main(void)
{
    const TerrainChunkInfo chunk = chunks[gl_BaseInstanceARB];

    vs_out.map_id = gl_BaseInstanceARB;
    vs_out.UV =  vertexTexCoord;
    vs_out.vertexPosition = vertexPosition;
    vs_out.vertexNormal = vertexNormal.xyz;
    vs_out.vertexTangent = vertexTangent.xyz;

    gl_Position = chunk.modelMatrix * vec4(vertexPosition, 1.0);

    const float ln = distance(gl_Position.xyz, cameraPos.xyz);
    const float factor = max(min(8.0 - ln / 10.0, 8.0), 1.0);
    vs_out.fLevel = factor * props.tess_level;
}
