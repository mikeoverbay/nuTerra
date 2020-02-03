// gWriter vertex Shader. We will use this as a template for other shaders
#version 430 compatibility

layout(location = 0) in vec3 vertex_in;
layout(location = 1) in vec3 normal_in;
layout(location = 2) in vec2 uv1_in;
layout(location = 3) in vec3 tangent_in;
layout(location = 4) in vec3 Binormal_in;
layout(location = 5) in vec2 uv2_in;

uniform mat4 ModelMatrix;
uniform mat4 ProjectionMatrix;

out vec2 UV;
out vec2 UV2;
out vec3 Vertex_Normal;
out vec3 v_Position;
out mat3 TBN;

void main(void)
{
    UV = uv1_in;
    UV2 = uv2_in;

    Vertex_Normal = mat3( transpose(inverse(ModelMatrix) ) ) * normal_in;
    v_Position = vec3(ModelMatrix * vec4(vertex_in,1.0));

	TBN = mat3( normalize(tangent_in), normalize(Binormal_in), normalize(Vertex_Normal));

    gl_Position = ProjectionMatrix * vec4(vertex_in,1.0);
}
