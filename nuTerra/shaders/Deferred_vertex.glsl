// Deferred lighting vertex shader.
#version 430 core

layout(location = 0) in vec2 vertexPosition;
layout(location = 1) in vec2 vertexTexCoord;

uniform mat4 ModelMatrix;
uniform mat4 ProjectionMatrix;

out vec2 UV;
out mat4 projMatrixInv;
out mat4 ModelMatrixInv;

void main(void)
{
	UV = vertexTexCoord;
	gl_Position = ProjectionMatrix * ModelMatrix * vec4(vertexPosition, 0.0, 1.0);
	projMatrixInv = inverse(ProjectionMatrix);
	ModelMatrixInv = inverse(ModelMatrix);
}
