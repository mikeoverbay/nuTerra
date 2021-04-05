#version 450 core


layout(location = 0) in vec3 vertexPosition;
layout(location = 1) in vec2 UV;

uniform mat4 Ortho_Project;
uniform mat4 cameraMat;
uniform mat4 modelMat;

out vec4 v_position;
out vec2 uv;
void main(void)
{
    mat4 modelView = cameraMat * modelMat;

    gl_Position = Ortho_Project * modelView * vec4(vertexPosition, 1.0);
    v_position = gl_Position;
    uv = UV;
}
