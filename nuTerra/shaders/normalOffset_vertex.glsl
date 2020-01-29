#version 330 compatibility

out vec2 texcoord;
void main(void)
{
  gl_Position = ftransform();
  texcoord = gl_MultiTexCoord0.xy;
}
