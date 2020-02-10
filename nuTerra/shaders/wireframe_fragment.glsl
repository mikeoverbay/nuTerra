// gWriter fragment Shader. We will use this as a template for other shaders
#version 430 core

layout (location = 0) out vec4 gColor;

uniform vec3 color;

void main(void)
{
	gColor = vec4(color, 1.0f);
}
