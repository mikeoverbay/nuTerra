// gWriter vertex Shader. We will use this as a template for other shaders
#version 430 compatibility

uniform int has_uv2;

uniform mat4 modelMatrix;
uniform mat3 modelNormalMatrix;
uniform mat4 modelViewProjection;

out vec2 UV;
out vec2 UV2;
out mat3 TBN;

void main(void)
{
    UV = gl_MultiTexCoord0.xy;
    vec3 vertexTangent = gl_MultiTexCoord1.xyz;
    vec3 vertexBinormal = gl_MultiTexCoord2.xyz;

    if (has_uv2 == 1) {
        UV2 = gl_MultiTexCoord3.xy;
    }

    // Tangent, biNormal and Normal must be trasformed by the normal Matrix.
    vec3 worldTangent = modelNormalMatrix  * vertexTangent;
    vec3 worldbiNormal = modelNormalMatrix  * vertexBinormal;
    vec3 worldNormal =  modelNormalMatrix  * gl_Normal;

    // Create the Tangent, BiNormal, Normal Matrix for transforming the normalMap.
    TBN = mat3( normalize(worldTangent), normalize(worldbiNormal), normalize(worldNormal));

    gl_Position = modelViewProjection * gl_Vertex;
}
