#version 430 compatibility

layout (location = 0) out vec4 gColor;
layout (location = 3) out vec3 gPosition;

in vec4 color;
in vec3 WorldPosition;
void main()
{
	gColor = color;
	gPosition = WorldPosition;
}