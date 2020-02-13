// gWriter vertex Shader. We will use this as a template for other shaders
#version 430 core

layout(location = 0) in vec3 vertexPosition;
layout(location = 1) in vec4 vertexNormal;
layout(location = 2) in vec2 vertexTexCoord1;
layout(location = 3) in vec4 vertexTangent;
layout(location = 4) in vec4 vertexBinormal;
layout(location = 5) in vec2 vertexTexCoord2;
layout(location = 6) in mat4 instanceMatrix;

uniform mat4 view;
uniform mat4 projection;

out vec2 UV;
out vec2 UV2;
out vec3 worldPosition;
out mat3 TBN;
out vec3 normal;

void main(void)
{
    UV =  vertexTexCoord1;
    UV2 = vertexTexCoord2;

    // Transform position & normal to world space
    worldPosition = vec3(instanceMatrix * vec4(vertexPosition, 1.0));

    mat3 normalMatrix = mat3(transpose(inverse(view * instanceMatrix)));

    // Tangent, biNormal and Normal must be trasformed by the normal Matrix.
    vec3 worldTangent = normalMatrix * vertexTangent.xyz;
    vec3 worldbiNormal = normalMatrix * vertexBinormal.xyz;
    vec3 worldNormal = normalMatrix * vertexNormal.xyz;
    worldTangent = normalize(worldTangent - dot(worldNormal, worldTangent) * worldNormal);
    worldbiNormal = normalize(worldbiNormal - dot(worldNormal, worldbiNormal) * worldNormal);

	normal = worldNormal;// temp for lightitng debug

    // Create the Tangent, BiNormal, Normal Matrix for transforming the normalMap.
    TBN = mat3( normalize(worldTangent), normalize(worldbiNormal), normalize(worldNormal));

    // Calculate vertex position in clip coordinates
    gl_Position = projection * view * instanceMatrix * vec4(vertexPosition, 1.0f);
}
