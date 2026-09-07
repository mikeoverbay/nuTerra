#version 450 core

#extension GL_ARB_shading_language_include : require

#define USE_PERVIEW_UBO
#include "common.h" //! #include "../common.h"

// Emits premultiplied colour with ZERO coverage. Under the FX buffer's
// One / OneMinusSrcAlpha that reduces to dst + src, so the bulb ADDS light and
// attenuates nothing behind it - the same contract the volumetric meshes use
// (docs/FX_PIPELINE.md, "Cards first, meshes second"). The alpha channel there
// is accumulated coverage, and a light source has none: it is not a surface.

// The scene depth, for the occlusion test below. The pass itself runs with the
// depth test OFF - see the test for why.
layout(binding = 0) uniform sampler2D depthMap;

in vec2 vOffset;
in float vDim;      // 1 at true size, lower where the pixel floor inflated it
in vec4 vCentre;    // clip-space centre of the bulb
in float vThresh;   // window depth of a point see_thru metres in front of it

uniform vec3 bulb_color;   // sRGB, as the picker holds it
uniform float bulb_gain;

out vec4 fragColor;

// How much of the bulb the scene lets through, 0..1.
//
// This is done HERE, per sprite, rather than by letting the hardware depth test
// do it per pixel. Per pixel is what produced donuts: the bulb sits inside its
// fixture, the housing in front of it is nearer, so the test cut the middle out
// of the disc and left the rim that overhangs the silhouette. Testing one point
// against a threshold set slightly in FRONT of the bulb lets it shine through
// its own glass and hood while a wall still stops it.
//
// Five taps rather than one so a bulb passing behind a pole fades over a few
// pixels instead of snapping off.
float visibility()
{
    if (vCentre.w <= 0.0) return 0.0;

    vec3 ndc = vCentre.xyz / vCentre.w;
    if (any(greaterThan(abs(ndc.xy), vec2(1.0)))) return 0.0;

    vec2 uv = ndc.xy * 0.5 + 0.5;
    vec2 px = 3.0 / resolution;

    float vis = 0.0;
    vis += step(vThresh, texture(depthMap, uv).r);
    vis += step(vThresh, texture(depthMap, uv + vec2( px.x, 0.0)).r);
    vis += step(vThresh, texture(depthMap, uv - vec2( px.x, 0.0)).r);
    vis += step(vThresh, texture(depthMap, uv + vec2( 0.0, px.y)).r);
    vis += step(vThresh, texture(depthMap, uv - vec2( 0.0, px.y)).r);
    return vis * 0.2;
}

void main(void)
{
    float d = length(vOffset);
    if (d >= 1.0) discard;

    float vis = visibility();
    if (vis <= 0.0) discard;

    // Tight core, soft shoulder. Cubed falloff reads as a bulb rather than a
    // sticker: nearly all the energy sits in the middle few pixels, which is
    // both what a filament looks like and what the bright pass wants to find -
    // a flat disc of the same total energy blooms as a ring.
    float f = 1.0 - d;
    float core = f * f * f;

    // Linearise. The colour is authored in a picker and stored sRGB, the same
    // as every other lamp colour, and this buffer is linear float.
    vec3 linear = pow(bulb_color, vec3(2.2));

    fragColor = vec4(linear * (core * bulb_gain * vDim * vis), 0.0);
}
