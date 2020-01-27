//Comment here

#version 130

out vec4 color;
varying vec2 UV;
out vec3 n;
void main(void)
{
  UV = gl_MultiTexCoord0.xy;
  n = gl_Normal;
  gl_Position = ftransform();
  color = gl_Color;
}
