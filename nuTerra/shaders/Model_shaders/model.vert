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
    vec2 TC1;
    vec2 TC2;
    vec3 worldPosition;
    mat3 TBN;
    flat uint material_id;
    flat vec3 surfaceNormal;
#ifdef PICK_MODELS
    flat uint model_id;
#endif
#ifdef SHOW_LOD_COLORS
    flat uint lod_level;
#endif
} vs_out;

void main(void)
{
    const CandidateDraw thisDraw = draw[gl_BaseInstanceARB];
    const ModelInstance thisModel = models[thisDraw.model_id];
    const mat4 mvp = thisModel.cached_mvp;

    vs_out.material_id = thisDraw.material_id;

#ifdef PICK_MODELS
    vs_out.model_id = thisDraw.model_id;
#endif
#ifdef SHOW_LOD_COLORS
    vs_out.lod_level = thisDraw.lod_level;
#endif

    vs_out.TC1 = vertexTexCoord1;
    vs_out.TC2 = vertexTexCoord2;

    mat4 modelView = view * thisModel.matrix;
    mat3 normalMatrix = mat3(transpose(inverse(modelView)));

    // Transform position & normal to world space
    vs_out.worldPosition = vec3(modelView * vec4(vertexPosition, 1.0f));

    vec3 t = normalize(normalMatrix * vertexTangent.xyz);
    vec3 b = normalize(normalMatrix * vertexBinormal.xyz);
    vec3 n = normalize(normalMatrix * vertexNormal.xyz);
    vs_out.surfaceNormal = n;
    vs_out.TBN = mat3(t, b, n);

    // Calculate vertex position in clip coordinates
    gl_Position = mvp * vec4(vertexPosition, 1.0f);
}
