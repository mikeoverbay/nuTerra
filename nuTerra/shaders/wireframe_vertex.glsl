#version 430 core

layout(location = 0) in vec3 vertexPosition;

uniform mat4 modelViewProjection;
uniform mat4 modelMatrix;

out vec3 worldPosition;

void main(void)
{
    gl_Position = modelViewProjection * vec4(vertexPosition, 1.0);
	worldPosition = vec3(modelMatrix * vec4(vertexPosition, 1.0));
}
