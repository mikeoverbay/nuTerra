#version 450 core

#extension GL_ARB_shading_language_include : require

#define USE_PERVIEW_UBO
#include "common.h" //! #include "../common.h"

layout (triangles, equal_spacing) in;

in TCS_OUT {
    vec3 n;
    vec3 t;
    vec3 b;
    flat uint map_id;
} tes_in[];

out TES_OUT {
    vec3 n;
    vec3 t;
    vec3 b;
    flat uint map_id;
} tes_out;

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

    tes_out.t = gl_TessCoord.x * tes_in[0].t +
                gl_TessCoord.y * tes_in[1].t +
                gl_TessCoord.z * tes_in[2].t;

    tes_out.b = gl_TessCoord.x * tes_in[0].b +
                gl_TessCoord.y * tes_in[1].b +
                gl_TessCoord.z * tes_in[2].b;
}
