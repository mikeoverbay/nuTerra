//Decals color pass.
#version 450 core

#extension GL_ARB_shading_language_include : require
#include "common.h"

uniform mat4 DecalMatrix;

layout (binding = PER_FRAME_DATA_BASE, std140) uniform PerView {
    mat4 view;
    mat4 projection;
    mat4 viewProj;
    mat4 invViewProj;
    vec3 cameraPos;
    vec2 resolution;
};

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

out VS_OUT {
    flat mat4 invMVP;
} vs_out;

void main(void)
{
    gl_Position = viewProj * DecalMatrix * vec4(CUBE[gl_VertexID], 1.0);
    vs_out.invMVP = inverse(viewProj * DecalMatrix);
}
