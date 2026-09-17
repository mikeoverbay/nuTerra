#version 450 core

in vec2 uv;
out vec4 fragColour;

uniform sampler2D source;
uniform float fade;           // whole-panel opacity, 0..1

void main(void)
{
    vec4 c = texture(source, uv);

    // THE TARGET IS NOT PREMULTIPLIED. It was cleared to a translucent colour
    // and drawn over with ordinary src-alpha blending, so its rgb is already
    // the colour that should appear - scaling rgb here as well as alpha would
    // darken the scope every time the fade came down, which reads as the
    // background bleeding through the lines rather than as opacity.
    fragColour = vec4(c.rgb, c.a * fade);
}
