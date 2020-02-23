// Normal shader .. shows the normal,tangent and biNormal vectors and wire overlay
#version 430 core

layout(location = 0) in vec3 vertexPosition;
layout(location = 1) in vec2 vertexTexCoord1;
layout(location = 2) in vec3 vertexNormal;

uniform mat4 model;
uniform mat4 view;
uniform mat4 projection;

out vec3 n;


void main(void)
{
    // Calculate vertex position in clip coordinates
    gl_Position = projection * view * model * vec4(vertexPosition, 1.0);

    mat3 normalMatrix = mat3(transpose(inverse(view * model)));

    n = normalize(vec3(projection * vec4(normalMatrix * vertexNormal.xyz, 0.0f)));
}
