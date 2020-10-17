//Terrian chunks Markers.. 
#version 450 core

#extension GL_ARB_shading_language_include : require
#include "common.h"

layout(location = 0) in vec2 vertexXZ;
layout(location = 1) in float vertexY;
layout(location = 2) in vec2 vertexTexCoord;

out vec2 uv;
out vec2 V;

layout (binding = PER_FRAME_DATA_BASE, std140) uniform PerView {
    mat4 view;
    mat4 projection;
    mat4 viewProj;
    mat4 invViewProj;
    vec3 cameraPos;
};

uniform mat4 model;

void main(void)
{
    vec4 Vertex = model * vec4(vertexXZ.x, vertexY, vertexXZ.y, 1.0);
    V = Vertex.xz;

    gl_Position = viewProj * Vertex;

    uv = vertexTexCoord.xy;
}
