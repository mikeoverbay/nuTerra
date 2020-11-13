﻿#version 450 core

#extension GL_ARB_shading_language_include : require

#define USE_PERVIEW_UBO
#include "common.h" //! #include "../common.h"

out vec4 colorOut;

layout(binding = 0 ) uniform sampler2D depthMap;
layout(binding = 1 ) uniform sampler2D gGMF;
layout(binding = 2 ) uniform sampler2D gPosition;

uniform vec4 color;
uniform vec3 ring_center;
uniform float radius;
uniform float thickness;
uniform mat4 ORTHOPROJECTION;

void main (void)
{
    vec2 UV = gl_FragCoord.xy / resolution;
    float Depth = texture(depthMap, UV).x;
    vec4 PositionIn = vec4(texture(gPosition, UV).xyz, 1.0);

    bool flag = texture(gGMF, UV).b * 255.0 == 64.0;
    if (flag) discard;

    // Calculate Worldposition by recreating it out of the coordinates and depth-sample
    vec4 ScreenPosition = vec4(UV*2.0-1.0, Depth, 1.0);

    // Transform position from screen space to world space
    vec4 WorldPosition = invViewProj * ScreenPosition ;
    WorldPosition.xyz /= WorldPosition.w;
    
    //This should not be needed. PositionIn needs to stay in world space!
    //PositionIn = fs_in.ORTHOVIEW * PositionIn;

    // WorldPosition and PositionIn in are in world space projection
    // at this point.
    if (WorldPosition.z == PositionIn.z) 
    {

        //move to ortho projection
        WorldPosition = ORTHOPROJECTION * WorldPosition;



        float rs = length(WorldPosition.xz - ring_center.xz);
        float t = 1.0+ smoothstep(radius, radius+thickness, rs) 
                        - smoothstep(radius-thickness, radius, rs);



        if (colorOut.a > 0.0)
        {
            //un rem to draw gPosition texture
            //gColor.rgb *= PositionIn.rgb;
            colorOut = color;
            colorOut.a = 1.0-t;
        }

    }// WorldPosition.z == PositionIn.z
}
