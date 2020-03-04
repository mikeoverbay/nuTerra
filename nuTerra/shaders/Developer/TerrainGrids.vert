//Terrian chunks Markers.. 

#version 430 core

layout(location = 0) in vec2 vertexXZ;
layout(location = 1) in float vertexY;
layout(location = 2) in vec2 vertexTexCoord;


out vec2 uv;
out vec2 V;

uniform mat4 projection;
uniform mat4 view;
uniform mat4 model;

void main(void)
{
    vec4 Vertex = model * vec4(vertexXZ.x, vertexY, vertexXZ.y, 1.0);
    V = Vertex.xz;

    gl_Position = projection * view * Vertex;

    uv = vertexTexCoord.xy;
}
