﻿#version 450 core

#extension GL_ARB_shading_language_include : require

#define USE_PERVIEW_UBO
#include "common.h"

layout(location = 0) in vec2 vertexXZ;
layout(location = 1) in vec2 vertexXZ_morph;
layout(location = 2) in float vertexY;
layout(location = 3) in float vertexY_morph;
layout(location = 4) in vec2 vertexTexCoord;
layout(location = 5) in vec4 vertexNormal;
layout(location = 6) in vec4 vertexNormal_morph;
layout(location = 7) in vec4 vertexTangent;
layout(location = 8) in vec4 vertexTangent_morph;

uniform vec2 map_size;
uniform vec2 map_center;
uniform mat4 modelMatrix;
uniform mat3 normalMatrix;
uniform vec2 me_location;

out VS_OUT {
    vec4 Vertex;
    mat3 TBN;
    vec3 worldPosition;
    vec2 UV;
    vec2 Global_UV;
} vs_out;

void main(void)
{
    vs_out.UV = vertexTexCoord;

    // calculate tex coords for global_AM
    vec2 uv_g;
    vec2 scaled = vs_out.UV / map_size;
    vec2 m_s = vec2(1.0)/map_size;
    uv_g.x = ((( (me_location.x )-50.0)/100.0)+map_center.x) * m_s.x ;
    uv_g.y = ((( (me_location.y )-50.0)/100.0)-map_center.y) * m_s.y ;
    vs_out.Global_UV = scaled + uv_g;
    vs_out.Global_UV.xy = 1.0 - vs_out.Global_UV.xy;
    
    vec3 vertexPosition = vec3(vertexXZ.x, vertexY, vertexXZ.y);
    vs_out.Vertex = vec4(vertexPosition, 1.0) * 1.0;
    vs_out.Vertex.x *= -1.0;

    //-------------------------------------------------------
    // Calculate biNormal
    vec3 VT, VB, VN ;
    VN = normalize(vertexNormal.xyz);
    VT = normalize(vertexTangent.xyz);

    VT = VT - dot(VN, VT) * VN;
    VB = cross(VT, VN);
    //-------------------------------------------------------

    // vertex --> world pos
    vs_out.worldPosition = vec3(view * modelMatrix * vec4(vertexPosition, 1.0f));

    // Tangent, biNormal and Normal must be trasformed by the normal Matrix.
    vec3 worldNormal = normalMatrix * VN;
    vec3 worldTangent = normalMatrix * VT;
    vec3 worldbiNormal = normalMatrix * VB;

    // make perpendicular
    worldTangent = normalize(worldTangent - dot(worldNormal, worldTangent) * worldNormal);
    worldbiNormal = normalize(worldbiNormal - dot(worldNormal, worldbiNormal) * worldNormal);

    // Create the Tangent, BiNormal, Normal Matrix for transforming the normalMap.
    vs_out.TBN = mat3(worldTangent, worldbiNormal, normalize(worldNormal));

    // Calculate vertex position in clip coordinates
    gl_Position = viewProj * modelMatrix * vec4(vertexPosition, 1.0f);
}
