#version 450 core

#extension GL_ARB_shading_language_include : require

#define USE_PERVIEW_UBO
#include "common.h" //! #include "../common.h"

layout(location = 0) in vec3 vertexPosition;
layout(location = 2) in vec2 vertexTexCoord1;

out vec2 texCoord;

void main(void)
{
    texCoord = vertexTexCoord1;
    gl_Position = viewProj * vec4(cameraPos + vertexPosition, 1.0f);
}
