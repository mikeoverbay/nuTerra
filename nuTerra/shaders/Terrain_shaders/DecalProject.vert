//Decals color pass.
#version 450 core

#extension GL_ARB_shading_language_include : require
#include "common.h"

layout(location = 0) in vec3 vertexPosition;

uniform mat4 DecalMatrix;

layout (binding = PER_FRAME_DATA_BASE, std140) uniform PerView {
    mat4 view;
    mat4 projection;
    mat4 viewProj;
    mat4 invViewProj;
    vec3 cameraPos;
    vec2 resolution;
};

out VS_OUT {
    flat mat4 invMVP;
} vs_out;

void main(void)
{
    gl_Position = viewProj * DecalMatrix * vec4(vertexPosition, 1.0);
    vs_out.invMVP = inverse(viewProj * DecalMatrix);
}
