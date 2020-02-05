// colorOnly_vertex. Only writes a color to the gColor texture
#version 430 compatibility
layout(location = 0) in vec3 vertex_in;

uniform mat4 ProjectionMatrix;


void main(void)
{

    gl_Position = ProjectionMatrix * vec4(vertex_in,1.0);
}
