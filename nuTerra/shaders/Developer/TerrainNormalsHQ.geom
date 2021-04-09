#version 450 core

#extension GL_ARB_shading_language_include : require

#define USE_PERVIEW_UBO
#define USE_TERRAIN_CHUNK_INFO_SSBO
#include "common.h" //! #include "../common.h"

layout (triangles) in;
layout (line_strip, max_vertices = 21) out;

uniform bool show_wireframe;
uniform int mode;
uniform float prj_length;

in TES_OUT
{
    vec3 n;
    vec3 t;
    vec3 b;
    flat uint map_id;
} gs_in[];

out GS_OUT {
    flat vec4 color;
} gs_out;

void main()
{
    const TerrainChunkInfo chunk = chunks[gs_in[0].map_id];
    mat4 mvp = viewProj * chunk.modelMatrix;

    int i;
    if (mode == 1) {
        vec4 sumV = (gl_in[0].gl_Position + gl_in[1].gl_Position + gl_in[2].gl_Position ) / 3.0f;
        vec3 sumN;

         // Normal
        gs_out.color = vec4(1.0f, 0.0f, 0.0f, 1.0f);
        sumN = (gs_in[0].n + gs_in[1].n + gs_in[2].n) / 3.0f;
        gl_Position = mvp * sumV;
        EmitVertex();
        gl_Position = mvp * (sumV + vec4(sumN * prj_length, 0.0f));
        EmitVertex();
        EndPrimitive();

        // Tangent
        gs_out.color = vec4(0.0f, 1.0f, 0.0f, 1.0f);
        sumN = (gs_in[0].t + gs_in[1].t + gs_in[2].t) / 3.0f;
        gl_Position = mvp * sumV;
        EmitVertex();
        gl_Position = mvp * (sumV + vec4(sumN * prj_length, 0.0f));
        EmitVertex();
        EndPrimitive();

        //biTangent
        gs_out.color = vec4(0.0f, 0.0f, 1.0f, 1.0f);
        sumN = (gs_in[0].b + gs_in[1].b + gs_in[2].b) / 3.0f;
        gl_Position = mvp * sumV;
        EmitVertex();
        gl_Position = mvp * (sumV + vec4(sumN * prj_length, 0.0f));
        EmitVertex();
        EndPrimitive();
    }
    else if (mode == 2) {
        // normal
        gs_out.color = vec4(1.0f, 0.0f, 0.0f, 1.0f);
        for(i = 0; i < gl_in.length(); i++) {
            gl_Position = mvp * gl_in[i].gl_Position;
            EmitVertex();
            gl_Position = mvp * (gl_in[i].gl_Position + vec4(gs_in[i].n * prj_length, 0.0f));
            EmitVertex();
            EndPrimitive();
        }
        // Tangent
        gs_out.color = vec4(0.0f, 1.0f, 0.0f, 1.0f);
        for(i = 0; i < gl_in.length(); i++) {
            gl_Position = mvp * gl_in[i].gl_Position;
            EmitVertex();
            gl_Position = mvp * (gl_in[i].gl_Position + vec4(gs_in[i].t * prj_length, 0.0f));
            EmitVertex();
            EndPrimitive();
        }
        // biTangent
        gs_out.color = vec4(0.0f, 0.0f, 1.0f, 1.0f);
        for(i = 0; i < gl_in.length(); i++) {
            gl_Position = mvp * gl_in[i].gl_Position;
            EmitVertex();
            gl_Position = mvp * (gl_in[i].gl_Position + vec4(gs_in[i].b * prj_length, 0.0f));
            EmitVertex();
            EndPrimitive();
        }
    }

    if (show_wireframe) {
        gs_out.color = vec4(1.0f, 1.0f, 0.0f, 1.0f);
        for (i = 0; i < gl_in.length(); i++) {
            gl_Position = mvp * gl_in[i].gl_Position;
            EmitVertex();
        }
        EndPrimitive();
    }
}
