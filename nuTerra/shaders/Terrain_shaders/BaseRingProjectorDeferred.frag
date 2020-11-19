#version 450 core

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

void main (void)
{
    if ( gl_FrontFacing ) discard;

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


    float rs = length(WorldPosition.xz - ring_center.xz);

    float t = 1.0+ smoothstep(radius, radius+thickness, rs) 
    - smoothstep(radius-thickness, radius, rs);

    colorOut = color;
    colorOut.a = (1.0-t) *0.25;
  
}
