#version 450 core

layout (location = 0) out vec4 gColor;
layout (location = 1) out vec4 gGMF;

in GS_OUT {
    flat vec4 color;
} fs_in;

void main()
{
    gColor = fs_in.color;
	gGMF = vec4(0.0);
}
