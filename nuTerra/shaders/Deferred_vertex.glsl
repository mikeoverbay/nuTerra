//
//Deferred lighting vertex shader.
//

#version 130
#extension GL_ARB_gpu_shader5 : enable

uniform mat4 ModelMatrix;
uniform mat4 ProjectionMatrix;

out vec2 UV;
out mat4 projMatrixInv;
out mat4 ModelMatrixInv;

void main(void)
{
  UV = gl_MultiTexCoord0.xy;
  gl_Position = ftransform();
  projMatrixInv = inverse(ProjectionMatrix);
  ModelMatrixInv = inverse(ModelMatrix);
}
