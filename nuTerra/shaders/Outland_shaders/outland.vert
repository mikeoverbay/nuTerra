#version 450 core

#extension GL_ARB_shading_language_include : require

#define USE_PERVIEW_UBO
#include "common.h" //! #include "../common.h"

layout(location = 0) in vec2 vertexPosition;
layout(location = 1) in vec2 UVs;
layout(location = 2) in vec4 vertexNormal;
layout(location = 3) in vec3 vertexTangent;

layout(binding = 1) uniform sampler2D heigth_map;

uniform float y_range;
uniform float y_offset;

uniform vec2 scale;
uniform vec2 center_offset;

uniform mat4 nMatrix;

layout(location = 0) out VS_OUT {
    vec3 vertexPosition;
    mat3 TBN;
    vec2 UV;
    float specular;
} vs_out;


void main(void)
{
    vec2 UV = -UVs;
    vs_out.UV = UV;
    

    vec3 VT, VB, VN ;
    VN = normalize(vertexNormal.xyz);
    VT = normalize(vertexTangent);
    VT = VT - dot(VN, VT) * VN;
    VB = cross(VT, VN);
    // Tangent, biNormal and Normal must be trasformed by the normal Matrix.
    mat3 normalMatrix = mat3(nMatrix);
    vec3 worldNormal = normalMatrix * VN;
    vec3 worldTangent = normalMatrix * VT;
    vec3 worldbiNormal = normalMatrix * VB;

    // make perpendicular
    worldTangent = normalize(worldTangent - dot(worldNormal, worldTangent) * worldNormal);
    worldbiNormal = normalize(worldbiNormal - dot(worldNormal, worldbiNormal) * worldNormal);

    // Create the Tangent, BiNormal, Normal Matrix for transforming the normalMap.
    vs_out.TBN = mat3(worldTangent, worldbiNormal, normalize(worldNormal));

    vec3 pos;
    pos.xz = vertexPosition.xy * scale;
    pos.xz += center_offset ;
    pos.y = texture(heigth_map, UV).x;

    pos.y = pos.y;

    pos.y = pos.y * y_range + y_offset-1.5;
    vs_out.vertexPosition = vec3( view * vec4(pos,1.0) );

    gl_Position = viewProj * vec4(pos, 1.0);

}
