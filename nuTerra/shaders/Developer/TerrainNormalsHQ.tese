#version 450 core

#extension GL_ARB_shading_language_include : require

#define USE_PERVIEW_UBO
#include "common.h" //! #include "../common.h"

layout (triangles, equal_spacing) in;

layout(binding = 1 ) uniform sampler2DArray at[8];
layout(binding = 17) uniform sampler2D mixtexture[4];

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
    mix_levels[0] = texture(mixtexture[0], mix_coords.xy).a;
    mix_levels[1] = texture(mixtexture[0], mix_coords.xy).g;
    mix_levels[2] = texture(mixtexture[1], mix_coords.xy).a;
    mix_levels[3] = texture(mixtexture[1], mix_coords.xy).g;
    mix_levels[4] = texture(mixtexture[2], mix_coords.xy).a;
    mix_levels[5] = texture(mixtexture[2], mix_coords.xy).g;
    mix_levels[6] = texture(mixtexture[3], mix_coords.xy).a;
    mix_levels[7] = texture(mixtexture[3], mix_coords.xy).g;

    for (int i = 0; i < 8; ++i) {
        float height = crop(at[i], get_transformed_uv(gl_Position.xyz, L.U[i], L.V[i]), L.s[i]).a;
        float amplitude = L.r1[i].x;
        gl_Position.xyz += height * amplitude * mix_levels[i] * tes_out.n;
    }

    tes_out.t = gl_TessCoord.x * tes_in[0].t +
                gl_TessCoord.y * tes_in[1].t +
                gl_TessCoord.z * tes_in[2].t;

    tes_out.b = gl_TessCoord.x * tes_in[0].b +
                gl_TessCoord.y * tes_in[1].b +
                gl_TessCoord.z * tes_in[2].b;
}
