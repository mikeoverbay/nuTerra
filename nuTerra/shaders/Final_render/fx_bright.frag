#version 450 core

// Bright pass for the FX glow.
//
// Reads the accumulated FX buffer and keeps the energy above `threshold`.
//
// 1.0 is the principled setting: gFX_HDR holds the premultiplied sum before
// composite_fx scales it back down, so above 1.0 is exactly the energy that
// used to clip against Rgba8 and turn the fire white. At that setting smoke,
// which sums well below 1.0, contributes nothing and no separate "is this
// smoke" test is needed.
//
// THE SHIPPED VALUE IS LOWER THAN THAT (FX_GLOW_THRESHOLD, hard wired at
// 0.42), so smoke DOES glow here - lightly, which is what was wanted.
//
// 0.42 is a FLOOR found by eye, not a midpoint: below it the smoke starts to
// bloom badly, and at it the smoke is only just lit. There is no headroom
// underneath. Do not "fix" it back to 1.0 on the strength of the paragraph
// above, and do not lower it expecting more glow - lower it and you get
// glowing smoke, not a hotter fire.
//
// Runs at reduced resolution. The quad covers the whole viewport, and the
// source is sampled Linear, so this doubles as the downsample - the box
// averaging that comes free with a smaller destination is wanted here.

layout(binding = 0) uniform sampler2D fxBuffer;

// The scene depth behind the FX. Carried in this pass's spare ALPHA so the
// blur spreads it alongside the energy and the composite can tell whether the
// surface it is about to light sits in FRONT of the glow that made it.
//
// The FX cards themselves are already occluded - fx_fbo borrows gDepth and the
// FX passes depth-TEST - so a fire behind a building contributes nothing at
// that building's pixels. What leaks is this blur, which spreads sideways in
// screen space long after that test, with the composite adding it fullscreen.
layout(binding = 1) uniform sampler2D depthMap;

uniform float threshold;

in vec2 texCoord;
layout(location = 0) out vec4 fragColor;

void main(void)
{
    vec3 c = texture(fxBuffer, texCoord).rgb;

    // Soft knee would round the shoulder, but a hard subtract keeps the glow
    // anchored to real over-range energy and cannot lift anything that was
    // already in gamut. Alpha is not carried: the glow ADDS light and must
    // never attenuate the scene, so the composite adds it with coverage taken
    // from the FX buffer, not from here.
    vec3 e = max(c - vec3(threshold), vec3(0.0));

    // Depth is written ONLY where there is energy; everywhere else it is 1.0,
    // the far plane. That matters: the blur averages this channel, so an empty
    // neighbour must not drag the result toward whatever happens to be behind
    // it. Far is the safe default because the test below only ever suppresses
    // when the receiving surface is NEARER than the glow.
    float lit = any(greaterThan(e, vec3(0.0))) ? 1.0 : 0.0;
    float d   = mix(1.0, texture(depthMap, texCoord).r, lit);

    fragColor = vec4(e, d);
}
