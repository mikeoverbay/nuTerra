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

uniform mat3 normalMatrix;

out VS_OUT {
    mat3 TBN;
    vec3 worldPosition;
    vec2 Global_UV;
    vec3 worldNormal;
} vs_out;


void main(void)
{
    const TerrainChunkInfo chunk = chunks[gl_BaseInstanceARB];

    // calculate tex coords for global_AM
    vs_out.Global_UV = chunk.g_uv_offset + (vertexTexCoord * props.map_size);

    //-------------------------------------------------------
    // Calculate biNormal
    vec3 VT, VB, VN ;
    VN = normalize(vertexNormal.xyz);
    VT = normalize(vertexTangent.xyz);

    VT = VT - dot(VN, VT) * VN;
    VB = cross(VT, VN);
    //-------------------------------------------------------

    // Tangent, biNormal and Normal must be trasformed by the normal Matrix.
    vec3 worldNormal = normalMatrix * VN;
    vec3 worldTangent = normalMatrix * VT;
    vec3 worldbiNormal = normalMatrix * VB;
    vs_out.worldNormal = worldNormal;
    // make perpendicular
    worldTangent = normalize(worldTangent - dot(worldNormal, worldTangent) * worldNormal);
    worldbiNormal = normalize(worldbiNormal - dot(worldNormal, worldbiNormal) * worldNormal);

    // Create the Tangent, BiNormal, Normal Matrix for transforming the normalMap.
    vs_out.TBN = mat3( worldTangent, worldbiNormal, normalize(worldNormal));

    // vertex --> world pos
    vs_out.worldPosition = vec3(view * chunk.modelMatrix * vec4(vertexPosition, 1.0f));

    // Calculate vertex position in clip coordinates
    gl_Position = viewProj * chunk.modelMatrix * vec4(vertexPosition, 1.0f);
}
