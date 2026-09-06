#version 450 core

// The 3D cursor and the ground grid in the lamp inspector. Lines only.

layout(location = 0) in vec3 vPos;

uniform mat4 mvp;

void main(void)
{
    gl_Position = mvp * vec4(vPos, 1.0);
}
