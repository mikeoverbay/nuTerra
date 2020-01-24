//Basic Shader for testing shit

#version 330 compatibility

out vec4 gColor;

in vec4 color;

void main (void)
{
            gColor = color;
}
