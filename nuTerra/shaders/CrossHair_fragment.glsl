//
// Cross Hair Fragment Program
//
#version 430 core

layout (location = 0) out vec4 gColor;

uniform sampler2D colorMap;
uniform vec4 shade;

in vec2 UV;

void main(void){

	vec4 color = texture(colorMap,UV * 5.0);
	gColor = color * shade;

}