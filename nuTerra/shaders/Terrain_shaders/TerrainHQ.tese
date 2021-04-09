#version 450 core

#extension GL_ARB_shading_language_include : require

#define USE_PERVIEW_UBO
#define USE_COMMON_PROPERTIES_UBO
#define USE_TERRAIN_CHUNK_INFO_SSBO
#include "common.h" //! #include "../common.h"

layout (triangles, equal_spacing) in;

uniform mat3 normalMatrix;

in TCS_OUT {
    vec3 vertexPosition;
    vec3 vertexNormal;
    vec3 vertexTangent;
    vec2 UV;
    flat uint map_id;
} tes_in[];

out TES_OUT {
    mat3 TBN;
    vec3 vertexPosition;
    vec3 worldPosition;
    vec2 UV;
    vec2 Global_UV;
    float ln;
    flat uint map_id;
} tes_out;

void main(void)
{
    // forward
    tes_out.map_id = tes_in[0].map_id;

    const TerrainChunkInfo chunk = chunks[tes_in[0].map_id];

    vec4 pos = gl_TessCoord.x * gl_in[0].gl_Position +
               gl_TessCoord.y * gl_in[1].gl_Position +
               gl_TessCoord.z * gl_in[2].gl_Position;

    tes_out.UV = gl_TessCoord.x * tes_in[0].UV +
                 gl_TessCoord.y * tes_in[1].UV +
                 gl_TessCoord.z * tes_in[2].UV;

    gl_Position = viewProj * pos;

    tes_out.vertexPosition = gl_TessCoord.x * tes_in[0].vertexPosition +
                             gl_TessCoord.y * tes_in[1].vertexPosition +
                             gl_TessCoord.z * tes_in[2].vertexPosition;

    tes_out.worldPosition = vec3(view * pos);

    tes_out.Global_UV =  1.0 - (chunk.g_uv_offset + (tes_out.UV * props.map_size));

    // This is the cut off distance for bumping the surface.
    tes_out.ln = distance(pos.xyz, cameraPos.xyz);

    if (tes_out.ln < props._start + props._end) { tes_out.ln = 1.0 - (tes_out.ln-props._start)/props._end;}
    else {tes_out.ln = 0.0;}

    //-------------------------------------------------------
    // Calculate biNormal
    vec3 VT, VB, VN ;
    VN = normalize(gl_TessCoord.x * tes_in[0].vertexNormal +
                   gl_TessCoord.y * tes_in[1].vertexNormal +
                   gl_TessCoord.z * tes_in[2].vertexNormal);

    VT = normalize(gl_TessCoord.x * tes_in[0].vertexTangent +
                   gl_TessCoord.y * tes_in[1].vertexTangent +
                   gl_TessCoord.z * tes_in[2].vertexTangent);

    VT = VT - dot(VN, VT) * VN;
    VB = cross(VT, VN);
    //-------------------------------------------------------

    // Tangent, biNormal and Normal must be trasformed by the normal Matrix.
    vec3 worldNormal = normalMatrix * VN;
    vec3 worldTangent = normalMatrix * VT;
    vec3 worldbiNormal = normalMatrix * VB;

    // make perpendicular
    worldTangent = normalize(worldTangent - dot(worldNormal, worldTangent) * worldNormal);
    worldbiNormal = normalize(worldbiNormal - dot(worldNormal, worldbiNormal) * worldNormal);

    // Create the Tangent, BiNormal, Normal Matrix for transforming the normalMap.
    tes_out.TBN = mat3(worldTangent, worldbiNormal, normalize(worldNormal));
}
