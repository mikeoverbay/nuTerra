#version 450 core

#extension GL_ARB_shading_language_include : require

#define USE_PERVIEW_UBO
#include "common.h" //! #include "../common.h"

uniform vec3 center;    // world position of this marker
uniform float size;     // half extent, world units

out vec2 texCoord;

void main(void)
{
    // Corner from gl_VertexID, as a triangle strip: 0=TL 1=BL 2=TR 3=BR
    vec2 co;
    if      (gl_VertexID == 0) { co = vec2(-1.0,  1.0); texCoord = vec2(0.0, 1.0); }
    else if (gl_VertexID == 1) { co = vec2(-1.0, -1.0); texCoord = vec2(0.0, 0.0); }
    else if (gl_VertexID == 2) { co = vec2( 1.0,  1.0); texCoord = vec2(1.0, 1.0); }
    else                       { co = vec2( 1.0, -1.0); texCoord = vec2(1.0, 0.0); }

    // Camera right and up straight out of invView's basis, so the quad faces the
    // viewer without needing the billboard built in view space and brought back.
    vec3 right = vec3(invView[0][0], invView[0][1], invView[0][2]);
    vec3 up    = vec3(invView[1][0], invView[1][1], invView[1][2]);

    vec3 world = center + (right * co.x + up * co.y) * size;
    gl_Position = viewProj * vec4(world, 1.0);
}
