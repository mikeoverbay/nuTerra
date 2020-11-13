#version 450 core

#extension GL_ARB_shading_language_include : require

#define USE_PERVIEW_UBO
#include "common.h" //! #include "../common.h"

//Atempt to draw persective model in ortho mode
layout(location = 0) in vec3 vertexPosition;


uniform mat4 ModelMatrix;
uniform mat4 ORTHOPROJECTION;

void main(void)
{
    gl_Position =  viewProj * ModelMatrix * vec4(vertexPosition, 1.0);
}


