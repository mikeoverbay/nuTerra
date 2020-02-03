// gWriter vertex Shader. We will use this as a template for other shaders
#version 430 compatibility


uniform mat4 ModelMatrix;
uniform mat4 ProjectionMatrix;

out vec2 UV;
out vec2 UV2;
out vec3 Vertex_Normal;
out vec3 v_Position;
out mat3 TBN;

void main(void)
{
    UV = gl_MultiTexCoord0.xy;
	vec3 tang = gl_MultiTexCoord1.xyz;
	vec3 binorm = gl_MultiTexCoord2.xyz;
    UV2 = gl_MultiTexCoord3.xy;

    Vertex_Normal = mat3( transpose(inverse(ModelMatrix) ) ) * gl_Normal;
    v_Position = vec3(ModelMatrix * gl_Vertex);

	TBN = mat3( normalize(tang), normalize(binorm), normalize(Vertex_Normal));

    gl_Position = ProjectionMatrix * gl_Vertex;
}
