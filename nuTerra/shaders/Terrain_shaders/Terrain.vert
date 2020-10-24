#version 450 core

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

layout (std140, binding = TERRAIN_LAYERS_UBO_BASE) uniform Layers {
    vec4 layer0UT1;
    vec4 layer1UT1;
    vec4 layer2UT1;
    vec4 layer3UT1;

    vec4 layer0UT2;
    vec4 layer1UT2;
    vec4 layer2UT2;
    vec4 layer3UT2;

    vec4 layer0VT1;
    vec4 layer1VT1;
    vec4 layer2VT1;
    vec4 layer3VT1;

    vec4 layer0VT2;
    vec4 layer1VT2;
    vec4 layer2VT2;
    vec4 layer3VT2;

    float used_1;
    float used_2;
    float used_3;
    float used_4;
    float used_5;
    float used_6;
    float used_7;
    float used_8;
};

uniform vec2 map_size;
uniform vec2 map_center;
uniform vec2 me_location;
uniform mat4 modelMatrix;
uniform mat3 normalMatrix;

out VS_OUT {
    mat3 TBN;
    vec4 Vertex;
    vec3 worldPosition;
    vec2 tuv4, tuv4_2, tuv3, tuv3_2;
    vec2 tuv2, tuv2_2, tuv1, tuv1_2;
    vec2 UV;
    vec2 Global_UV;
    float ln;
    flat float is_hole;
} vs_out;

void main(void)
{

    vs_out.UV =  vertexTexCoord;
    // calculate tex coords for global_AM
    vec2 uv_g;
    vec2 scaled = vs_out.UV / map_size;
    vec2 m_s = vec2(1.0)/map_size;
    uv_g.x = ((( (me_location.x )-50.0)/100.0)+map_center.x) * m_s.x ;
    uv_g.y = ((( (me_location.y )-50.0)/100.0)-map_center.y) * m_s.y ;
    vs_out.Global_UV = scaled + uv_g;
    vs_out.Global_UV.xy = 1.0 - vs_out.Global_UV.xy;
    
    vs_out.is_hole = vertexNormal.w ;
    //-------------------------------------------------------
    vec3 vertexPosition = vec3( vertexXZ.x, vertexY, vertexXZ.y );
    //-------------------------------------------------------

    // This is the morph blend distance.
    vec3 point = vec3(modelMatrix * vec4(vertexPosition, 1.0));
    float dist = max(distance( point.xyz,cameraPos.xyz ), 0.0);
    float start_ = 75.0;
    float end_ = 150.0;
    if (dist  > start_)
    {
        if (dist < start_ + end_)
        {
            dist = (dist-start_)/end_;
        }else{
            dist = 1.0;
        }
    } 
    else {dist = 0.0;}
    //-------------------------------------------------------
    vertexPosition = vec3( mix(vertexPosition.x,vertexXZ_morph.x,dist),
                                mix(vertexPosition.y,vertexY_morph,dist) ,
                                mix(vertexPosition.z,vertexXZ_morph.y,dist));

     //-------------------------------------------------------
   // Calulate UVs for the texture layers

    vs_out.Vertex = vec4(vertexPosition, 1.0) * 1.0;
    vs_out.Vertex.x *= -1.0;
    vec4 sVert = vs_out.Vertex;
    //
    vs_out.tuv4 = -vec2(dot(-layer3UT1, sVert), dot(layer3VT1, sVert))+0.5 ;
    vs_out.tuv4_2 = -vec2(dot(-layer3UT2, sVert), dot(layer3VT2, sVert))+0.5 ;

    vs_out.tuv3 = -vec2(dot(-layer2UT1, sVert), dot(layer2VT1, sVert))+0.5 ;
    vs_out.tuv3_2 = -vec2(dot(-layer2UT2, sVert), dot(layer2VT2, sVert))+0.5 ;

    vs_out.tuv2 = -vec2(dot(-layer1UT1, sVert), dot(layer1VT1, sVert))+0.5;
    vs_out.tuv2_2 = -vec2(dot(-layer1UT2, sVert), dot(layer1VT2, sVert))+0.5;

    vs_out.tuv1 = -vec2(dot(-layer0UT1, sVert), dot(layer0VT1, sVert))+0.5 ;
    vs_out.tuv1_2 = -vec2(dot(-layer0UT2, sVert), dot(layer0VT2, sVert))+0.5 ;
    //-------------------------------------------------------

    //-------------------------------------------------------
    // Calculate biNormal
    vec3 VT, VB, VN ;
    VN = normalize(mix(vertexNormal.xyz, vertexNormal_morph.xyz, dist));
    VT = normalize(mix(vertexTangent.xyz, vertexTangent_morph.xyz, dist));

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
    vs_out.TBN = mat3( normalize(worldTangent), normalize(worldbiNormal), normalize(worldNormal));

    // Calculate vertex position in clip coordinates
    gl_Position = viewProj * modelMatrix * vec4(vertexPosition, 1.0f);
   
    // This is the cut off distance for bumping the surface.
    point = vec3(modelMatrix * vec4(vertexPosition, 1.0));
    vs_out.ln = distance( point.xyz,cameraPos.xyz );
    float start = 75.0;
    float end = 200.0;
    if (vs_out.ln < start + end) { vs_out.ln = 1.0 - (vs_out.ln-start)/end;} 
    else {vs_out.ln = 0.0;}

}
