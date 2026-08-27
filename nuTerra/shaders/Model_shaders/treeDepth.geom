#version 450 core

#extension GL_ARB_shading_language_include : require

#define USE_PERVIEW_UBO
#include "common.h" //! #include "../common.h"

layout(triangles, invocations = 4) in;
layout(triangle_strip, max_vertices = 3) out;

in Block
{
    vec2 uv;
    flat uvec2 texHandle;
    flat uint flags;
} gs_in[];

out Block
{
    vec2 uv;
    flat uvec2 texHandle;
    flat uint flags;
} gs_out;

void main(void)
{
    for (int i = 0; i < 3; ++i)
    {
        gl_Position = lightSpaceMatrices[gl_InvocationID] * gl_in[i].gl_Position;

        gs_out.uv = gs_in[i].uv;
        gs_out.texHandle = gs_in[i].texHandle;
        gs_out.flags = gs_in[i].flags;

        gl_Layer = gl_InvocationID;
        EmitVertex();
    }
    EndPrimitive();
}
