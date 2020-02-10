// gWriter fragment Shader. We will use this as a template for other shaders
#version 430 core

layout (location = 0) out vec4 gColor;
layout (location = 3) out vec3 gPosition;

uniform vec3 color;
in vec3 worldPosition;

void main(void)
{
	// easy.. just transfer the values to the gBuffer Textures and calculate perturbed normal;
	gColor.xyz = color;
	gColor.a = 1.0;
	gPosition = worldPosition;
}
