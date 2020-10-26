#version 450 core

#extension GL_ARB_bindless_texture : require
#extension GL_ARB_shading_language_include : require

#define USE_MATERIALS_SSBO
#include "common.h"

// Output
layout (location = 1) out vec3 gNormal;
layout (location = 2) out vec3 gGMF;
layout (location = 3) out vec3 gPosition;
layout (location = 4) out uint gPick;
layout (location = 5) out vec4 gAux;

// Input from vertex shader
in VS_OUT
{
    vec2 TC1;
    vec3 worldPosition;
    mat3 TBN;
    flat uint material_id;
    flat uint model_id;
    flat uint lod_level;
} fs_in;

// ================================================================================
// globals
vec3 normalBump;
const float PI = 3.14159265359;
MaterialProperties thisMaterial = material[fs_in.material_id];


// ================================================================================
// functions
// ================================================================================
void get_normal()
{
    float alphaCheck = 1.0;
    if (thisMaterial.g_useNormalPackDXT1) {
        normalBump = (texture(thisMaterial.maps[1], fs_in.TC1).rgb * 2.0f) - 1.0f;
    } else {
        vec4 normal = texture(thisMaterial.maps[1], fs_in.TC1);
        normalBump.xy = normal.ag * 2.0 - 1.0;
        normalBump.z = sqrt(1.0 - dot(normalBump.xy, normalBump.xy));
        alphaCheck = normal.r;
    }
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
    float renderType = 64.0/255.0; // 64 = PBS, 63 = light/bump

    if (thisMaterial.shader_type != 6) {
        discard;
    }

    vec4 co = texture(thisMaterial.maps[0], fs_in.TC1); // color    vec4 co = textureLod(thisMaterial.maps[0], fs_in.TC1, 0); // color
    //note swizzle here
    gGMF.gr = texture(thisMaterial.maps[2], fs_in.TC1).rg;
    co *= thisMaterial.g_colorTint;
    gAux = co;
    gAux.a = 0.55;
    get_normal();

    // Just for debugging
    //gColor.r += fs_in.lod_level;

    gPosition = fs_in.worldPosition;
    gGMF.b = renderType;

//    if (fs_in.lod_level == 1)      { gColor.r = 1; }
//    else if (fs_in.lod_level == 2) { gColor.g = 1; }
//    else if (fs_in.lod_level == 3) { gColor.b = 1; }

    gPick.r = fs_in.model_id + 1;

}
// ================================================================================
