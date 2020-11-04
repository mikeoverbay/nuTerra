#version 450 core

#extension GL_ARB_shader_draw_parameters : require
#extension GL_ARB_shading_language_include : require

#define USE_CANDIDATE_DRAWS_SSBO
#include "common.h" //! #include "../common.h"

layout(location = 0) in vec3 vertexPosition;
layout(location = 1) in vec4 vertexNormal;
layout(location = 2) in vec4 vertexTangent;
layout(location = 3) in vec4 vertexBinormal;
layout(location = 4) in vec2 vertexTexCoord1;
layout(location = 5) in vec2 vertexTexCoord2;

uniform mat4 viewProjMat;

out VS_OUT {
    vec3 N;
    vec2 UV;
    vec3 FragPos;
    flat uint material_id;
} vs_out;

void main(void)
{
    const CandidateDraw thisDraw = draw[gl_BaseInstanceARB];
    vs_out.material_id = thisDraw.material_id;

    gl_Position = viewProjMat * vec4(vertexPosition, 1.0);
    vs_out.N = normalize(vertexNormal.xyz);
    vs_out.UV = vertexTexCoord1;
    vs_out.FragPos = vertexPosition;
}
