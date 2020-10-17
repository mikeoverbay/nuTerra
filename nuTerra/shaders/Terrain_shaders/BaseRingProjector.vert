#version 450 core

#extension GL_ARB_shading_language_include : require
#include "common.h"

layout(location = 0) in vec3 vertexPosition;

uniform mat4 ModelMatrix;

layout (binding = PER_FRAME_DATA_BASE, std140) uniform PER_FRAME_DATA {
    mat4 view;
    mat4 projection;
};

out mat4 inverseProject;
out vec4 positionSS;


void main(void)
{
    gl_Position =  projection * view * ModelMatrix * vec4(vertexPosition.xyz, 1.0);

    positionSS = gl_Position;

    inverseProject = inverse(projection * view);
}


