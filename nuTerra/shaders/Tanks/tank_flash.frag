#version 450 core

layout (binding = 0) uniform sampler2D atlas;

uniform vec3 tint;

in vec2 vUV;
in float vAlpha;

layout (location = 0) out vec4 fragColor;

void main(void)
{
    vec4 c = texture(atlas, vUV);
    float a = c.a * vAlpha;
    if (a <= 0.004) discard;

    // ADDITIVE, and alpha zero. Three cards cross each other at the muzzle and
    // each covers roughly the same air; blending them would let the nearest
    // one hide the other two and the flame would lose a third of itself
    // whenever the camera crossed a card. Adding makes the crossing brighter
    // instead, which is what a flame does.
    //
    // Alpha 0 is what makes the FX buffer's One / OneMinusSrcAlpha reduce to
    // dst + src - it adds light and attenuates nothing.
    fragColor = vec4(c.rgb * tint * a, 0.0);
}
