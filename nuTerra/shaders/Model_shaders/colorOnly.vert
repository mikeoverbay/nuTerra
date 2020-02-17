// colorOnly_vertex. Only writes a color to the gColor texture
#version 430 core

layout(location = 0) in vec3 Vertex;

uniform mat4 ProjectionMatrix;

void main(void)
{
    gl_Position = ProjectionMatrix * vec4(Vertex, 1.0);
}
