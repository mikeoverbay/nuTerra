#version 450 core

#extension GL_ARB_shading_language_include : require
#include "common.h"

uniform mat4 ModelMatrix;

layout (binding = PER_FRAME_DATA_BASE, std140) uniform PerView {
    mat4 view;
    mat4 projection;
    mat4 viewProj;
    mat4 invViewProj;
    vec3 cameraPos;
    vec2 resolution;
};

out mat4 inverseProject;

const vec3 CUBE[14] = vec3[14](
    vec3(0.5, 0.5, 0.5),
    vec3(-0.5, 0.5, 0.5),
    vec3(0.5, -0.5, 0.5),
    vec3(-0.5, -0.5, 0.5),
    vec3(-0.5, -0.5, -0.5),
    vec3(-0.5, 0.5, 0.5),
    vec3(-0.5, 0.5, -0.5),
    vec3(0.5, 0.5, 0.5),
    vec3(0.5, 0.5, -0.5),
    vec3(0.5, -0.5, 0.5),
    vec3(0.5, -0.5, -0.5),
    vec3(-0.5, -0.5, -0.5),
    vec3(0.5, 0.5, -0.5),
    vec3(-0.5, 0.5, -0.5)
);

void main(void)
{
    gl_Position =  viewProj * ModelMatrix * vec4(CUBE[gl_VertexID], 1.0);
    inverseProject = invViewProj;
}


