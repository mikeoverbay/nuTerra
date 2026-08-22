#version 450 core

#extension GL_ARB_bindless_texture : require
#extension GL_ARB_shading_language_include : require

#define USE_PERVIEW_UBO
#include "common.h" //! #include "../common.h"

// Real SpeedTree geometry, in the species' own object space. One copy per
// species; every placement is an instance carrying its own world transform.
layout(location = 0) in vec3 vertexPosition;
layout(location = 1) in vec3 vertexNormal;
layout(location = 2) in vec2 vertexTexCoord;
layout(location = 3) in uvec2 vertexTexHandle; // bindless bark or leaf atlas
layout(location = 4) in mat4 instanceMatrix;   // occupies 4..7

out VS_OUT
{
    vec2 TC;
    vec3 worldPosition;
    vec3 normal;
    flat uvec2 texHandle;
} vs_out;

void main(void)
{
    vs_out.TC = vertexTexCoord;
    vs_out.texHandle = vertexTexHandle;

    vec4 world = instanceMatrix * vec4(vertexPosition, 1.0);
    vec4 viewPos = view * world;

    vs_out.worldPosition = viewPos.xyz;
    // Trees are uniformly scaled, so the basis carries normals correctly. The
    // world is mirrored in x, which flips handedness - the fragment stage sorts
    // that out by facing.
    vs_out.normal = mat3(view) * normalize(mat3(instanceMatrix) * vertexNormal);

    gl_Position = projection * viewPos;
}
