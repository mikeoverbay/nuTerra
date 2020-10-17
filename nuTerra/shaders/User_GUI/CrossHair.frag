//
// Cross Hair Fragment Program
//
#version 450 core

layout (location = 0) out vec4 gColor;
layout (location = 2) out vec4 gGMF;

uniform sampler2D colorMap;
uniform vec4 shade;

in vec2 UV;

void main(void){

	vec4 color = texture(colorMap, UV * 5.0);
	gColor = color * shade;
	gGMF = vec4(0.0);
}