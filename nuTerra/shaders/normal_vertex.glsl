// Normal shader .. shows the normal,tangent and biNormal vectors
#version 430 core

layout(location = 0) in vec3 vertexPosition;
layout(location = 1) in vec3 vertexNormal;
layout(location = 2) in vec2 vertexTexCoord1;
layout(location = 3) in vec3 vertexTangent;
layout(location = 4) in vec3 vertexBinormal;
layout(location = 5) in vec2 vertexTexCoord2;

out vec3 n;      
out vec3 t;      
out vec3 b;      

void main(void)
{
    // Calculate vertex position in clip coordinates
    gl_Position =  vec4(vertexPosition, 1.0);
    n           = normalize(vertexNormal);
    t           = normalize(vertexTangent);
    b           = normalize(vertexBinormal);
    t           = normalize(t-dot(n,t)*n);
    b           = normalize(b-dot(n,b)*n);
}