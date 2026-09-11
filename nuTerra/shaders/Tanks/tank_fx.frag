#version 450 core

#extension GL_ARB_shading_language_include : require

#define USE_PERVIEW_UBO
#include "common.h" //! #include "../common.h"

layout (binding = 0) uniform sampler2D gDepth;

uniform vec3 colour;
uniform float phase;    // 0..1 through this puff's life
uniform float gain;

in vec2 vOffset;
in float vDepth;
in vec4 vCentre;

layout (location = 0) out vec4 fragColor;

void main(void)
{
    float d = length(vOffset);
    if (d > 1.0) discard;

    // OCCLUDED AT THE CENTRE, not per pixel. A burst sits ON the surface it
    // came from, so half of its disc is always behind that surface and a
    // per-pixel depth test carves it into a crescent. Testing the centre asks
    // the only question worth asking - is the event itself hidden behind
    // something - and lets the disc spill over the edge of what it hit, which
    // is what an explosion does.
    vec2 uv = (vCentre.xy / max(vCentre.w, 1e-6)) * 0.5 + 0.5;
    if (uv.x > 0.0 && uv.x < 1.0 && uv.y > 0.0 && uv.y < 1.0) {
        // Reversed depth: the engine's test is Greater, so a LARGER stored
        // value is nearer. Something in front of the flash therefore has a
        // depth above the flash's own.
        float scene = texture(gDepth, uv).r;
        if (scene > vDepth + 1e-6) discard;
    }

    // Two terms, because a flash is a hot core in a soft glow and one falloff
    // gives either a hard disc or a smudge. The core is what clears the bright
    // pass and becomes the halo downstream; the glow is what the eye reads as
    // the flash having size.
    float core = pow(max(1.0 - d, 0.0), 6.0);
    float glow = pow(max(1.0 - d, 0.0), 2.0);

    // Fades over its life, and fades FASTER than it grows. An expanding disc
    // at constant brightness reads as a balloon; energy spread over a larger
    // area has to dim, so the square keeps the total roughly conserved.
    float fade = max(1.0 - phase, 0.0);
    fade *= fade;

    vec3 rgb = colour * (core * 6.0 + glow) * fade * gain;

    // Alpha 0. The FX buffer blends One / OneMinusSrcAlpha, so an alpha of
    // zero makes this dst + src - it adds light and attenuates nothing, which
    // is what lets two overlapping bursts brighten instead of covering each
    // other. Emitting a real alpha here would make the nearer one wipe out
    // the further one's contribution.
    fragColor = vec4(rgb, 0.0);
}
