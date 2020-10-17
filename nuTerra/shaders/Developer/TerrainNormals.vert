// Normal shader .. shows the normal,tangent and biNormal vectors and wire overlay
#version 450 core

#extension GL_ARB_shading_language_include : require
#include "common.h"

layout(location = 0) in vec2 vertexXZ;
layout(location = 1) in float vertexY;
layout(location = 2) in vec2 vertexTexCoord;
layout(location = 3) in vec4 vertexNormal;

uniform mat4 model;

layout (binding = PER_FRAME_DATA_BASE, std140) uniform PerView {
    mat4 view;
    mat4 projection;
    mat4 viewProj;
    mat4 invViewProj;
    vec3 cameraPos;
};

out vec3 n;

void main(void)
{
    vec3 vertexPosition = vec3(vertexXZ.x, vertexY, vertexXZ.y);
    
    // Calculate vertex position in clip coordinates
    gl_Position = viewProj * model * vec4(vertexPosition, 1.0);

    mat3 normalMatrix = mat3(transpose(inverse(view * model)));

  	// NOTE: vertexNormal is already normalized in the VBO.
    n = normalize(vec3(projection * vec4(normalMatrix * vertexNormal.xyz, 0.0f)));
}
