#version 450 core

#extension GL_ARB_bindless_texture : require
#extension GL_ARB_shading_language_include : require

#include "common.h" //! #include "../common.h"

// Same vertex layout as treeDepth - one shared geometry buffer per species,
// every placement an instance carrying its own world transform.
layout(location = 0) in vec3 vertexPosition;
layout(location = 2) in vec2 vertexTexCoord;
layout(location = 3) in uvec2 vertexTexHandle;
layout(location = 4) in mat4 instanceMatrix;   // occupies 4..7
layout(location = 8) in uint vertexFlags;      // bit 0 = bark (no alpha test)

uniform mat4 sunViewProj;

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

    // Straight to the sun's clip space. treeDepth stops at world space because
    // a geometry stage fans it out into the four cascades; there is only one
    // projection here, so that indirection is not needed.
    gl_Position = sunViewProj * instanceMatrix * vec4(vertexPosition, 1.0);
}
