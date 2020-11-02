#version 450 core

layout(location = 0) in vec3 Vertex;
layout(location = 1) in vec3 Normal;
layout(location = 1) in vec2 uv;

uniform mat4 viewProjMat;
uniform mat4 modelMat;

out vec3 N;
out vec2 UV;
out vec3 FragPos;

void main(void)
{
    gl_Position = viewProjMat * vec4(Vertex, 1.0);
	N = normalize(Normal);
	UV = uv;
	FragPos = Vertex;
}
