// gWriter vertex Shader. We will use this as a template for other shaders
#version 430 core

layout(location = 0) in vec2 vertexXZ;
layout(location = 1) in float vertexY;
layout(location = 2) in vec2 vertexTexCoord;
layout(location = 3) in vec4 vertexNormal;
layout(location = 4) in int vertexhole;

uniform mat4 viewModel;
uniform mat4 projection;
uniform mat3 normalMatrix;

out mat3 TBN;
out vec3 worldPosition;
out vec2 UV;
flat out float is_hole;

void main(void)
{
    UV =  vertexTexCoord;
    is_hole = vertexhole;

	vec3 vertexPosition = vec3(vertexXZ.x, vertexY, vertexXZ.y);
    vec3 tangent;

    vec3 c1 = cross(vertexNormal.xyz, vec3(0.0, 0.0, 1.0));
    vec3 c2 = cross(vertexNormal.xyz, vec3(0.0, 1.0, 0.0));

    if( length(c1) > length(c2) )
        {
            tangent = normalize(c1);
        }
        else
        {
            tangent = normalize(c2);
        }

    tangent = normalize(tangent - dot(vertexNormal.xyz, tangent) * vertexNormal.xyz);

    vec3 bitangent = cross(tangent, vertexNormal.xyz);

    worldPosition = vec3(viewModel * vec4(vertexPosition, 1.0f));

    // Tangent, biNormal and Normal must be trasformed by the normal Matrix.
    vec3 worldNormal = normalMatrix * vertexNormal.xyz;
    vec3 worldTangent = normalMatrix * tangent;
    vec3 worldbiNormal = normalMatrix * bitangent;

    //make perpendicular
    worldTangent = normalize(worldTangent - dot(worldNormal, worldTangent) * worldNormal);
    worldbiNormal = normalize(worldbiNormal - dot(worldNormal, worldbiNormal) * worldNormal);

    // Create the Tangent, BiNormal, Normal Matrix for transforming the normalMap.
    TBN = mat3( normalize(worldTangent), normalize(worldbiNormal), normalize(worldNormal));

    // Calculate vertex position in clip coordinates
    gl_Position = projection * viewModel * vec4(vertexPosition, 1.0f);
}
