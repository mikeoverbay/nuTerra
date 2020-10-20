#version 450 core

#extension GL_ARB_shading_language_include : require
#include "common.h"

layout (location = 0) out vec4 gColor;

uniform sampler2D depthMap;
uniform vec4 color;
uniform vec3 ring_center;
uniform float radius;
uniform float thickness;

layout (binding = PER_FRAME_DATA_BASE, std140) uniform PerView {
    mat4 view;
    mat4 projection;
    mat4 viewProj;
    mat4 invViewProj;
    vec3 cameraPos;
    vec2 resolution;
};

void main (void)
{
    vec2 UV = gl_FragCoord.xy / resolution;
    float Depth = texture(depthMap, UV).x;

    // Calculate Worldposition by recreating it out of the coordinates and depth-sample
    vec4 ScreenPosition;
    ScreenPosition.xy = UV * 2.0 - 1.0;
    ScreenPosition.z = (Depth);
    ScreenPosition.w = 1.0f;
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
