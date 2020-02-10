#version 430 core

layout (location = 0) out vec4 gColor;

in vec4 color;

void main()
{
    gColor = color;
}
