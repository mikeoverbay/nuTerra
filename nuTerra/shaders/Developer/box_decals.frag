#version 450 core

#extension GL_ARB_bindless_texture : require
#extension GL_ARB_shading_language_include : require

#define USE_PERVIEW_UBO
#define USE_DECALS_SSBO
#include "common.h" //! #include "../common.h"

layout (location = 0) out vec4 gColor;
//layout (location = 1) out vec4 nColor;

layout (binding = 0) uniform sampler2D depthMap;
layout (binding = 1) uniform sampler2D gGMF;


in VS_OUT {
    flat mat4 invMVP;
    flat uint decal_id;
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
    bool flag = texture(gGMF,uv).b*255.0 == 64.0;
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

    vec4 color =  texture(sampler2D(decals[fs_in.decal_id].color_tex),  WorldPosition.xy);
    // vec4 normal = texture(sampler2D(decals[fs_in.decal_id].normal_tex), WorldPosition.xy);

    if (color.a < 0.05) { discard; }
    gColor = color;
    gColor.a = 0.0;
    // nColor.rgb = normal.rgb;
}
