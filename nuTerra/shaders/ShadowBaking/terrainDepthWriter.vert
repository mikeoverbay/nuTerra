#version 450 core


layout(location = 0) in vec2 vertexXZ;
layout(location = 1) in float vertexY;

uniform mat4 Ortho_Project;
out vec4 v_position;

void main(void)
{
    vec4 vp = vec4(vertexXZ.x, vertexY, vertexXZ.y, 1.0);
    gl_Position = Ortho_Project * vec4(vp.x, vp.y, vp.z, 1.0);
    v_position = gl_Position;

}
