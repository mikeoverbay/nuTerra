#version 450 core

#extension GL_ARB_bindless_texture : require
#extension GL_ARB_shading_language_include : require

#define USE_PERVIEW_UBO
#include "common.h" //! #include "../common.h"

layout(location = 0) in vec3 vertexPosition;
layout(location = 2) in vec2 vertexTexCoord;
layout(location = 3) in uvec2 vertexTexHandle;
layout(location = 4) in mat4 instanceMatrix;   // occupies 4..7
layout(location = 8) in uint vertexFlags;      // bit 0 = bark (no alpha test)

out Block
{
    vec2 uv;
    flat uvec2 texHandle;
    flat uint flags;
} vs_out;

void main(void)
{
    vs_out.uv = vertexTexCoord;
    vs_out.texHandle = vertexTexHandle;
    vs_out.flags = vertexFlags;

    // World space; the geometry stage puts it into each cascade.
    gl_Position = instanceMatrix * vec4(vertexPosition, 1.0);
}
