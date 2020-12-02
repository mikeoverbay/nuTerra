#version 450 core

#extension GL_ARB_bindless_texture : require
#extension GL_ARB_shader_draw_parameters : require
#extension GL_ARB_shading_language_include : require

#define USE_PERVIEW_UBO
#define USE_MODELINSTANCES_SSBO
#define USE_CANDIDATE_DRAWS_SSBO
#include "common.h" //! #include "../common.h"

layout(location = 0) in vec3 vertexPosition;
layout(location = 1) in vec4 vertexNormal;
layout(location = 2) in vec4 vertexTangent;
layout(location = 3) in vec4 vertexBinormal;
layout(location = 4) in vec2 vertexTexCoord1;
layout(location = 5) in vec2 vertexTexCoord2;

out VS_OUT
{
    vec3 n;
    vec3 t;
    vec3 b;
    mat4 matrix;
} vs_out;

void main(void)
{
    const CandidateDraw thisDraw = draw[gl_BaseInstanceARB];
    const ModelInstance thisModel = models[thisDraw.model_id];

    // Calculate vertex position in clip coordinates

    vec3 offsetVertex;
    offsetVertex = vertexPosition.xyz + (vertexNormal.xyz * 0.005);
    gl_Position = vec4(offsetVertex, 1.0);
    vs_out.matrix = viewProj * thisModel.matrix;
    // Should be mat3(transpose(inverse(view * thisModel.matrix))), but it's very slow
    vs_out.n = normalize(vertexNormal.xyz);
    vs_out.t = normalize(vertexTangent.xyz);
    vs_out.b = normalize(vertexBinormal.xyz);

    //Make angles perpendicular
    vs_out.t -= dot(vs_out.n, vs_out.t) * vs_out.n;
    vs_out.b -= dot(vs_out.n, vs_out.b) * vs_out.n;
}
