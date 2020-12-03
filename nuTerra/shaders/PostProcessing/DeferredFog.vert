#version 450 core

#extension GL_ARB_shading_language_include : require

#define USE_PERVIEW_UBO
#include "common.h" //! #include "../common.h"

layout(location = 0) in vec3 vertexPosition;

uniform mat4 DecalMatrix;

out VS_OUT {
    flat mat4 invMVP;
    flat mat4 invDecal;
} vs_out;

void main(void)
{
    gl_Position = viewProj * DecalMatrix * vec4(vertexPosition, 1.0);
    vs_out.invMVP = inverse(viewProj * DecalMatrix);
    vs_out.invDecal = inverse(view * DecalMatrix);
}
