#version 450 core

// Bright pass for the FX glow.
//
// Reads the accumulated FX buffer and keeps only the energy that is ABOVE
// displayable range. That threshold is not a tuning guess: gFX_HDR holds the
// premultiplied sum before composite_fx scales it back down, so everything
// over 1.0 here is exactly the energy that used to clip against Rgba8 and turn
// the fire white. Glowing precisely that is what makes a hot core read as hot
// instead of merely bright.
//
// Smoke sums well below 1.0 and so contributes nothing, which is why this is a
// subtract rather than a multiply - no separate "is this smoke" test is needed.
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
