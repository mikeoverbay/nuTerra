﻿#version 450 core

#extension GL_ARB_bindless_texture : require
#extension GL_ARB_shading_language_include : require

#define USE_PERVIEW_UBO
#include "common.h" //! #include "../common.h"

layout (location = 0) out vec4 gColor;
layout (location = 1) out vec4 gNormal;
layout (location = 6) out vec4 gGMF;

layout (binding = 0) uniform sampler2D depthMap;
layout (binding = 1) uniform sampler2D igGMF;

layout (binding = 2) uniform sampler2D normal_tex;
layout (binding = 3) uniform sampler2D color_tex;
layout (binding = 4) uniform sampler2D SurfaceNormal;
layout (binding = 5) uniform sampler2D gposition;

uniform vec2 offset;
uniform vec2 scale;
uniform uint influence;
uniform uint mType;
uniform uint v1;
uniform uint v2;
uniform uint vis;
uniform uint wet;
uniform vec3 cam_position;

in VS_OUT {
    flat mat4 invMVP;
    flat vec3 s_vector;
} fs_in;

const vec3 tr = vec3 (0.5 ,0.5 , 0.5);
const vec3 bl = vec3(-0.5, -0.5, -0.5);
const float height_scale = 1.0;


void clip(vec3 v) {
    if (v.x > tr.x || v.x < bl.x ) discard;
    if (v.y > tr.y || v.y < bl.y ) discard;
    if (v.z > tr.z || v.z < bl.z ) discard;
}

mat3 get_tbn (in vec3 v_Position, in vec3 v_Normal, in vec2 UV1){
    vec3 pos_dx = dFdx(v_Position);
    vec3 pos_dy = dFdy(v_Position);
    vec3 tex_dx = dFdx(vec3(UV1, 0.0));
    vec3 tex_dy = dFdy(vec3(UV1, 0.0));
    vec3 t = (tex_dy.t * pos_dx - tex_dx.t * pos_dy) / (tex_dx.s * tex_dy.t - tex_dy.s * tex_dx.t);
    vec3 ng = normalize(v_Normal);

    t = normalize(t - ng * dot(ng, t));
    vec3 b = normalize(cross(ng, t));
    return mat3(t, b, ng);

}
vec3 getNormal( in vec2 UV1)
{
    vec3 normalBump;
    vec4 normal = texture(normal_tex,UV1);
    normalBump.xy = normal.ag * 2.0 - 1.0;
    float dp = min(dot(normalBump.xy, normalBump.xy),1.0);
    normalBump.z = clamp(sqrt(-dp+1.0),-1.0,1.0);
    normalBump = normalize(normalBump);
        //normalBump.x*=-1.0;
    return normalBump;
}


vec2 ParallaxMapping(vec2 texCoords, vec3 viewDir)
{ 
    float height =  texture(igGMF, texCoords).a;    
    vec2 p = viewDir.xy / viewDir.z * (height * height_scale);
    return texCoords - p;    
} 



//        ' FLAG INFO
//        ' 0  = No shading
//        ' 64  = model 
//        ' 128 = terrain
//        ' 255 = sky dome. We will want to control brightness
//        ' more as they are added
//
void main()
{
    // Calculate UVs
    vec2 uv = gl_FragCoord.xy / resolution;
    vec3 position = texture(gposition,uv).xyz;

    // not sure how to do cliping by angle :(
    vec3 normal = texture(SurfaceNormal,uv).xyz;
    float angle = dot(normal,fs_in.s_vector);
//    if (angle > 0.6) discard;
    /*==================================================*/
    int flag = int(texture(igGMF,uv).b*255.0);
    if (flag == 64 && influence == 2) discard;
    if (flag == 128 && influence == 34) discard;
    // influence of 18 seems to be draw on models and terrain.
    /*==================================================*/
    // sample the Depth from the Depthsampler
    float depth = texture(depthMap, uv).x;

    // Calculate clip space by recreating it out of the coordinates and depth-sample
    vec4 ScreenPosition = vec4(uv*2.0-1.0, depth, 1.0);

    // Transform position from screen space to world space
    vec4 WorldPosition = fs_in.invMVP * ScreenPosition;
    vec4 WP = WorldPosition;
    WorldPosition.xyz /= WorldPosition.w;
    WorldPosition.w = 1.0f;
    // trasform to decal original and size.
    // 1 x 1 x 1
    clip (WorldPosition.xyz);

    /*==================================================*/
   WorldPosition.xy += 0.5;
   WorldPosition.xy *= -1.0;
   vec2 tuv = WorldPosition.xy * scale + offset;
   vec4 color =  texture(color_tex, tuv);



   //Get texture UVs
   if (wet ==1) {
       gColor.a = color.r*0.8;
       gColor.rgb = vec3(0.0);
       gNormal.a = color.r;
       gGMF.r = 0.9;
       }
   else
   {
   mat3 TBN = get_tbn(position, texture(SurfaceNormal,uv).xyz, tuv);   

   vec3 view_dir = (TBN * cam_position) - (TBN * position.xyz);

   //tuv = ParallaxMapping( tuv, -view_dir);

   vec4 color =  texture(color_tex, tuv);

    gNormal.xyz = TBN * getNormal(tuv) *0.5 + 0.5;   

   gColor = color;

    uint code = v2;
//
//    if (code == 0) {
//        gColor.rgb = vec3(1.0 ,0.0 ,0.0);
//        }
//    if (code == 1) {
//        gColor.rgb = vec3(0.0 ,1.0 ,0.0);
//        }
//    if (code == 2) {
//        gColor.rgb = vec3(0.0 ,0.0 ,1.0);
//        }
//    if (code == 3) {
//        gColor.rgb = vec3(1.0 ,1.0 ,0.0);
//        }
//    if (code == 4) {
//        gColor.rgb = vec3(0.0 ,1.0 ,1.0);
//        }
//    if (code == 5) {
//        gColor.rgb = vec3(1.0 ,1.0 ,1.0);
//        }
//    if (code == 6) {
//        gColor.rgb = vec3(0.1 ,1.0 ,1.0);
//        }
//
     code = v1;

//    if (code == 0) {
//        gColor.rgb = vec3(1.0 ,0.0 ,0.0);
//        }
//    if (code == 1) {
//        gColor.rgb = vec3(0.0 ,1.0 ,0.0);
//        }
//    if (code == 2) {
//        gColor.rgb = vec3(0.0 ,0.0 ,1.0);
//        }
//    if (code == 3) {
//        gColor.rgb = vec3(1.0 ,1.0 ,0.0);
//        }
//    if (code == 4) {
//        gColor.rgb = vec3(0.0 ,1.0 ,1.0);
//        }
//    if (code == 5) {
//        gColor.rgb = vec3(1.0 ,1.0 ,1.0);
//        }
//    if (code == 6) {
//        gColor.rgb = vec3(0.1 ,1.0 ,1.0);
//        }
//
//

    gNormal.a = color.a;
    }
}


