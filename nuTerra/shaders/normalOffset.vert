// Normal shader .. shows the normal,tangent and biNormal vectors and wire overlay

#version 430 compatibility

out vec2 texcoord;
void main(void)
{
  gl_Position = ftransform();
  texcoord = gl_MultiTexCoord0.xy;
}
