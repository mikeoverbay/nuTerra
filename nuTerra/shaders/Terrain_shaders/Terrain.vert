﻿#version 450 core

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

layout (std140, binding = TERRAIN_LAYERS_UBO_BASE) uniform Layers {
    vec4 U1;
    vec4 U2;
    vec4 U3;
    vec4 U4;

    vec4 U5;
    vec4 U6;
    vec4 U7;
    vec4 U8;

    vec4 V1;
    vec4 V2;
    vec4 V3;
    vec4 V4;

    vec4 V5;
    vec4 V6;
    vec4 V7;
    vec4 V8;

    vec4 r1_1;
    vec4 r1_2;
    vec4 r1_3;
    vec4 r1_4;
    vec4 r1_5;
    vec4 r1_6;
    vec4 r1_7;
    vec4 r1_8;

    vec4 r2_1;
    vec4 r2_2;
    vec4 r2_3;
    vec4 r2_4;
    vec4 r2_5;
    vec4 r2_6;
    vec4 r2_7;
    vec4 r2_8;

    vec4 s1;
    vec4 s2;
    vec4 s3;
    vec4 s4;
    vec4 s5;
    vec4 s6;
    vec4 s7;
    vec4 s8;
};

uniform mat3 normalMatrix;

out VS_OUT {
    mat3 TBN;
    vec4 Vertex;
    vec3 worldPosition;
    vec2 UV;
    vec2 Global_UV;
    float ln;
    flat float map_id;
} vs_out;

//-------------------------------------------------------
//-------------------------------------------------------

const TerrainChunkInfo chunk = chunks[gl_BaseInstanceARB];

void main(void)
{
    vs_out.map_id = gl_BaseInstanceARB;

    vs_out.UV =  vertexTexCoord;
    // calculate tex coords for global_AM
    vec2 uv_g;
    vec2 scaled = vs_out.UV / props.map_size;
    vec2 m_s = vec2(1.0)/props.map_size;
    uv_g.x = ((( (chunk.me_location.x )-50.0)/100.0)+props.map_center.x) * m_s.x ;
    uv_g.y = ((( (chunk.me_location.y )-50.0)/100.0)-props.map_center.y) * m_s.y ;
    vs_out.Global_UV = scaled + uv_g;
    vs_out.Global_UV.xy = 1.0 - vs_out.Global_UV.xy;
    
    //-------------------------------------------------------
    // Calulate UVs for the texture layers
    vs_out.Vertex = vec4(vertexPosition, 1.0);

    //-------------------------------------------------------
    // Calculate biNormal
    vec3 VT, VB, VN ;
    VN = normalize(vertexNormal.xyz);
    VT = normalize(vertexTangent.xyz);

    VT = VT - dot(VN, VT) * VN;
    VB = cross(VT, VN);
    //-------------------------------------------------------

    // vertex --> world pos
    vs_out.worldPosition = vec3(view * chunk.modelMatrix * vec4(vertexPosition, 1.0f));

    // Tangent, biNormal and Normal must be trasformed by the normal Matrix.
    vec3 worldNormal = normalMatrix * VN;
    vec3 worldTangent = normalMatrix * VT;
    vec3 worldbiNormal = normalMatrix * VB;

    // make perpendicular
    worldTangent = normalize(worldTangent - dot(worldNormal, worldTangent) * worldNormal);
    worldbiNormal = normalize(worldbiNormal - dot(worldNormal, worldbiNormal) * worldNormal);

    // Create the Tangent, BiNormal, Normal Matrix for transforming the normalMap.
    vs_out.TBN = mat3( worldTangent, worldbiNormal, normalize(worldNormal));

    // Calculate vertex position in clip coordinates
    gl_Position = viewProj * chunk.modelMatrix * vec4(vertexPosition, 1.0f);
   
    // This is the cut off distance for bumping the surface.
    vec3 point = vec3(chunk.modelMatrix * vec4(vertexPosition, 1.0));
    vs_out.ln = distance( point.xyz,cameraPos.xyz );

    if (vs_out.ln < props._start + props._end) { vs_out.ln = 1.0 - (vs_out.ln-props._start)/props._end;}
    else {vs_out.ln = 0.0;}

}
