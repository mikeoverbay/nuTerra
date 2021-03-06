﻿#version 450 core

#extension GL_ARB_shading_language_include : require

#define USE_PERVIEW_UBO
#include "common.h" //! #include "../common.h"

//uniform sampler2D colorMap;



layout (binding = 1) uniform sampler2D alpha_LUT;

uniform mat4 matrix;
uniform float scale;
uniform float rot_angle;
uniform float frame_index;
uniform float row_length;
uniform float total_frames;

out vec2 texCoord;
flat out float alpha;

void main(void)
{
    vec2 uv;
    vec2 co;
    // Our 4, PI angle points. So we can rotate the quad :)
    const float pi_1 =  3.92699082;
    const float pi_2 =  2.3561945;
    const float pi_3 = -0.7853982;
    const float pi_4 =  0.7853982;

//    ivec2 isize = textureSize(colorMap,0);

    if (gl_VertexID == 0) {
        co.x = sin(rot_angle + pi_1) * scale;
        co.y = cos(rot_angle + pi_1) * scale;

        uv = vec2(0.0f, 0.5f);
    }
    else if (gl_VertexID == 1) {
        co.x = sin(rot_angle + pi_2) * scale;
        co.y = cos(rot_angle + pi_2) * scale;

        uv = vec2(0.0f, 0.0f);
    }
    else if (gl_VertexID == 2) {
        co.x = sin(rot_angle + pi_3) * scale;
        co.y = cos(rot_angle + pi_3) * scale;

        uv = vec2(1.0f, 0.5f);
    }
    else {
        co.x = sin(rot_angle + pi_4) * scale;
        co.y = cos(rot_angle + pi_4) * scale;

        uv = vec2(1.0f, 0.0f);
    }

    vec2 texCoord_LUT = vec2(frame_index/total_frames);

    alpha = textureLod(alpha_LUT, texCoord_LUT, 0.0).r;

    // Figure out what tile to draw...
    float tile_width = 1.0/row_length;
    uv.x = (uv.x/row_length) + (tile_width * frame_index);
    
    // Next Row?
    if (frame_index > row_length-1) {
        uv.x -= (tile_width * (frame_index-row_length));
        uv.y+=0.5;
        }

    texCoord   = uv;

    vec4 p = view *  matrix[3]  ;
    p += vec4(co, 0.0f, 1.0f);
    
    p = inverse(view) * p ;

    gl_Position =  viewProj * p;

}
