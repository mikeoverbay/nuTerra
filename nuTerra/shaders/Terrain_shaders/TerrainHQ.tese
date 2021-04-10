#version 450 core

#ifdef GL_SPIRV
#extension GL_GOOGLE_include_directive : require
#else
#extension GL_ARB_shading_language_include : require
#endif

#define USE_PERVIEW_UBO
#define USE_COMMON_PROPERTIES_UBO
#define USE_TERRAIN_CHUNK_INFO_SSBO
#include "common.h" //! #include "../common.h"

layout (triangles, equal_spacing) in;

layout(binding = 1 ) uniform sampler2DArray at[8];
layout(binding = 17) uniform sampler2D mixtexture[4];

layout(location = 5) uniform mat3 normalMatrix;

layout(location = 0) in TCS_OUT {
    vec3 vertexPosition;
    vec3 vertexNormal;
    vec3 vertexTangent;
    vec2 UV;
    flat int map_id;
} tes_in[];

layout(location = 0) out TES_OUT {
    mat3 TBN;
    vec3 vertexPosition;
    vec3 worldPosition;
    vec2 UV;
    vec2 Global_UV;
    float ln;
    flat int map_id;
} tes_out;

vec2 get_transformed_uv(in vec3 pos, in vec4 U, in vec4 V) {
    vec4 vt = vec4(-pos.x+50.0, pos.y, pos.z, 1.0);
    vt *= vec4(1.0, -1.0, 1.0,  1.0);
    vec2 out_uv = vec2(dot(U,vt), dot(-V,vt));
    return out_uv;
}

vec4 crop( sampler2DArray samp, in vec2 uv , in vec4 offset)
{
    uv += vec2(0.50,0.50);
    vec2 cropped = fract(uv) * vec2(0.875, 0.875) + vec2(0.0625, 0.0625);
    return texture(samp, vec3(cropped, 0.0));
}

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

    vec2 mix_coords = tes_out.UV;
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
        float height = crop(at[i], get_transformed_uv(tes_out.vertexPosition, L.U[i], L.V[i]), L.s[i]).a;
        float amplitude = L.r1[i].x;
        pos.xyz += height * amplitude * mix_levels[i] * VN;
    }

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

    gl_Position = viewProj * pos;
}
