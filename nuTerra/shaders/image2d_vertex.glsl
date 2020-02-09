#version 430 core

layout(location = 0) in vec2 vertexPosition;
layout(location = 1) in vec2 vertexTexCoord;

uniform mat4 ProjectionMatrix;

out vec2 texCoord;

void main(void)
{
    texCoord = vertexTexCoord;
    gl_Position = ProjectionMatrix * vec4(vertexPosition, 0.0, 1.0);
}
