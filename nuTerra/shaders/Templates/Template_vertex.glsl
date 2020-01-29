// Comment here
#version 430 compatibility

out vec4 color;
out vec2 UV;
out vec3 n;

void main(void)
{
	UV = gl_MultiTexCoord0.xy;
	n = gl_Normal;
	gl_Position = gl_ProjectionMatrix * gl_ModelViewMatrix * gl_Vertex;
	color = gl_Color;
}
