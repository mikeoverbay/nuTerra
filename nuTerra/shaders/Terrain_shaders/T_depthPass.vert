#version 450 core

#extension GL_ARB_shading_language_include : require

#define USE_PERVIEW_UBO
#include "common.h" //! #include "../common.h"

layout(location = 0) in vec2 vertexXZ;
layout(location = 1) in float vertexY;

uniform mat4 modelMatrix;

void main(void){

    vec3 vertexPosition = vec3(vertexXZ.x, vertexY, vertexXZ.y);
    gl_Position = viewProj * modelMatrix * vec4(vertexPosition, 1.0f);

}