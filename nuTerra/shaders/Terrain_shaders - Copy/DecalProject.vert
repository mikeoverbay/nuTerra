//Decals color pass.

#version 430 core

layout(location = 0) in vec3 vertexPosition;


uniform mat4 ProjectionMatrix;
uniform mat4 ViewMatrix;
uniform mat4 DecalMatrix;

out mat4 inverseProject;
out mat4 inverseModel;
out vec4 positionSS;


void main(void)
{
    gl_Position =  ProjectionMatrix * ViewMatrix * DecalMatrix * vec4(vertexPosition.xyz, 1.0);

    positionSS = gl_Position;

    inverseProject = inverse(ProjectionMatrix * ViewMatrix);

    inverseModel = inverse(DecalMatrix);

}
