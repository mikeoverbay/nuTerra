﻿#version 450 core

#extension GL_ARB_bindless_texture : require
#extension GL_ARB_shader_draw_parameters : require
#extension GL_ARB_shading_language_include : require

#define USE_PERVIEW_UBO
#define USE_MODELINSTANCES_SSBO
#define USE_CANDIDATE_DRAWS_SSBO
#include "common.h" //! #include "../common.h"

layout(location = 0) in vec3 vertexPosition;
layout(location = 4) in vec2 vertexTexCoord1;

out VS_OUT
{
flat out uint model_id;
out vec2 uv;
} vs_out;

void main(void)
{
    const CandidateDraw thisDraw = draw[gl_BaseInstanceARB];
    const ModelInstance thisModel = models[thisDraw.model_id];

    vs_out.model_id = thisDraw.material_id;
    vs_out.uv = vertexTexCoord1;

    mat4 modelView = view * thisModel.matrix;

    // Calculate vertex position in clip coordinates
    gl_Position = projection * modelView * vec4(vertexPosition, 1.0f);

 
}