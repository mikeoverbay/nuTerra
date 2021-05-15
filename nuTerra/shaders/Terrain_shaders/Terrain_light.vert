#version 450 core

#extension GL_ARB_shader_draw_parameters : require
#extension GL_ARB_shading_language_include : require

#define USE_PERVIEW_UBO
#define USE_TERRAIN_CHUNK_INFO_SSBO
#include "common.h" //! #include "../common.h"

layout(location = 0) in vec3 vertexPosition;

void main(void)
{
    const TerrainChunkInfo chunk = chunks[gl_BaseInstanceARB];
    gl_Position = light_vp_matrix * chunk.modelMatrix * vec4(vertexPosition, 1.0f);
}
