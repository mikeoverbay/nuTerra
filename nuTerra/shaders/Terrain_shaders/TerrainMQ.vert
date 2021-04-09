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
    vec4 Vertex;
    vec3 worldPosition;
    vec2 UV;
    vec2 Global_UV;
    float ln;
    flat uint map_id;
} vs_out;

//-------------------------------------------------------
//-------------------------------------------------------

const TerrainChunkInfo chunk = chunks[gl_BaseInstanceARB];

void main(void)
{
    vs_out.map_id = gl_BaseInstanceARB;

    vs_out.UV =  vertexTexCoord;

    // calculate tex coords for global_AM
    vs_out.Global_UV = 1.0 - (chunk.g_uv_offset + (vertexTexCoord * props.map_size));

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