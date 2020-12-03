#version 450 core

#extension GL_ARB_shading_language_include : require

#define USE_PERVIEW_UBO
#include "common.h" //! #include "../common.h"


layout (binding = 0) uniform sampler2D colorMap;
layout (binding = 1) uniform sampler2D glassMap;

uniform float BRIGHTNESS;
out vec4 blend;

in VS_OUT {
    vec2 UV;
} fs_in;

void main(void){

vec4 color = texture(colorMap, fs_in.UV);
vec4 glass = texture(glassMap, fs_in.UV);
blend.rgb = mix(color.rgb, glass.rgb, glass.a* BRIGHTNESS);
blend.a = color.a;
}