#version 450 core

// THE ATLAS IS ImGui's, and it is COVERAGE IN ALPHA - white pixels with the
// glyph shape carried by the alpha channel. So the colour comes entirely from
// the vertex and the texture only says how much of it to lay down. Multiplying
// the rgb as well would work for white text and quietly darken every other
// colour, which is the kind of bug that reads as "the font looks wrong".
in vec2 uv;
in vec4 colour;

uniform sampler2D atlas;

out vec4 fragColour;

void main(void)
{
    float coverage = texture(atlas, uv).a;
    if (coverage <= 0.003) discard;      // keeps blank glyph cells off the depth pass
    fragColour = vec4(colour.rgb, colour.a * coverage);
}
