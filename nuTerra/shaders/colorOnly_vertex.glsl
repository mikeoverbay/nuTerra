//
//colorOnly_vertex. Only writes a color to the gColor texture
//
#version 130
#extension GL_ARB_gpu_shader5 : enable
uniform mat4 ModelMatrix;
uniform mat4 ProjectionMatrix;

out vec2 UV;
out vec3 Vertex_Normal;


void main(void)
{
    UV = gl_MultiTexCoord0.xy;
	Vertex_Normal = mat3( transpose(inverse(ModelMatrix) ) ) * gl_Normal;

    gl_Position = ProjectionMatrix * gl_Vertex;
}
