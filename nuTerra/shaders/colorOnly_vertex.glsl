// colorOnly_vertex. Only writes a color to the gColor texture
#version 430 compatibility
layout(location = 0) in vec3 vertex_in;
layout(location = 1) in vec3 normal_in;

uniform mat4 ModelMatrix;
uniform mat4 ProjectionMatrix;

out vec3 Vertex_Normal;

void main(void)
{
    Vertex_Normal = mat3( transpose(inverse(ModelMatrix) ) ) * normal_in;

    gl_Position = ProjectionMatrix * vec4(vertex_in,1.0);
}
