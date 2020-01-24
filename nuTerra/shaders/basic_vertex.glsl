//Basic Shader for testing shit

#version 130

out vec4 color;

void main(void)
{
  gl_Position = ftransform();
  color = gl_Color;
}
