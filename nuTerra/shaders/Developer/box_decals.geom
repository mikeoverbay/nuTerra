#version 450 core

#extension GL_ARB_shading_language_include : require

#define USE_PERVIEW_UBO
#define USE_DECALS_SSBO
#include "common.h" //! #include "../common.h"

layout (points) in;
layout (triangle_strip, max_vertices = 14) out;


const vec4 f[8] = vec4[8](
    // near
    vec4(-1, -1, -1, 1),
    vec4(1,  -1, -1, 1),
    vec4(1,   1, -1, 1),
    vec4(-1,  1, -1, 1),
    // far
    vec4(-1, -1,  1, 1),
    vec4(1,  -1,  1, 1),
    vec4(1,   1,  1, 1),
    vec4(-1,  1,  1, 1)
);


void main(void)
{
    const DecalGLInfo thisDecal = decals[gl_PrimitiveIDIn];

    const mat4 MVP = viewProj * thisDecal.matrix;

    vec4 v[8];
    for (int i = 0; i < 8; i++) {
        v[i] = MVP * f[i];
    }

    gl_Position = v[0];
    EmitVertex();

    gl_Position = v[1];
    EmitVertex();

    gl_Position = v[3];
    EmitVertex();

    gl_Position = v[2];
    EmitVertex();

    gl_Position = v[6];
    EmitVertex();

    gl_Position = v[1];
    EmitVertex();

    gl_Position = v[5];
    EmitVertex();

    gl_Position = v[0];
    EmitVertex();

    gl_Position = v[4];
    EmitVertex();

    gl_Position = v[3];
    EmitVertex();

    gl_Position = v[7];
    EmitVertex();

    gl_Position = v[6];
    EmitVertex();

    gl_Position = v[4];
    EmitVertex();

    gl_Position = v[5];
    EmitVertex();
}
