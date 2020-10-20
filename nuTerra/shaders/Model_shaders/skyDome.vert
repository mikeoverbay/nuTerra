#version 450 core

layout(location = 0) in vec3 vertexPosition;
layout(location = 2) in vec2 vertexTexCoord1;

uniform mat4 mvp;

out vec2 texCoord;

void main(void)
{
    texCoord =  vertexTexCoord1;
    gl_Position = mvp * vec4(vertexPosition, 1.0f);
}
