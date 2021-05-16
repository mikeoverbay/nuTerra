#version 450 core

#extension GL_ARB_shading_language_include : require

#define USE_PERVIEW_UBO
#include "common.h" //! #include "../common.h"

uniform vec4 rect;
uniform mat4 ProjectionMatrix;

out flat mat4 shadowMatrix;

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

#ifdef SHADOW_MAPPING
    const mat4 biasMatrix = mat4(0.5, 0.0, 0.0, 0.0,
        0.0, 0.5, 0.0, 0.0,
        0.0, 0.0, -0.5, 0.0,
        0.5, 0.5, 0.5, 1.0);
    shadowMatrix = biasMatrix * light_vp_matrix * invView;
#endif

    gl_Position = ProjectionMatrix * vec4(co, 0.0f, 1.0f);
}
