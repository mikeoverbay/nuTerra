#version 450 core

#extension GL_ARB_shading_language_include : require

#define USE_COMMON_PROPERTIES_UBO
#include "common.h" //! #include "../common.h"

layout (vertices = 3) out;

in VS_OUT {
    vec3 n;
    vec3 t;
    vec3 b;
    vec2 uv;
    flat uint map_id;
} tcs_in[];

out TCS_OUT {
    vec3 n;
    vec3 t;
    vec3 b;
    vec2 uv;
    flat uint map_id;
} tcs_out[];

void main(void)
{
    if (gl_InvocationID == 0) {
        gl_TessLevelInner[0] = props.tess_level;
        gl_TessLevelOuter[0] = props.tess_level;
        gl_TessLevelOuter[1] = props.tess_level;
        gl_TessLevelOuter[2] = props.tess_level;
    }

    gl_out[gl_InvocationID].gl_Position = gl_in[gl_InvocationID].gl_Position;

    // forward
    tcs_out[gl_InvocationID].n = tcs_in[gl_InvocationID].n;
    tcs_out[gl_InvocationID].t = tcs_in[gl_InvocationID].t;
    tcs_out[gl_InvocationID].b = tcs_in[gl_InvocationID].b;
    tcs_out[gl_InvocationID].uv = tcs_in[gl_InvocationID].uv;
    tcs_out[gl_InvocationID].map_id = tcs_in[gl_InvocationID].map_id;
}
