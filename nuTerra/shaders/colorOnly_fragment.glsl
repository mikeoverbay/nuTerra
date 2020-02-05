// gWriter fragment Shader. We will use this as a template for other shaders
#version 430 compatibility

layout (location = 0) out vec4 gColor;

uniform vec3 color;


void main(void)
{
	// easy.. just transfer the values to the gBuffer Textures and calculate perturbed normal;
	gColor.xyz = color;
	gColor.a = 1.0;
}
