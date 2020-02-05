// Comment here
#version 430 compatibility

out vec4 color;
out vec2 UV;
out vec3 n;

uniform mat4 modelViewProjection;

void main(void)
{
	UV = gl_MultiTexCoord0.xy;
	n = gl_Normal;
	gl_Position = modelViewProjection * gl_Vertex;
	color = gl_Color;
}
