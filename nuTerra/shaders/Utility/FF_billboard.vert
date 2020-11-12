#version 450 core

#extension GL_ARB_shading_language_include : require

#define USE_PERVIEW_UBO
#include "common.h" //! #include "../common.h"

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

    texCoord   = uv;

    vec4 p = view * ( matrix[3] + vec4(cameraPos, 0.0) ) ;
    p += vec4(co * scale * scale *0.5, 0.0f, 1.0f);
    
    p = inverse(view) * p ;
    //p = viewProj * p;

    gl_Position =  viewProj * p;


}
