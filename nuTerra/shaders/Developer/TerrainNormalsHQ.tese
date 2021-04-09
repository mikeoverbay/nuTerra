#version 450 core

#extension GL_ARB_shading_language_include : require

#define USE_PERVIEW_UBO
#include "common.h" //! #include "../common.h"

layout (triangles, equal_spacing) in;

layout(binding = 17) uniform sampler2D mixtexture1;
layout(binding = 18) uniform sampler2D mixtexture2;
layout(binding = 19) uniform sampler2D mixtexture3;
layout(binding = 20) uniform sampler2D mixtexture4;

layout(binding = 1 ) uniform sampler2DArray at1;
layout(binding = 2 ) uniform sampler2DArray at2;
layout(binding = 3 ) uniform sampler2DArray at3;
layout(binding = 4 ) uniform sampler2DArray at4;

layout(binding = 5 ) uniform sampler2DArray at5;
layout(binding = 6 ) uniform sampler2DArray at6;
layout(binding = 7 ) uniform sampler2DArray at7;
layout(binding = 8 ) uniform sampler2DArray at8;

in TCS_OUT {
    vec3 n;
    vec3 t;
    vec3 b;
    vec2 uv;
    flat uint map_id;
} tes_in[];

out TES_OUT {
    vec3 n;
    vec3 t;
    vec3 b;
    flat uint map_id;
} tes_out;

vec2 get_transformed_uv(in vec3 pos, in vec4 U, in vec4 V) {
    vec4 vt = vec4(-pos.x+50.0, pos.y, pos.z, 1.0);
    vt *= vec4(1.0, -1.0, 1.0,  1.0);
    vec2 out_uv = vec2(dot(U,vt), dot(-V,vt));
    return out_uv;
}

vec4 crop(sampler2DArray samp, in vec2 uv, in vec4 offset)
{
    uv += vec2(0.50,0.50);
    vec2 cropped = fract(uv) * vec2(0.875, 0.875) + vec2(0.0625, 0.0625);
    return texture(samp, vec3(cropped, 0.0));
}

void main(void)
{
    // forward
    tes_out.map_id = tes_in[0].map_id;

    gl_Position = gl_TessCoord.x * gl_in[0].gl_Position +
                  gl_TessCoord.y * gl_in[1].gl_Position +
                  gl_TessCoord.z * gl_in[2].gl_Position;

    tes_out.n = gl_TessCoord.x * tes_in[0].n +
                gl_TessCoord.y * tes_in[1].n +
                gl_TessCoord.z * tes_in[2].n;

    vec2 mix_coords = gl_TessCoord.x * tes_in[0].uv +
                      gl_TessCoord.y * tes_in[1].uv +
                      gl_TessCoord.z * tes_in[2].uv;
    mix_coords.x = 1.0 - mix_coords.x;

    float mix_levels[8];
    mix_levels[0] = texture(mixtexture1, mix_coords.xy).a;
    mix_levels[1] = texture(mixtexture1, mix_coords.xy).g;
    mix_levels[2] = texture(mixtexture2, mix_coords.xy).a;
    mix_levels[3] = texture(mixtexture2, mix_coords.xy).g;
    mix_levels[4] = texture(mixtexture3, mix_coords.xy).a;
    mix_levels[5] = texture(mixtexture3, mix_coords.xy).g;
    mix_levels[6] = texture(mixtexture4, mix_coords.xy).a;
    mix_levels[7] = texture(mixtexture4, mix_coords.xy).g;

    float amplitudes[8];
    amplitudes[0] = r1_1.x;
    amplitudes[1] = r1_2.x;
    amplitudes[2] = r1_3.x;
    amplitudes[3] = r1_4.x;
    amplitudes[4] = r1_5.x;
    amplitudes[5] = r1_6.x;
    amplitudes[6] = r1_7.x;
    amplitudes[7] = r1_8.x;

    float heights[8];
    heights[0] = crop(at1, get_transformed_uv(gl_Position.xyz, U1, V1), s1).a;
    heights[1] = crop(at2, get_transformed_uv(gl_Position.xyz, U2, V2), s2).a;
    heights[2] = crop(at3, get_transformed_uv(gl_Position.xyz, U3, V3), s3).a;
    heights[3] = crop(at4, get_transformed_uv(gl_Position.xyz, U4, V4), s4).a;
    heights[4] = crop(at5, get_transformed_uv(gl_Position.xyz, U5, V5), s5).a;
    heights[5] = crop(at6, get_transformed_uv(gl_Position.xyz, U6, V6), s6).a;
    heights[6] = crop(at7, get_transformed_uv(gl_Position.xyz, U7, V7), s7).a;
    heights[7] = crop(at8, get_transformed_uv(gl_Position.xyz, U8, V8), s8).a;

    for (int i = 0; i < 8; ++i) {
        gl_Position.xyz += heights[i] * amplitudes[i] * mix_levels[i] * tes_out.n;
    }

    tes_out.t = gl_TessCoord.x * tes_in[0].t +
                gl_TessCoord.y * tes_in[1].t +
                gl_TessCoord.z * tes_in[2].t;

    tes_out.b = gl_TessCoord.x * tes_in[0].b +
                gl_TessCoord.y * tes_in[1].b +
                gl_TessCoord.z * tes_in[2].b;
}
