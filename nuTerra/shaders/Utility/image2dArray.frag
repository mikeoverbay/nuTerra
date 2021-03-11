﻿#version 450 core

out vec4 fragColor;
uniform sampler2DArray imageMap;
uniform int id;
in vec2 texCoord;

void main(void)
{
    fragColor.r = texture( imageMap, vec3( texCoord,float(id) ) ).r*0.5;
}