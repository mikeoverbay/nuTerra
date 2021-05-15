#version 450 core

#extension GL_ARB_shading_language_include : require

#define USE_PERVIEW_UBO
#include "common.h" //! #include "../common.h"

layout(location = 0) in vec2 vertexPosition;

layout(location = 0) out VS_OUT {
    vec3 vertexPosition;
    vec3 vertexNormal;
    vec2 UV;
} vs_out;

void main(void)
{
    vs_out.UV =  vertexPosition.xy +50.0;
    vec3 pos;
    pos.xz = vertexPosition.xy * 10;
    pos.y = 0.0;// this will come from height map

    vs_out.vertexPosition = pos;

    // this will come from normal texture
    //vs_out.vertexNormal = vertexNormal.xyz;
    gl_Position = viewProj * vec4(pos, 1.0);

}
