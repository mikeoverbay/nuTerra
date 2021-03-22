#version 450 core

layout(binding = 0) uniform sampler2D imageMap;
out vec4 fragColor;
in vec2 texCoord;

void main(void)
{
    fragColor = texture(imageMap, texCoord);
}
