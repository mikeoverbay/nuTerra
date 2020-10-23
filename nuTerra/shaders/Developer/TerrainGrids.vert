#version 450 core

#extension GL_ARB_shading_language_include : require

#define USE_PERVIEW_UBO
#include "common.h"

layout(location = 0) in vec2 vertexXZ;
layout(location = 2) in float vertexY;
layout(location = 4) in vec2 vertexTexCoord;

out vec2 uv;
out vec2 V;

uniform mat4 model;

void main(void)
{
    vec4 Vertex = model * vec4(vertexXZ.x, vertexY, vertexXZ.y, 1.0);
    V = Vertex.xz;

    gl_Position = viewProj * Vertex;

    uv = vertexTexCoord.xy;
}
