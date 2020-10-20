#version 450 core

layout (location = 0) out vec4 gColor;
layout (location = 1) out vec4 gGMF;

uniform vec3 color;
in vec3 worldPosition;

void main(void)
{
	// easy.. just transfer the values to the gBuffer Textures and calculate perturbed normal;
	gColor.xyz = color;
	gColor.a = 1.0;
	gGMF = vec4(0.0);
}
