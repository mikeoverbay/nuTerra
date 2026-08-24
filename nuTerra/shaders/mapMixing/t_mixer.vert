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
    const TerrainChunkInfo chunk = chunks[gl_BaseInstanceARB];

    vs_out.UV = vertexTexCoord;

    // calculate tex coords for global_AM
    vs_out.Global_UV = chunk.g_uv_offset + (vertexTexCoord * props.map_size);
    vs_out.Global_UV *= -1.0;

    // True world position, Y up. The fragment stage projects this through
    // sunViewProj to look the baked sun shadow up, and sun_depth_terrain.vert
    // builds that map from the same chunk.modelMatrix * vertexPosition - so the
    // two have to be the same quantity or the lookup lands nowhere.
    //
    // This was declared in VS_OUT and never assigned. The fragment shader read
    // an undefined varying, so every baked shadow lookup used garbage.
    const vec4 world = chunk.modelMatrix * vec4(vertexPosition.xyz, 1.0f);
    vs_out.worldPosition = world.xyz;

    // Calculate vertex position in clip coordinates. The .xzyw swizzle is what
    // makes this a top-down ortho: world X and Z drive the page, world Y is depth.
    gl_Position = Ortho_Project * world.xzyw;
}
