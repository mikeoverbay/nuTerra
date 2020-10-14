
#version 430 core

layout(location = 0) in vec3 vertexPosition;

uniform mat4 ProjectionMatrix;
uniform mat4 ViewMatrix;
uniform mat4 ModelMatrix;

out mat4 inverseProject;
out vec4 positionSS;


void main(void)
{
    gl_Position =  ProjectionMatrix * ViewMatrix * ModelMatrix * vec4(vertexPosition.xyz, 1.0);

    positionSS = gl_Position;

    inverseProject = inverse(ProjectionMatrix * ViewMatrix);
}


