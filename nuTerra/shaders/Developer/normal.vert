﻿// Normal shader .. shows the normal,tangent and biNormal vectors and wire overlay
#version 430 core

layout(location = 0) in vec3 vertexPosition;
layout(location = 1) in vec4 vertexNormal;
layout(location = 2) in vec2 vertexTexCoord1;
layout(location = 3) in vec4 vertexTangent;
layout(location = 4) in vec4 vertexBinormal;
layout(location = 5) in vec2 vertexTexCoord2;
layout(location = 6) in mat4 instanceModelView;

uniform mat4 projection;

out vec3 n;
out vec3 t;
out vec3 b;

void main(void)
{
    // Calculate vertex position in clip coordinates
    gl_Position = projection * instanceModelView * vec4(vertexPosition, 1.0);

    // Should be mat3(transpose(inverse(instanceModelView))), but it's very slow
    mat3 normalMatrix = mat3(instanceModelView);

    n = normalize(vec3(projection * vec4(normalMatrix * vertexNormal.xyz, 0.0f)));
    t = normalize(vec3(projection * vec4(normalMatrix * vertexTangent.xyz, 0.0f)));
    b = normalize(vec3(projection * vec4(normalMatrix * vertexBinormal.xyz, 0.0f)));
    //Make angles perpendicular
    t           = t-dot(n,t)*n;
    b           = b-dot(n,b)*n;
}
