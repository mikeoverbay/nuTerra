#version 450 core

#extension GL_ARB_shading_language_include : require

#define USE_PERVIEW_UBO
#include "common.h" //! #include "../common.h"

layout(triangles, invocations = 3) in;
layout(triangle_strip, max_vertices = 3) out;

in Block
{
    flat uint material_id;
    vec2 uv;
} gs_in[];

out Block
{
    flat uint material_id;
    vec2 uv;
} gs_out;

void main(void)
{
	for (int i = 0; i < 3; ++i)
	{
		gl_Position = lightSpaceMatrices[gl_InvocationID] * gl_in[i].gl_Position;

		gs_out.material_id = gs_in[i].material_id;
		gs_out.uv = gs_in[i].uv;

		gl_Layer = gl_InvocationID;
		EmitVertex();
	}
	EndPrimitive();
}
