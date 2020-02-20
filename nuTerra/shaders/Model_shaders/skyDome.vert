// gWriter vertex Shader. We will use this as a template for other shaders
#version 430 core

layout(location = 0) in vec3 vertexPosition;
//layout(location = 1) in vec4 vertexNormal;
layout(location = 2) in vec2 vertexTexCoord1;

uniform mat4 model;
uniform mat4 view;
uniform mat4 projection;

out vec2 texCoord;


void main(void)
{
    texCoord =  vertexTexCoord1;

    gl_Position = projection * view * model * vec4(vertexPosition, 1.0f);
}
