#version 430 core

layout (location = 0) out vec4 gColor;
layout (location = 2) out vec3 gGMF;

uniform sampler2D imageMap;

in vec2 texCoord;

void main(void)
{
    gColor = texture(imageMap, texCoord);
    gGMF = vec3(0.0, 0.0, 1.0 ); //First to render and we want no lighting
}
