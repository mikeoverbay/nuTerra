#version 430 core

out vec4 fragColor;
uniform sampler2D imageMap;
uniform vec4 color;
in vec2 texCoord;

void main(void)
{
    fragColor = texture(imageMap, texCoord) * color;
}
