#version 450 core

in vec4 color;
in vec2 texCoord;

uniform sampler2D in_fontTexture;

out vec4 outputColor;

void main()
{
    // The atlas is single channel in RGBA form; ImGui multiplies the vertex
    // colour through it, which is what gives text its tint and panels their
    // alpha.
    outputColor = color * texture(in_fontTexture, texCoord);
}
