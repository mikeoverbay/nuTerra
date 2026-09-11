#version 450 core

#extension GL_ARB_shading_language_include : require

#define USE_PERVIEW_UBO
#define USE_COMMON_PROPERTIES_UBO
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

    // TERRAIN ONLY, as an ALLOWLIST rather than a list of things to skip.
    //
    // This used to discard GBUF_RENDER_MODEL and nothing else, which covered
    // buildings and roads and missed the tanks: a tank writes GFLAG_UNLIT so
    // the resolve passes its pixels through unlit, and GBUF_RENDER of that is
    // 0 - neither MODEL nor TERRAIN - so it fell through the test and the ring
    // painted over the hulls standing in the base.
    //
    // The ring cannot simply be drawn BEFORE the tanks instead: this pass runs
    // after the deferred resolve, painting onto the lit image, while the tanks
    // go into the G-buffer long before it. There is no ordering that puts a
    // hull back on top. Asking what a pixel IS, rather than listing what it is
    // not, also excludes the next thing drawn into the G-buffer without
    // anybody remembering to come back here.
    if (GBUF_RENDER(texture(gGMF, UV).b) != GBUF_RENDER_TERRAIN) discard;
    
    // Calculate Worldposition by recreating it out of the coordinates and depth-sample
    vec4 ScreenPosition = vec4(UV*2.0-1.0, Depth, 1.0);

    // Transform position from screen space to world space
    vec4 WorldPosition = invViewProj * ScreenPosition ;
    WorldPosition.xyz /= WorldPosition.w;


    float rs = length(WorldPosition.xz - ring_center.xz);

    float t = 1.0+ smoothstep(radius, radius+thickness, rs) 
    - smoothstep(radius-thickness, radius, rs);

    colorOut = color;
    colorOut.a = (1.0-t) *0.25 * props.BRIGHTNESS;
  
}
