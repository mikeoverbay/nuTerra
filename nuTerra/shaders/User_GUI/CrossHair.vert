//
// Cross Hair Vertex Program
//
#version 450 core

layout(location = 0) in vec3 vertexPosition;
layout(location = 1) in vec3 vertexNormal; //unused
layout(location = 2) in vec2 vertexTexCoord1;

uniform mat4 ProjectionMatrix;
uniform float time;
out vec2 UV;

void main(void)
{
    gl_Position = ProjectionMatrix * vec4(vertexPosition, 1.0);
    UV = vertexTexCoord1;
	UV.y += time;
}
