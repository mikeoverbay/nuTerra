// gWriter vertex Shader. We will use this as a template for other shaders
#version 430 core

layout(location = 0) in vec3 vertexPosition;
layout(location = 1) in vec2 vertexTexCoord;
layout(location = 2) in vec4 vertexNormal;

uniform mat4 viewModel;
uniform mat4 projection;
uniform mat3 normalMatrix;

out vec3 worldPosition;
out vec2 UV;
out vec3 normal;
flat out float is_hole;

void main(void)
{
    UV =  vertexTexCoord;
    is_hole = vertexNormal.w;

    worldPosition = vec3(viewModel * vec4(vertexPosition, 1.0f));

    normal = normalMatrix * vertexNormal.xyz;

    // Calculate vertex position in clip coordinates
    gl_Position = projection * viewModel * vec4(vertexPosition, 1.0f);
}
