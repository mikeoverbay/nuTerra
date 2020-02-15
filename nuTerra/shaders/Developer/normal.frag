#version 430 core

layout (location = 0) out vec4 gColor;
layout (location = 2) out vec4 gGMF;

in vec4 color;

void main()
{
    gColor = color;
	gGMF = vec4(0.0);
}
