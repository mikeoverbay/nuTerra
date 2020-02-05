// gWriter vertex Shader. We will use this as a template for other shaders
#version 430 compatibility

uniform int has_uv2;

uniform mat4 modelMatrix;
uniform mat3 modelNormalMatrix;
uniform mat4 modelViewProjection;

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

    if (has_uv2 == 1) {
        UV2 = gl_MultiTexCoord3.xy;
    }

    Vertex_Normal = modelNormalMatrix * gl_Normal;
    v_Position = vec3(modelMatrix * gl_Vertex);

	TBN = mat3( normalize(tang), normalize(binorm), normalize(Vertex_Normal));

    gl_Position = modelViewProjection * gl_Vertex;
}
