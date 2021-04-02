#version 450 core

layout (location = 0) out vec4 fragColor;

layout(binding = 0) uniform sampler2D imageMap;

in vec2 texCoord;

void main(void)
{
    fragColor = texture(imageMap, texCoord);
}
