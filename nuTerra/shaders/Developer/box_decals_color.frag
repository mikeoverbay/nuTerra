#version 450 core

#extension GL_ARB_bindless_texture : require
#extension GL_ARB_shading_language_include : require

#define USE_PERVIEW_UBO
#include "common.h" //! #include "../common.h"

layout (location = 0) out vec4 gColor;
layout (location = 1) out vec4 gNormal;

layout (binding = 0) uniform sampler2D depthMap;
layout (binding = 1) uniform sampler2D igGMF;
layout (binding = 2) uniform sampler2D color_tex;
layout (binding = 3) uniform sampler2D normal_tex;

in VS_OUT {
    flat mat4 invMVP;
} fs_in;

const vec3 tr = vec3 (0.5 ,0.5 , 0.5);
const vec3 bl = vec3(-0.5, -0.5, -0.5);

void clip(vec3 v) {
    if (v.x > tr.x || v.x < bl.x ) discard;
    if (v.y > tr.y || v.y < bl.y ) discard;
    if (v.z > tr.z || v.z < bl.z ) discard;
}

void main()
{
    // Calculate UVs
    vec2 uv = gl_FragCoord.xy / resolution;

    /*==================================================*/
    bool flag = texture(igGMF,uv).b*255.0 == 64.0;
    if (flag) discard;

    /*==================================================*/
    // sample the Depth from the Depthsampler
    float depth = texture(depthMap, uv).x;

    // Calculate clip space by recreating it out of the coordinates and depth-sample
    vec4 ScreenPosition = vec4(uv*2.0-1.0, depth, 1.0);

    // Transform position from screen space to world space
    vec4 WorldPosition = fs_in.invMVP * ScreenPosition;
    WorldPosition.xyz /= WorldPosition.w;
    WorldPosition.w = 1.0f;
    // trasform to decal original and size.
    // 1 x 1 x 1
    clip (WorldPosition.xyz);

    /*==================================================*/
    //Get texture UVs
    WorldPosition.xy += 0.5;

    vec4 color =  texture(color_tex, WorldPosition.xy);
    gColor = color;
    vec4 normal =  texture(normal_tex, WorldPosition.xy);
    vec3 normalBump;
    normalBump.xy = normal.ag * 2.0 - 1.0;
    float dp = min(dot(normalBump.xy, normalBump.xy),1.0);
    normalBump.z = clamp(sqrt(-dp+1.0),-1.0,1.0);

    gNormal.xyz = normalize(normalBump) *0.5 + 0.5;
    gNormal.a = color.a;
 
}


