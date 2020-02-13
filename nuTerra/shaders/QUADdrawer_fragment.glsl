// draws a quad from a single vertex in

#version 330 core

out vec4 color_out;
uniform sampler2D colorMap;

in vec2 TexCoord;

void main(void){

color_out = texture(colorMap, TexCoord.st);

}

