#version 450 core

layout (location = 0) out vec4 outColor;

in vec2 texCoord;

uniform vec3 color;

void main(void)
{
    // Soft round blob for now - no texture yet, this is just to see where the
    // GFX_models sit. A ring so overlapping markers stay countable.
    vec2 d = texCoord * 2.0 - 1.0;
    float r = length(d);
    if (r > 1.0) discard;

    float edge = smoothstep(1.0, 0.75, r);
    float ring = smoothstep(0.55, 0.75, r) * 0.6 + 0.4;

    outColor = vec4(color * ring, edge * 0.85);
}
