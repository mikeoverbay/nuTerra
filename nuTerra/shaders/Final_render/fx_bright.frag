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
    fragColor = vec4(max(c - vec3(threshold), vec3(0.0)), 1.0);
}
