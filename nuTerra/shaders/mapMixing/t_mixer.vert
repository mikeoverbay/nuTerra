#version 450 core

#extension GL_ARB_shading_language_include : require
#extension GL_ARB_shader_draw_parameters : require

#define USE_PERVIEW_UBO
#define USE_TERRAIN_CHUNK_INFO_SSBO
#include "common.h" //! #include "../common.h"

layout(location = 0) in vec3 vertexPosition;
layout(location = 1) in vec2 vertexTexCoord;
layout(location = 2) in vec4 vertexNormal;
layout(location = 3) in vec3 vertexTangent;

uniform mat4 Ortho_Project;

out VS_OUT {
    vec4 Vertex;
    vec3 worldPosition;
    vec2 UV;
    vec2 Global_UV;
    flat uint map_id;
} vs_out;

void main(void)
{
    vs_out.map_id = gl_BaseInstanceARB;
    const TerrainChunkInfo info = terrain_chunk_info[gl_BaseInstanceARB];

    vs_out.UV =  vertexTexCoord;
    // calculate tex coords for global_AM
    vec2 uv_g;
    vec2 scaled = vs_out.UV / map_size;
    vec2 m_s = vec2(1.0)/map_size;
    uv_g.x = ((( (info.me_location.x )-50.0)/100.0)+map_center.x) * m_s.x ;
    uv_g.y = ((( (info.me_location.y )-50.0)/100.0)-map_center.y) * m_s.y ;
    vs_out.Global_UV = scaled + uv_g;
    vs_out.Global_UV.xy = 1.0 - vs_out.Global_UV.xy;
    
    //-------------------------------------------------------
    vec4 Vertex = vec4(vertexPosition.xzy, 1.0) * 1.0;
    //Vertex.x *= -1.0;
    vs_out.Vertex = Vertex;


    //-------------------------------------------------------

    // Calculate vertex position in clip coordinates
    gl_Position = Ortho_Project  * vec4(vertexPosition.xzy, 1.0f);

}
