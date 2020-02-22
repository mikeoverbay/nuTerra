// gWriter vertex Shader. We will use this as a template for other shaders
#version 430 core

layout(location = 0) in vec3 vertexPosition;
layout(location = 1) in vec2 vertexTexCoord;
layout(location = 2) in vec3 vertexNormal;
layout(location = 3) in float hole;


uniform mat4 model;
uniform mat4 view;
uniform mat4 projection;

out vec3 worldPosition;
out vec2 UV;
out vec3 normal;
flat out float is_hole;

void main(void)
{
    UV =  vertexTexCoord;
	is_hole = hole;

	worldPosition = vec3(view * model * vec4(vertexPosition, 1.0));

    mat3 normalMatrix = mat3(transpose(inverse(view * model)));

    normal = normalMatrix * vertexNormal.xyz;

    // Calculate vertex position in clip coordinates
    gl_Position = projection * view * model * vec4(vertexPosition, 1.0f);
}
