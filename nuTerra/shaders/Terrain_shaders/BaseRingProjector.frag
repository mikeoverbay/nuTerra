#version 450 core

#extension GL_ARB_shading_language_include : require

#define USE_PERVIEW_UBO
#include "common.h" //! #include "../common.h"

layout (location = 0) out vec4 gColor;

uniform sampler2D depthMap;
uniform sampler2D gGMF;
uniform vec4 color;
uniform vec3 ring_center;
uniform float radius;
uniform float thickness;

void main (void)
{
    vec2 UV = gl_FragCoord.xy / resolution;
    float Depth = texture(depthMap, UV).x;

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
    gColor = color;
    gColor.a = 1.0-t;
    if (gColor.a <0.0) {
        discard;
    }
}
