//BillBoardBasic
#version 450 core

#extension GL_ARB_shading_language_include : require

#define USE_PERVIEW_UBO
#include "common.h"

uniform mat4 matrix;
uniform vec4 rect;
uniform float scale;
out vec2 texCoord;

void main(void)
{

    vec2 uv;
    vec2 co;

    if (gl_VertexID == 0) {
        co = rect.xw;
        uv = vec2(0.0f, 1.0f);
    }
    else if (gl_VertexID == 1) {
        co = rect.xy;
        uv = vec2(0.0f, 0.0f);
    }
    else if (gl_VertexID == 2) {
        co = rect.zw;
        uv = vec2(1.0f, 1.0f);
    }
    else {
        co = rect.zy;
        uv = vec2(1.0f, 0.0f);
    }

    vec4 vectex = vec4(co * scale, 0.0f, 1.0f);
    vec3 n = vec3 (0.0,0.0,-1.0);
    texCoord   = uv;

    vec4 p =  matrix[3];
    p.xyz -= vectex.xyz;

    p = inverse(matrix) * p;

    gl_Position =  viewProj * matrix  * p;


}
