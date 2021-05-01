#version 450 core

#extension GL_ARB_shader_draw_parameters : require
#extension GL_ARB_shading_language_include : require

#define USE_PERVIEW_UBO
#define USE_COMMON_PROPERTIES_UBO
#define USE_TERRAIN_CHUNK_INFO_SSBO
#include "common.h" //! #include "../common.h"

layout(location = 0) in vec3 vertexPosition;
layout(location = 1) in vec2 vertexTexCoord;
layout(location = 2) in vec4 vertexNormal;
layout(location = 3) in vec3 vertexTangent;

out VS_OUT {
    vec3 n;
    vec3 t;
    vec3 b;
    vec2 uv;
    flat uint map_id;
    float fLevel;
} vs_out;

void main(void)
{
    const TerrainChunkInfo chunk = chunks[gl_BaseInstanceARB];

    vs_out.map_id = gl_BaseInstanceARB;
    vs_out.uv = vertexTexCoord;

    vec3 VT = vertexTangent.xyz - dot(vertexNormal.xyz, vertexTangent.xyz) * vertexNormal.xyz;
    vec3 worldBiTangent = cross(VT, vertexNormal.xyz);

    //--------------------
    // NOTE: vertexNormal is already normalized in the VBO.
    vs_out.n = normalize(vertexNormal.xyz);
    vs_out.t = normalize(vertexTangent.xyz);
    vs_out.b = normalize(worldBiTangent.xyz);
    //Make angles perpendicular
    vs_out.t -= dot(vs_out.n, vs_out.t) * vs_out.n;
    vs_out.b -= dot(vs_out.n, vs_out.b) * vs_out.n;
    
    // Calculate vertex position in clip coordinates
    vec3 offsetVertex = vertexPosition.xyz + (vertexNormal.xyz * 0.005);
    gl_Position = vec4(offsetVertex, 1.0);

    const float ln = distance(vec3(chunk.modelMatrix * gl_Position), cameraPos.xyz);
    const float factor = max(min(8.0 - ln / 10.0, 8.0), 1.0);
    vs_out.fLevel = factor * props.tess_level;
}
