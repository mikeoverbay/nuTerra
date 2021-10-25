#version 450 core

#extension GL_ARB_shading_language_include : require

#define USE_PERVIEW_UBO
#include "common.h" //! #include "../common.h"

layout (points, invocations = 4) in;
layout (line_strip, max_vertices = 24) out;

const vec4 f[8] = vec4[8](
    // near
    vec4(-1, -1, 0, 1),
    vec4(1,  -1, 0, 1),
    vec4(1,   1, 0, 1),
    vec4(-1,  1, 0, 1),
    // far
    vec4(-1, -1,  1, 1),
    vec4(1,  -1,  1, 1),
    vec4(1,   1,  1, 1),
    vec4(-1,  1,  1, 1)
);

void main(void)
{
    mat4 inv = inverse(lightSpaceMatrices[gl_InvocationID]);

    vec4 v[8];
    for (int i = 0; i < 8; i++) {
        vec4 ff = inv * f[i];
        v[i].xyz = ff.xyz / ff.w;
        v[i].w = 1.0f;
        v[i] = viewProj * v[i];
    }

    gl_Position = v[0];
    EmitVertex();

    gl_Position = v[1];
    EmitVertex();

    EndPrimitive(); // 1

    gl_Position = v[1];
    EmitVertex();

    gl_Position = v[2];
    EmitVertex();

    EndPrimitive(); // 2

    gl_Position = v[2];
    EmitVertex();

    gl_Position = v[3];
    EmitVertex();

    EndPrimitive(); // 3

    gl_Position = v[3];
    EmitVertex();

    gl_Position = v[0];
    EmitVertex();

    EndPrimitive(); // 4

    gl_Position = v[4];
    EmitVertex();

    gl_Position = v[5];
    EmitVertex();

    EndPrimitive(); // 5

    gl_Position = v[5];
    EmitVertex();

    gl_Position = v[6];
    EmitVertex();

    EndPrimitive(); // 6

    gl_Position = v[6];
    EmitVertex();

    gl_Position = v[7];
    EmitVertex();

    EndPrimitive(); // 7

    gl_Position = v[7];
    EmitVertex();

    gl_Position = v[4];
    EmitVertex();

    EndPrimitive(); // 8

    gl_Position = v[0];
    EmitVertex();

    gl_Position = v[4];
    EmitVertex();

    EndPrimitive(); // 9

    gl_Position = v[1];
    EmitVertex();

    gl_Position = v[5];
    EmitVertex();

    EndPrimitive(); // 10

    gl_Position = v[2];
    EmitVertex();

    gl_Position = v[6];
    EmitVertex();

    EndPrimitive(); // 11

    gl_Position = v[3];
    EmitVertex();

    gl_Position = v[7];
    EmitVertex();

    EndPrimitive(); // 12
}
