// colorOnly_vertex. Only writes a color to the gColor texture
#version 430 core

layout(location = 0) in vec3 Vertex;

uniform mat4 ProjectionMatrix;
uniform mat4 ModelMatrix;

out vec3 worldPosition;
void main(void)
{
    gl_Position = ProjectionMatrix * vec4(Vertex, 1.0);
	worldPosition = vec3(ModelMatrix * vec4(Vertex,1.0));
}
