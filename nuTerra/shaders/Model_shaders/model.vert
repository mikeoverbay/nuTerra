// gWriter vertex Shader. We will use this as a template for other shaders
#version 450 core

#extension GL_ARB_shader_draw_parameters : require

layout(location = 0) in vec3 vertexPosition;
layout(location = 1) in vec4 vertexNormal;
layout(location = 2) in vec2 vertexTexCoord1;

layout(binding = 0, std140) readonly buffer MODEL_MATRIX_BLOCK
{
    mat4 model_matrix[];
};

uniform mat4 projection;
uniform mat4 view;

out vec2 UV;
out vec2 UV2;
out vec3 worldPosition;
out mat3 TBN;
out vec3 normal;

void main(void)
{
    UV =  vertexTexCoord1;
    //UV2 = vertexTexCoord2;

    mat4 instanceModelView = view * model_matrix[gl_BaseInstance];

    // Transform position & normal to world space
    worldPosition = vec3(instanceModelView * vec4(vertexPosition, 1.0));

    // Should be mat3(transpose(inverse(instanceModelView))), but it's very slow
    mat3 normalMatrix = mat3(instanceModelView);

    // Tangent, biNormal and Normal must be trasformed by the normal Matrix.
    //vec3 worldTangent = normalMatrix * vertexTangent.xyz;
    //vec3 worldbiNormal = normalMatrix * vertexBinormal.xyz;
    vec3 worldNormal = normalMatrix * vertexNormal.xyz;
    //worldTangent = normalize(worldTangent - dot(worldNormal, worldTangent) * worldNormal);
    //worldbiNormal = normalize(worldbiNormal - dot(worldNormal, worldbiNormal) * worldNormal);

	normal = worldNormal;// temp for lightitng debug

    // Create the Tangent, BiNormal, Normal Matrix for transforming the normalMap.
    TBN = mat3(worldNormal, worldNormal, normalize(worldNormal));

    // Calculate vertex position in clip coordinates
    gl_Position = projection * instanceModelView * vec4(vertexPosition, 1.0f);
}
