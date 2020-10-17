#version 450 core

#extension GL_ARB_shading_language_include : require
#include "common.h"

layout(location = 0) in vec2 vertexXZ;
layout(location = 1) in float vertexY;
layout(location = 2) in vec2 vertexTexCoord;
layout(location = 3) in vec4 vertexNormal;
layout(location = 4) in vec3 vertexTangent;

layout(location = 7) uniform vec2 map_size;
layout(location = 8) uniform vec2 map_center;
layout(location = 9) uniform vec3 cam_position;

layout(location = 10) uniform mat4 modelMatrix;
layout(location = 11) uniform mat3 normalMatrix;
layout(location = 12) uniform vec2 me_location;

layout (std140, binding = 0 ) uniform Layers {
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

layout (binding = PER_FRAME_DATA_BASE, std140) uniform PerView {
    mat4 view;
    mat4 projection;
    mat4 viewProj;
    mat4 invViewProj;
    vec3 cameraPos;
};

out vec4 Vertex;
out float ln;
out mat3 TBN;
out vec3 worldPosition;
out vec2 tuv4, tuv4_2, tuv3, tuv3_2;
out vec2 tuv2, tuv2_2, tuv1, tuv1_2;
out vec2 UV;
out vec2 Global_UV;

flat out uint is_hole;

void main(void)
{

    UV =  vertexTexCoord;
    // calculate tex coords for global_AM
    vec2 uv_g;
    vec2 scaled = UV / map_size;
    vec2 m_s = vec2(1.0)/map_size;
    uv_g.x = ((( (me_location.x )-50.0)/100.0)+map_center.x) * m_s.x ;
    uv_g.y = ((( (me_location.y )-50.0)/100.0)-map_center.y) * m_s.y ;
    Global_UV = scaled + uv_g;
    Global_UV.xy = 1.0 - Global_UV.xy;
    
    is_hole = (vertexNormal.w == 1.0f) ? 1 : 0;
    //-------------------------------------------------------
    // Calulate UVs for the texture layers
    vec3 vertexPosition = vec3(vertexXZ.x, vertexY, vertexXZ.y);
    Vertex = vec4(vertexPosition, 1.0) * 1.0;
    Vertex.x *= -1.0;
    vec4 sVert = Vertex;
    //sVert = vec4(UV.x*100.0, 0.0, UV.y*100.0, 0.0);//Vertex.xyz;// * vec3(0.875) + vec3(0.0625);
    
    //
    tuv4 = -vec2(dot(-layer3UT1, sVert), dot(layer3VT1, sVert))+0.5 ;
    tuv4_2 = -vec2(dot(-layer3UT2, sVert), dot(layer3VT2, sVert))+0.5 ;

    tuv3 = -vec2(dot(-layer2UT1, sVert), dot(layer2VT1, sVert))+0.5 ;
    tuv3_2 = -vec2(dot(-layer2UT2, sVert), dot(layer2VT2, sVert))+0.5 ;

    tuv2 = -vec2(dot(-layer1UT1, sVert), dot(layer1VT1, sVert))+0.5;
    tuv2_2 = -vec2(dot(-layer1UT2, sVert), dot(layer1VT2, sVert))+0.5;

    tuv1 = -vec2(dot(-layer0UT1, sVert), dot(layer0VT1, sVert))+0.5 ;
    tuv1_2 = -vec2(dot(-layer0UT2, sVert), dot(layer0VT2, sVert))+0.5 ;


    //-------------------------------------------------------
    // clip border - dont work!
//    tuv1 = (tuv1*0.875) + (tuv1*0.0625);
//    tuv2 = (tuv2*0.875) + (tuv2*0.0625);
//    tuv3 = (tuv3*0.875) + (tuv3*0.0625);
//    tuv4 = (tuv4*0.875) + (tuv4*0.0625);
//
//    tuv1_2 = (tuv1_2*0.875) + (tuv1_2*0.0625);
//    tuv2_2 = (tuv2_2*0.875) + (tuv2_2*0.0625);
//    tuv3_2 = (tuv3_2*0.875) + (tuv3_2*0.0625);
//    tuv4_2 = (tuv4_2*0.875) + (tuv4_2*0.0625);
    //-------------------------------------------------------

    //-------------------------------------------------------
    // Calculate biNormal
    vec3 VT, VB, VN ;
    VN = normalize(vertexNormal.xyz);
    VT = normalize(vertexTangent.xyz);

    VT = VT - dot(VN, VT) * VN;
    VB = cross(VT, VN);
    //-------------------------------------------------------

    // vertex --> world pos
    worldPosition = vec3(view * modelMatrix * vec4(vertexPosition, 1.0f));

    // Tangent, biNormal and Normal must be trasformed by the normal Matrix.
    vec3 worldNormal = normalMatrix * VN;
    vec3 worldTangent = normalMatrix * VT;
    vec3 worldbiNormal = normalMatrix * VB;

    // make perpendicular
    worldTangent = normalize(worldTangent - dot(worldNormal, worldTangent) * worldNormal);
    worldbiNormal = normalize(worldbiNormal - dot(worldNormal, worldbiNormal) * worldNormal);

    // Create the Tangent, BiNormal, Normal Matrix for transforming the normalMap.
    TBN = mat3( normalize(worldTangent), normalize(worldbiNormal), normalize(worldNormal));

    // Calculate vertex position in clip coordinates
    gl_Position = viewProj * modelMatrix * vec4(vertexPosition, 1.0f);
   
    // This is the cut off distance for bumpping the surface.
    vec3 point = vec3(modelMatrix * vec4(vertexPosition, 1.0));
    ln = distance( point.xyz,cam_position.xyz );
    float start = 100.0;
    if (ln < start + 200.0) { ln = 1.0 - (ln-start)/200.0;} 
    else {ln = 0.0;}

}
