#version 450 core
#extension GL_ARB_shading_language_include : require

#define USE_PERVIEW_UBO
#include "common.h" //! #include "../common.h"

// Tank G-buffer pass, vertex stage. Reads the RAW WoT vertex stream - the
// attribute formats in TankMesh.BuildVAO hand the packed bytes straight
// through and the unpacking happens here, so nothing is decoded on the CPU.
//
// Slots, fixed with TankPrimitives.vb:
//   0 position  1 normal  2 uv0  3 tangent(u32)  4 binormal(u32)
//   5 bone idx  6 bone weights  7 uv1
// Bones ride through unused for now: bind pose is the identity skin.

layout(location = 0) in vec3  a_pos;
layout(location = 1) in vec4  a_normal;     // 8/8/8 bytes normalised, or float3 for "xyznuv"
layout(location = 2) in vec2  a_uv0;
layout(location = 3) in uint  a_tangent;    // packed 11/10/10
layout(location = 4) in uint  a_binormal;   // packed 11/10/10
layout(location = 5) in uvec4 a_bone_idx;
layout(location = 6) in vec4  a_bone_wt;
layout(location = 7) in vec2  a_uv1;

uniform mat4 u_model;
uniform int  normal_mode;   // 1: a_normal is 8/8/8 bytes (b/127.5 - 1); 0: a_normal.x carries a packed 11/10/10 (unused so far)

out VS_OUT
{
    vec2 TC1;
    vec2 TC2;
    vec3 viewPosition;
    mat3 TBN;
    flat vec3 surfaceNormal;
} vs_out;

// The exporter's unpackNormal, bit for bit: x in bits 0..10, y in 11..20,
// z in 22..31, each sign-extended and divided by 511.
vec3 unpack_11_10_10(uint p)
{
    int x = (int(p << 21u)) >> 22;
    int y = (int(p << 11u)) >> 22;
    int z = (int(p)) >> 22;
    vec3 v = vec3(x, y, z) / 511.0;
    float len = length(v);
    return (len < 1e-6) ? vec3(0.0, 0.0, 1.0) : v / len;
}

void main(void)
{
    vec3 n_local;
    if (normal_mode == 1) {
        // 8/8/8: the normalised byte is b/255, the exporter wants b/127.5 - 1.
        n_local = normalize(a_normal.xyz * 2.0 - 1.0);
    } else {
        n_local = normalize(a_normal.xyz);
    }
    vec3 t_local = unpack_11_10_10(a_tangent);
    vec3 b_local = unpack_11_10_10(a_binormal);

    mat4 modelView = view * u_model;
    mat3 normalMatrix = mat3(transpose(inverse(modelView)));

    vec3 n = normalize(normalMatrix * n_local);
    vec3 t = normalize(normalMatrix * t_local);
    vec3 b = normalize(normalMatrix * b_local);
    // A stream without tangents unpacks to (0,0,1): build a frame from the
    // normal alone so the normal map still reads as "flat" rather than junk.
    if (a_tangent == 0u) {
        vec3 up = abs(n.y) < 0.99 ? vec3(0.0, 1.0, 0.0) : vec3(1.0, 0.0, 0.0);
        t = normalize(cross(up, n));
        b = cross(n, t);
    }

    vs_out.TC1 = a_uv0;
    vs_out.TC2 = a_uv1;
    vs_out.viewPosition = vec3(modelView * vec4(a_pos, 1.0));
    vs_out.TBN = mat3(t, b, n);
    vs_out.surfaceNormal = n;

    gl_Position = projection * modelView * vec4(a_pos, 1.0);
}
