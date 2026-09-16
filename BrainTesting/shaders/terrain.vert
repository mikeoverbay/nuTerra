#version 450 core

// Terrain, as MESHES. No virtual texturing, no cascades, no tiles - the
// owner's ask: "draw the terrain as meshes".
//
// The chunk index comes from gl_BaseInstanceARB, exactly as nuTerra's own
// terrain shaders take it: build_Terrain_VAO writes the chunk number into
// each indirect command's baseInstance, so one MultiDrawElementsIndirect
// draws every chunk with its own matrix and nothing is issued per chunk.
#extension GL_ARB_shader_draw_parameters : require

layout(location = 0) in vec3 vertexPosition;
// Attribute 2 is the packed normal - GL_INT_2_10_10_10_REV, normalized, so
// it arrives already in -1..1 and must NOT be rescaled.
layout(location = 2) in vec4 vertexNormal;

// Layout copied from common.h. std430 and the same field order, because the
// buffer is filled by nuTerra's own ChunkFunctions.
struct TerrainChunkInfo {
    mat4 modelMatrix;
    vec2 g_uv_offset;
    uint pad1;
    uint pad2;
};
layout(std430, binding = 0) readonly buffer TerrainChunkInfoBuffer {
    TerrainChunkInfo chunks[];
};

uniform mat4 viewProj;

out vec3 worldNormal;
out vec3 worldPos;

void main(void)
{
    TerrainChunkInfo chunk = chunks[gl_BaseInstanceARB];
    vec4 world = chunk.modelMatrix * vec4(vertexPosition, 1.0);

    worldPos = world.xyz;
    worldNormal = normalize(mat3(chunk.modelMatrix) * vertexNormal.xyz);

    gl_Position = viewProj * world;
}
