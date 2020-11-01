#version 450 core

#extension GL_ARB_shading_language_include : require

#define USE_PERVIEW_UBO
#include "common.h" //! #include "../common.h"

layout(location = 0) in vec3 vertexPosition;

uniform mat4 ModelMatrix;

void main(void)
{
    gl_Position =  viewProj * ModelMatrix * vec4(vertexPosition, 1.0);
}


