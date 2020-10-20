#version 450 core

#extension GL_ARB_shading_language_include : require

#define USE_PERVIEW_UBO
#include "common.h"

layout(location = 0) in vec2 vertexXZ;
layout(location = 1) in float vertexY;
layout(location = 2) in vec2 vertexTexCoord;
layout(location = 3) in vec4 vertexNormal;
layout(location = 4) in vec4 vertexTangent;

uniform mat4 model;

out VS_OUT
{
    vec3 n;
    vec3 t;
    vec3 b;
} vs_out;

void main(void)
{
    vec3 vertexPosition = vec3(vertexXZ.x, vertexY, vertexXZ.y);
    
    // Calculate vertex position in clip coordinates
    gl_Position = viewProj * model * vec4(vertexPosition, 1.0);

    mat3 normalMatrix = mat3(transpose(inverse(view * model)));
    vec3 VT = vertexTangent.xyz - dot(vertexNormal.xyz, vertexTangent.xyz) * vertexNormal.xyz;
    vec3 worldBiTangent = cross(VT, vertexNormal.xyz);
    //--------------------
    // NOTE: vertexNormal is already normalized in the VBO.
    vs_out.n = normalize(vec3(projection * vec4(normalMatrix * vertexNormal.xyz, 0.0f)));
    vs_out.t = normalize(vec3(projection * vec4(normalMatrix * vertexTangent.xyz, 0.0f)));
    vs_out.b= normalize(vec3(projection * vec4(normalMatrix * worldBiTangent.xyz, 0.0f)));
}
