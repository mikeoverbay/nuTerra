// Draws a quad from a single vertex input.

#version 330 core

layout(location = 0) in vec3 vertexPosition;

uniform mat4 ProjectionMatrix;

void main(void)
{
    gl_Position = ProjectionMatrix * vec4(vertexPosition, 1.0);

}
