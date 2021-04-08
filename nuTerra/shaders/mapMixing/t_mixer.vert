#version 450 core

#extension GL_ARB_shader_draw_parameters : require
#extension GL_ARB_shading_language_include : require

#define USE_PERVIEW_UBO
#define USE_COMMON_PROPERTIES_UBO
#define USE_TERRAIN_CHUNK_INFO_SSBO
#include "common.h" //! #include "../common.h"

layout(location = 0) in vec3 vertexPosition;
layout(location = 1) in vec2 vertexTexCoord;
layout(location = 2) in vec4 vertexNormal;
layout(location = 3) in vec3 vertexTangent;

layout (std140, binding = TERRAIN_LAYERS_UBO_BASE) uniform Layers {
    vec4 U1;
    vec4 U2;
    vec4 U3;
    vec4 U4;

    vec4 U5;
    vec4 U6;
    vec4 U7;
    vec4 U8;

    vec4 V1;
    vec4 V2;
    vec4 V3;
    vec4 V4;

    vec4 V5;
    vec4 V6;
    vec4 V7;
    vec4 V8;

    vec4 r1_1;
    vec4 r1_2;
    vec4 r1_3;
    vec4 r1_4;
    vec4 r1_5;
    vec4 r1_6;
    vec4 r1_7;
    vec4 r1_8;

    vec4 r2_1;
    vec4 r2_2;
    vec4 r2_3;
    vec4 r2_4;
    vec4 r2_5;
    vec4 r2_6;
    vec4 r2_7;
    vec4 r2_8;

    vec4 s1;
    vec4 s2;
    vec4 s3;
    vec4 s4;
    vec4 s5;
    vec4 s6;
    vec4 s7;
    vec4 s8;
    };

uniform mat4 Ortho_Project;

out VS_OUT {
    vec4 Vertex;
    vec3 worldPosition;
    vec2 UV;
    vec2 Global_UV;
    flat uint map_id;
} vs_out;


const TerrainChunkInfo chunk = chunks[gl_BaseInstanceARB];


void main(void)
{
    vs_out.map_id = gl_BaseInstanceARB;
    vs_out.UV =  vertexTexCoord;

    // calculate tex coords for global_AM
    vs_out.Global_UV = 1.0 - (chunk.g_uv_offset + (vertexTexCoord * props.map_size));
    
    //-------------------------------------------------------
    vec4 Vertex = vec4(vertexPosition.xzy, 1.0) * 1.0;
    //Vertex.x *= -1.0;
    vs_out.Vertex = Vertex;


    //-------------------------------------------------------

    // Calculate vertex position in clip coordinates
    gl_Position = Ortho_Project  * vec4(vertexPosition.xzy, 1.0f);

}
