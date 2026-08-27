#version 450 core

#extension GL_ARB_shading_language_include : require

#define USE_PERVIEW_UBO
#include "common.h" //! #include "../common.h"

// Wireframe view of the outland sheet - identical displacement to
// outland.vert, lifted a touch so the lines win the depth fight against the
// fill surface (the terrain wire path does the same via its normal offset).

layout(location = 0) in vec2 vertexPosition;
layout(location = 1) in vec2 UVs;

layout(binding = 1) uniform sampler2D height_map;

uniform float y_range;
uniform float y_offset;

uniform vec2 scale;
uniform vec2 center_offset;

void main(void)
{
    vec2 UV = -UVs;

    vec3 pos;
    pos.xz = vertexPosition.xy * scale;
    pos.xz += center_offset;
    pos.y = texture(height_map, UV).x * y_range + y_offset - 1.5 + 0.2;

    gl_Position = viewProj * vec4(pos, 1.0);
}
