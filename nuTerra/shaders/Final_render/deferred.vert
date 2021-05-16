#version 450 core

#extension GL_ARB_shading_language_include : require

#define USE_PERVIEW_UBO
#include "common.h" //! #include "../common.h"

uniform vec4 rect;
uniform mat4 ProjectionMatrix;

void main(void)
{
    vec2 co;

    if (gl_VertexID == 0) {
        co = rect.xw;
    }
    else if (gl_VertexID == 1) {
        co = rect.xy;
    }
    else if (gl_VertexID == 2) {
        co = rect.zw;
    }
    else {
        co = rect.zy;
    }

    gl_Position = ProjectionMatrix * vec4(co, 0.0f, 1.0f);
}
