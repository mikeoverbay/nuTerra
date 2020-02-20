﻿#version 430 core

layout (location = 0) out vec4 gColor;
layout (location = 2) out vec4 gGMF;

uniform sampler2D imageMap;

in vec2 texCoord;

void main(void)
{
    gColor = texture(imageMap, texCoord);
    gGMF = vec4(0.0, 0.0, 0.0, 0.0 ); //First to render and we want no lighting
}
