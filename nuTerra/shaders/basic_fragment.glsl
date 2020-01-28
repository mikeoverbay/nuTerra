// Basic Shader for testing shit
#version 430 compatibility

layout (location = 0) out vec4 gColor;

uniform sampler2D colorMap;

in vec2 UV;
in vec4 color;
in vec3 n;

void main (void)
{
    vec4 text_color = texture(colorMap, UV);
    gColor = text_color;//+color*0.2;
    gColor.a = 1.0;
}
