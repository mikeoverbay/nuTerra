#version 450 core
layout(location = 0) in vec3 vertexPosition;
uniform mat4 viewProj;
void main(void) { gl_Position = viewProj * vec4(vertexPosition, 1.0); }
