// Normal shader .. shows the normal,tangent and biNormal vectors and wire overlay
#version 460 core

#extension GL_ARB_bindless_texture : require
#extension GL_ARB_shader_draw_parameters : require
#extension GL_ARB_shading_language_include : require
#include "common.h"

layout(location = 0) in vec3 vertexPosition;
layout(location = 1) in vec4 vertexNormal;
layout(location = 2) in vec4 vertexTangent;
layout(location = 3) in vec4 vertexBinormal;
layout(location = 4) in vec2 vertexTexCoord1;
layout(location = 5) in vec2 vertexTexCoord2;

layout (binding = MATRICES_BASE, std430) readonly buffer MODEL_MATRIX_BLOCK
{
    ModelInstance models[];
};

layout (binding = DRAW_CANDIDATES_BASE, std430) readonly buffer CandidateDraws
{
    CandidateDraw draw[];
};

uniform mat4 projection;
uniform mat4 view;

out VS_OUT
{
    vec3 n;
    vec3 t;
    vec3 b;
} vs_out;

void main(void)
{
    const CandidateDraw thisDraw = draw[gl_BaseInstanceARB];
    const ModelInstance thisModel = models[thisDraw.model_id];

    // Calculate vertex position in clip coordinates
    gl_Position = projection * view * thisModel.matrix * vec4(vertexPosition, 1.0);

    // Should be mat3(transpose(inverse(view * thisModel.matrix))), but it's very slow
    mat3 normalMatrix = mat3(view * thisModel.matrix);

    vs_out.n = normalize(vec3(projection * vec4(normalMatrix * vertexNormal.xyz, 0.0f)));
    vs_out.t = normalize(vec3(projection * vec4(normalMatrix * vertexTangent.xyz, 0.0f)));
    vs_out.b = normalize(vec3(projection * vec4(normalMatrix * vertexBinormal.xyz, 0.0f)));

    //Make angles perpendicular
    vs_out.t -= dot(vs_out.n, vs_out.t) * vs_out.n;
    vs_out.b -= dot(vs_out.n, vs_out.b) * vs_out.n;
}
