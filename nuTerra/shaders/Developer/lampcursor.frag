#version 450 core

uniform vec4 line_color;

out vec4 outColor;

void main(void)
{
    outColor = line_color;
}
