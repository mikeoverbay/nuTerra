// gWriter vertex Shader. We will use this as a template for other shaders
#version 430 core

layout(location = 0) in vec3 vertex_in;
layout(location = 1) in vec3 normal_in;
layout(location = 2) in vec2 uv1_in;

uniform mat4 ModelMatrix;
uniform mat4 ProjectionMatrix;
uniform mat3 modelNormalMatrix;

out vec2 UV;
out vec3 Vertex_Normal;
out vec3 v_Position;

void main(void)
{
    UV = uv1_in;

    Vertex_Normal =  modelNormalMatrix  * normal_in;
    v_Position = vec3(ModelMatrix * vec4(vertex_in, 1.0));
    gl_Position = ProjectionMatrix * vec4(vertex_in, 1.0);
}
