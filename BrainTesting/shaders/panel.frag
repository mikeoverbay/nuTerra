#version 450 core

in vec2 uv;
out vec4 fragColour;

uniform sampler2D source;
uniform float fade;

void main(void)
{
    vec4 c = texture(source, uv);

    // FULLY TRANSPARENT TEXELS ARE DISCARDED rather than blended. A label
    // standing in the world is mostly empty, and blending that emptiness still
    // writes it - so a panel in front of another panel punches a rectangle of
    // nothing through it. Discarding leaves the hole unwritten.
    if (c.a * fade <= 0.004) discard;

    fragColour = vec4(c.rgb, c.a * fade);
}
