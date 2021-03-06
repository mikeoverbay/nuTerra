﻿// gWriter vertex Shader. We will use this as a template for other shaders
#version 460 core

#extension GL_ARB_shader_draw_parameters : require
#extension GL_ARB_shading_language_include : require
#include "common.h"

layout(location = 0) in vec3 vertexPosition;
layout(location = 1) in vec4 vertexNormal;
layout(location = 2) in vec4 vertexTangent;
layout(location = 3) in vec4 vertexBinormal;
layout(location = 4) in vec2 vertexTexCoord1;
layout(location = 5) in vec2 vertexTexCoord2;

layout (binding = 0, std430) readonly buffer MODEL_MATRIX_BLOCK
{
    ModelInstance models[];
};

layout (binding = 1, std430) readonly buffer CandidateDraws
{
    CandidateDraw draw[];
};

out VS_OUT
{
    vec2 TC1;
    vec2 TC2;
    vec3 worldPosition;
    mat3 TBN;
    flat uint material_id;
} vs_out;

uniform mat4 projection;
uniform mat4 view;

void main(void)
{
    const CandidateDraw thisDraw = draw[gl_BaseInstanceARB];
    const ModelInstance thisModel = models[thisDraw.model_id];

    vs_out.material_id = thisDraw.material_id;
    vs_out.TC1 = vertexTexCoord1;
    vs_out.TC2 = vertexTexCoord2;

    mat4 modelView = view * thisModel.matrix;
    // TODO: mat3 normalMatrix = mat3(transpose(inverse(modelView)));
    mat3 normalMatrix = mat3(modelView);

    // Transform position & normal to world space
    vs_out.worldPosition = vec3(modelView * vec4(vertexPosition, 1.0f));
    vec3 t = normalize(normalMatrix * vertexTangent.xyz);
    vec3 b = normalize(normalMatrix * vertexBinormal.xyz);
    vec3 n = normalize(normalMatrix * vertexNormal.xyz);

    vs_out.TBN = mat3(t, b, n);

    // Calculate vertex position in clip coordinates
    gl_Position = projection * modelView * vec4(vertexPosition, 1.0f);
}
