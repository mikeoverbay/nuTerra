#version 450 core

#extension GL_ARB_shader_draw_parameters : require
#extension GL_ARB_shading_language_include : require

#define USE_TERRAIN_CHUNK_INFO_SSBO
#include "common.h" //! #include "../common.h"

layout(location = 0) in vec3 vertexPosition;
layout(location = 1) in vec2 vertexTexCoord;
layout(location = 2) in vec4 vertexNormal;
layout(location = 3) in vec3 vertexTangent;

out VS_OUT {
    vec3 vertexPosition;
    vec3 vertexNormal;
    vec3 vertexTangent;
    vec2 UV;
    flat uint map_id;
} vs_out;

const TerrainChunkInfo chunk = chunks[gl_BaseInstanceARB];

void main(void)
{
    vs_out.map_id = gl_BaseInstanceARB;
    vs_out.UV =  vertexTexCoord;
    vs_out.vertexPosition = vertexPosition;
    vs_out.vertexNormal = vertexNormal.xyz;
    vs_out.vertexTangent = vertexTangent.xyz;

    gl_Position = chunk.modelMatrix * vec4(vertexPosition, 1.0);
}
