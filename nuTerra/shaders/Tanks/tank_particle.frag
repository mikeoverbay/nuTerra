#version 450 core

in vec2 vOffset;
in vec4 vColour;

layout (location = 0) out vec4 fragColor;

void main(void)
{
    float d = length(vOffset);
    if (d > 1.0) discard;

    // Soft round puff. Squared falloff rather than a hard edge, because a
    // trail is hundreds of these overlapping and a visible rim on each turns
    // the streak into a string of beads.
    float s = 1.0 - d;
    float a = vColour.a * s * s;

    // PREMULTIPLIED, and alpha 0 - the FX buffer blends One / OneMinusSrcAlpha,
    // so this adds light and attenuates nothing. Vapour is not additive in
    // life, but this buffer is where the glow is built and a trail that
    // subtracts from the scene behind it would punch a hole in the terrain.
    fragColor = vec4(vColour.rgb * a, 0.0);
}
