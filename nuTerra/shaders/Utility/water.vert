#version 450 core

#extension GL_ARB_shading_language_include : require

#define USE_PERVIEW_UBO
#include "common.h" //! #include "../common.h"

// The water mesh comes straight out of BWWa - already world space, X already
// mirrored at load like everything else. No model matrix.
layout(location = 0) in vec3 vertexPosition;
layout(location = 1) in float vertexAux;

out VS_OUT
{
    vec3 worldPos;   // genuinely world space, for once
    float aux;
} vs_out;

// Viewer-side height trim, saved per map. The authored heights are exact -
// this exists for judging fit against boats and shorelines by eye.
uniform float water_y_offset;

void main(void)
{
    vec3 p = vertexPosition;
    p.y += water_y_offset;
    vs_out.worldPos = p;
    vs_out.aux = vertexAux;
    gl_Position = viewProj * vec4(p, 1.0);
}
