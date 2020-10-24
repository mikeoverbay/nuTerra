﻿#version 450 core

#extension GL_ARB_shading_language_include : require

#define USE_PERVIEW_UBO
#include "common.h"

layout(location = 0) in vec2 vertexXZ;
layout(location = 1) in float vertexY;
layout(location = 2) in vec4 vertexNormal;
layout(location = 3) in vec4 vertexTangent;
layout(location = 4) in vec2 vertexXZ_morph;
layout(location = 5) in float vertexY_morph;
layout(location = 6) in vec4 vertexNormal_morph;
layout(location = 7) in vec4 vertexTangent_morph;

uniform mat4 model;

uniform float morph_start;
uniform float morph_end;

out VS_OUT
{
    vec3 n;
    vec3 t;
    vec3 b;
} vs_out;

void main(void)
{
    //-------------------------------------------------------
    vec3 vertexPosition = vec3( vertexXZ.x, vertexY, vertexXZ.y );
    //-------------------------------------------------------

    // This is the morph blend distance.
    vec3 point = vec3(model * vec4(vertexPosition, 1.0));
    float dist = max(distance( point.xyz,cameraPos.xyz ), 0.0);
    float start_ = morph_start;
    float end_ = morph_end;
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
    //dist = 1.0;
    //-------------------------------------------------------
    //blend vertices
    vertexPosition = vec3( mix(vertexPosition.x,vertexXZ_morph.x,dist),
                                mix(vertexPosition.y,vertexY_morph,dist) ,
                                mix(vertexPosition.z,vertexXZ_morph.y,dist));    

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
