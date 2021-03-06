﻿#version 450 core

#extension GL_ARB_bindless_texture : require
#extension GL_ARB_shading_language_include : require

#define USE_MATERIALS_SSBO
#include "common.h" //! #include "../common.h"

// Output
layout (location = 0) out vec3 gColor;
layout (location = 1) out vec3 gNormal;
layout (location = 2) out vec4 gGMF;
layout (location = 3) out vec3 gPosition;
#ifdef PICK_MODELS
layout (location = 4) out uint gPick;
#endif
layout (location = 5) out vec4 gAux;

// Input from vertex shader
in VS_OUT
{
    vec2 TC1;
    vec3 worldPosition;
    mat3 TBN;
    flat uint material_id;
#ifdef PICK_MODELS
    flat uint model_id;
#endif
} fs_in;

// ================================================================================
// globals
vec3 normalBump;
const MaterialProperties thisMaterial = material[fs_in.material_id];


// ================================================================================
// functions
// ================================================================================
void get_normal()
{
        vec4 normal = texture(sampler2D(thisMaterial.maps[1]), fs_in.TC1);
        float alphaCheck = normal.r;      
        normalBump.xy = normal.ag * 2.0 - 1.0;
        normalBump.z = sqrt(1.0 - dot(normalBump.xy, normalBump.xy));
        alphaCheck = normal.r;

    if (thisMaterial.alphaTestEnable && alphaCheck < thisMaterial.alphaReference) {
        discard;
    }
    gNormal.xyz = normalize(fs_in.TBN * normalBump.xyz);
}

// ================================================================================
// Main start
// ================================================================================
void main(void)
{
    const float renderType = 64.0/255.0; // 64 = PBS, 63 = light/bump

    vec4 co = texture(sampler2D(thisMaterial.maps[0]), fs_in.TC1); // color    vec4 co = textureLod(thisMaterial.maps[0], fs_in.TC1, 0); // color
    //note swizzle here
    gGMF.gr = texture(sampler2D(thisMaterial.maps[2]), fs_in.TC1).rg;
    co.rgb *= thisMaterial.g_colorTint.rgb;
    gAux = co;
    gAux.a = co.a;
    get_normal();

    gPosition = fs_in.worldPosition;
    gGMF.b = renderType;
    gGMF.a = 0.5; // cube map all the time :)

#ifdef PICK_MODELS
    gPick.r = fs_in.model_id + 1;
#endif
}
