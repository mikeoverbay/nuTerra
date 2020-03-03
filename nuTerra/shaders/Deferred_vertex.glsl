// Deferred lighting vertex shader.
#version 430 compatibility

uniform mat4 ModelMatrix;
uniform mat4 ProjectionMatrix;

out vec2 UV;
out mat4 projMatrixInv;
out mat4 ModelMatrixInv;

void main(void)
{
	UV = gl_MultiTexCoord0.xy;
	gl_Position = ProjectionMatrix * ModelMatrix * gl_Vertex;
	projMatrixInv = inverse(ProjectionMatrix);
	ModelMatrixInv = inverse(ModelMatrix);
}
