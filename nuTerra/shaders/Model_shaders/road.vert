#version 450 core

#extension GL_ARB_bindless_texture : require
#extension GL_ARB_shading_language_include : require

#define USE_PERVIEW_UBO
#include "common.h" //! #include "../common.h"

// Road patches come out of road_map.bin already in world space.
layout(location = 0) in vec3 vertexPosition;
layout(location = 1) in vec3 vertexNormal;
layout(location = 2) in vec2 vertexTexCoord;
layout(location = 3) in vec4 vertexColour;
layout(location = 4) in uvec2 vertexTexHandle;

out VS_OUT
{
    vec2 TC;
    vec3 worldPosition;
    vec3 normal;
    vec4 colour;
    flat uvec2 texHandle;
} vs_out;

void main(void)
{
    vs_out.TC = vertexTexCoord;
    vs_out.colour = vertexColour;
    vs_out.texHandle = vertexTexHandle;

    vec4 viewPos = view * vec4(vertexPosition, 1.0);
    vs_out.worldPosition = viewPos.xyz;
    vs_out.normal = mat3(view) * vertexNormal;

    gl_Position = projection * viewPos;
}
