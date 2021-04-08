#version 450 core

#extension GL_ARB_shading_language_include : require

#define USE_PERVIEW_UBO
#include "common.h" //! #include "../common.h"

layout (triangles, equal_spacing, ccw) in;

in TCS_OUT {
    vec4 Vertex;
    vec2 UV;
    vec2 Global_UV;
    float ln;
    flat uint map_id;
} tes_in[];

out TES_OUT {
    mat3 TBN;
    vec4 Vertex;
    vec3 worldPosition;
    vec2 UV;
    vec2 Global_UV;
    float ln;
    flat uint map_id;
} tes_out;

void main(void)
{
    vec4 pos = gl_TessCoord.x * gl_in[0].gl_Position +
               gl_TessCoord.y * gl_in[1].gl_Position +
               gl_TessCoord.z * gl_in[2].gl_Position;

    gl_Position = viewProj * pos;

    tes_out.Vertex = gl_TessCoord.x * tes_in[0].Vertex +
                     gl_TessCoord.y * tes_in[1].Vertex +
                     gl_TessCoord.z * tes_in[2].Vertex;

    tes_out.worldPosition = vec3(view * pos);

    tes_out.UV = gl_TessCoord.x * tes_in[0].UV +
                 gl_TessCoord.y * tes_in[1].UV +
                 gl_TessCoord.z * tes_in[2].UV;

    tes_out.Global_UV = tes_in[0].Global_UV;

    tes_out.ln = (gl_TessCoord.x * tes_in[0].ln +
                  gl_TessCoord.y * tes_in[1].ln +
                  gl_TessCoord.z * tes_in[2].ln);

    tes_out.map_id = tes_in[0].map_id;
}
