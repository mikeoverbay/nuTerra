#version 450 core

#extension GL_ARB_shader_draw_parameters : require
#extension GL_ARB_shading_language_include : require

#define USE_PERVIEW_UBO
#define USE_TERRAIN_CHUNK_INFO_SSBO
#include "common.h" //! #include "../common.h"

layout(location = 0) in vec3 vertexPosition;
layout(location = 1) in vec2 vertexTexCoord;

out vec2 uv;
out vec2 V;

void main(void)
{
    const TerrainChunkInfo info = terrain_chunk_info[gl_BaseInstanceARB];

    vec4 Vertex = info.modelMatrix * vec4(vertexPosition.x, vertexPosition.y+0.1, vertexPosition.z, 1.0);
    V = Vertex.xz;

    gl_Position = viewProj * Vertex;

    uv = vertexTexCoord.xy;
}
