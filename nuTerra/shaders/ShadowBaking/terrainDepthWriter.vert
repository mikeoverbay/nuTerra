#version 450 core


layout(location = 0) in vec2 vertexXZ;
layout(location = 1) in float vertexY;
layout(location = 2) in vec2 vertexTexCoord;
layout(location = 3) in vec4 vertexNormal;
layout(location = 4) in vec3 vertexTangent;

uniform mat4 Ortho_Project;
out vec2 uv;

void main(void)
{
    uv = vertexTexCoord;
    vec4 vp = vec4(vertexXZ.x, vertexY, vertexXZ.y, 1.0);
    gl_Position = Ortho_Project * vec4(vp.x, vp.y, vp.z, 1.0);

}
