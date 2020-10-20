#version 450 core

#extension GL_ARB_shading_language_include : require

#define USE_PERVIEW_UBO
#include "common.h"

layout(location = 0) in vec3 vertexPosition;
layout(location = 2) in vec2 vertexTexCoord1;

out vec2 texCoord;

void main(void)
{
    texCoord =  vertexTexCoord1;

    mat4 posMat = mat4(
        vec4( 1.0, 0.0, 0.0, 0.0),
        vec4( 0.0, 1.0, 0.0, 0.0),
        vec4( 0.0, 0.0, 1.0, 0.0),
        vec4( cameraPos, 1.0));

    gl_Position = viewProj * posMat * vec4(vertexPosition, 1.0f);
}
