#version 450 core

layout(location = 0) in vec3 aPos;
layout(location = 1) in vec2 aUV;
layout(location = 2) in vec3 aNormal;

uniform mat4 uViewProj;

out vec2 vUV;
out vec3 vWorld;
out vec3 vNormal;

void main(void)
{
    vUV = aUV;
    vWorld = aPos;
    vNormal = aNormal;
    gl_Position = uViewProj * vec4(aPos, 1.0);
}
