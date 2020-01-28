//
//gWriter vertex Shader. We will use this as a template for other shaders
//
#version 130
#extension GL_ARB_gpu_shader5 : enable
uniform mat4 ModelMatrix;
uniform mat4 ProjectionMatrix;

out vec2 UV;
out vec3 Vertex_Normal;
out vec3 v_Position;

void main(void)
{
    UV = gl_MultiTexCoord0.xy;

    Vertex_Normal = mat3( transpose(inverse(ModelMatrix) ) ) * gl_Normal;
    v_Position = vec3(ModelMatrix * gl_Vertex);

    gl_Position = ProjectionMatrix * gl_Vertex;
}
