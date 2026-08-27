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
layout(location = 8) in uint vertexFlags;      // bit 0 = bark (no alpha test)

out VS_OUT
{
    vec2 TC;
    vec3 worldPosition;
    vec3 normal;
    flat uvec2 texHandle;
    flat uint flags;
} vs_out;

void main(void)
{
    vs_out.TC = vertexTexCoord;
    vs_out.texHandle = vertexTexHandle;
    vs_out.flags = vertexFlags;

    vec4 world = instanceMatrix * vec4(vertexPosition, 1.0);
    vec4 viewPos = view * world;

    vs_out.worldPosition = viewPos.xyz;

    // Trees are uniformly scaled, so the basis carries normals correctly: for
    // M = R * diag(-1,1,1) the inverse transpose equals mat3(M), so the mirrored
    // normal already points outward and needs no special handling.
    //
    // The winding is another matter. Every instance carries a -1 x scale - the
    // whole world is mirrored for display - and a mirror reverses triangle
    // winding, so gl_FrontFacing in the fragment stage reports the opposite of
    // the truth for every tree. The fragment stage flips the normal on a back
    // face, because leaf cards are two sided and that is the right thing to do
    // for a genuine back face; fed an inverted facing it flipped every FRONT
    // face instead and the whole canopy lit backwards to the sun.
    //
    // Pre-negating here cancels that. Driven off the determinant rather than
    // assumed, so an instance that is not mirrored still behaves.
    vec3 nrm = normalize(mat3(instanceMatrix) * vertexNormal);
    if (determinant(mat3(instanceMatrix)) < 0.0) {
        nrm = -nrm;
    }
    vs_out.normal = mat3(view) * nrm;

    gl_Position = projection * viewPos;
}
