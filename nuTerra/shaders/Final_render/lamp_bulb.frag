#version 450 core

// Emits premultiplied colour with ZERO coverage. Under the FX buffer's
// One / OneMinusSrcAlpha that reduces to dst + src, so the bulb ADDS light and
// attenuates nothing behind it - the same contract the volumetric meshes use
// (docs/FX_PIPELINE.md, "Cards first, meshes second"). The alpha channel there
// is accumulated coverage, and a light source has none: it is not a surface.

in vec2 vOffset;

uniform vec3 bulb_color;   // sRGB, as the picker holds it
uniform float bulb_gain;

out vec4 fragColor;

void main(void)
{
    float d = length(vOffset);
    if (d >= 1.0) discard;

    // Tight core, soft shoulder. Cubed falloff reads as a bulb rather than a
    // sticker: nearly all the energy sits in the middle few pixels, which is
    // both what a filament looks like and what the bright pass wants to find -
    // a flat disc of the same total energy blooms as a ring.
    float f = 1.0 - d;
    float core = f * f * f;

    // Linearise. The colour is authored in a picker and stored sRGB, the same
    // as every other lamp colour, and this buffer is linear float.
    vec3 linear = pow(bulb_color, vec3(2.2));

    fragColor = vec4(linear * (core * bulb_gain), 0.0);
}
