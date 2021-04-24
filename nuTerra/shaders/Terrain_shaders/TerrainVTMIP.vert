#version 450 core

#extension GL_ARB_shader_draw_parameters : require
#extension GL_ARB_shading_language_include : require

#define USE_PERVIEW_UBO
#define USE_COMMON_PROPERTIES_UBO
#define USE_TERRAIN_CHUNK_INFO_SSBO
#include "common.h" //! #include "../common.h"

layout(location = 0) in vec3 vertexPosition;
layout(location = 1) in vec2 vertexTexCoord;

out VS_OUT {
    vec2 UV;
} vs_out;

void main(void)
{
    const TerrainChunkInfo chunk = chunks[gl_BaseInstanceARB];

    vs_out.UV = chunk.g_uv_offset + (vertexTexCoord * props.map_size);

    // Calculate vertex position in clip coordinates
    gl_Position = viewProj * chunk.modelMatrix * vec4(vertexPosition, 1.0f);
}
