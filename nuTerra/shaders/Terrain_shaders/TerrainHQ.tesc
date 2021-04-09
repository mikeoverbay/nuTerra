#version 450 core

#extension GL_ARB_shading_language_include : require

#define USE_COMMON_PROPERTIES_UBO
#include "common.h" //! #include "../common.h"

layout (vertices = 3) out;

in VS_OUT {
    vec3 vertexPosition;
    vec3 vertexNormal;
    vec3 vertexTangent;
    vec2 UV;
    flat uint map_id;
} tcs_in[];

out TCS_OUT {
    vec3 vertexPosition;
    vec3 vertexNormal;
    vec3 vertexTangent;
    vec2 UV;
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
    tcs_out[gl_InvocationID].vertexPosition = tcs_in[gl_InvocationID].vertexPosition;
    tcs_out[gl_InvocationID].vertexNormal = tcs_in[gl_InvocationID].vertexNormal;
    tcs_out[gl_InvocationID].vertexTangent = tcs_in[gl_InvocationID].vertexTangent;
    tcs_out[gl_InvocationID].UV = tcs_in[gl_InvocationID].UV;
    tcs_out[gl_InvocationID].map_id = tcs_in[gl_InvocationID].map_id;
}
