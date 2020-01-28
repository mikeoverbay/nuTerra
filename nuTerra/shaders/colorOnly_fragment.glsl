//
//gWriter fragment Shader. We will use this as a template for other shaders
//
#version 330 compatibility
layout (location = 0) out vec4 gColor;
layout (location = 1) out vec3 gNormal;

uniform vec3 color;

in vec2 UV;
in vec3 Vertex_Normal;

////////////////////////////////////////////////////////////////
void main(void)
{
// easy.. just transfer the values to the gBuffer Textures and calculate perturbed normal;
gColor.xyz = color;
gColor.a = 1.0;

gNormal.xyz = Vertex_Normal;
}
