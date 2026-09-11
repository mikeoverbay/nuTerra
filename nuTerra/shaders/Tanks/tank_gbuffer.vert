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
uniform int  normal_mode;
// Which way the triangles come out wound. See where it is used below.
uniform float u_winding;

// THE TRACK BAND'S SCROLL. The band's material is PBS_tank_uvtransform*.fx -
// the animation is a UV transform, not bone motion, and the name says so. Zero
// on everything else, so this costs one add on meshes that do not scroll.
uniform vec2 uv_scroll;

// ---- GPU SKINNING ----------------------------------------------------------
//
// The vertex stream has carried bone indices and weights since the VAO was
// written; nothing consumed them and the comment above said so - "Bones ride
// through unused for now: bind pose is the identity skin". This is that path.
//
// The contract is the one every Bigworld / WoT shader uses, and TEPY's
// mesh.vert states it outright:
//
//     skin = sum_i ww[i] * u_bones[ iii[i] / 3 ]
//
// The DIVIDE BY THREE is SC_UBYTE4_REVERSE_PADDED: the engine binds a flat
// `vec4 bones[N*3]` - three rows per bone for a 3x4 affine - and the iii byte
// indexes that flat array directly, so the byte is palette_index * 3.
// TankPrimitives already documents it at the attribute. A mat4 palette is
// cleaner in GLSL, so the byte is divided back down here.
//
// Position AND the whole TBN are skinned. Rotating the position without
// rotating the basis leaves normal-mapped surfaces lit for the bind pose on
// geometry that has moved.
//
// 64 is TEPY's ceiling too, and it notes that going higher walks into
// GL_MAX_VERTEX_UNIFORM_COMPONENTS territory on weaker drivers. The app logs
// and clamps rather than overflowing.
const int MAX_TANK_BONES = 64;
uniform mat4 u_bones[MAX_TANK_BONES];
uniform int  u_skinned;      // 0 = identity skin, exactly as before   // 1: a_normal is 8/8/8 bytes (b/127.5 - 1); 0: a_normal.x carries a packed 11/10/10 (unused so far)

// TWO FRAMES, ON PURPOSE.
//
// The G-buffer wants VIEW space - gPosition and gNormal are read that way by
// ssr, lamp_fog, probe_field, the particles and the resolve itself. The tank's
// own shading is the exporter's, and that is written in WORLD space: the light
// rig, the crash tile's position hash and the muzzle flash all take world
// positions, and the environment cube is a world direction. Carrying both is
// two extra varyings and removes every chance of mixing them up.
out VS_OUT
{
    vec2 TC1;
    vec2 TC2;
    vec3 viewPosition;
    mat3 TBN;              // VIEW space, for the G-buffer normal
    flat vec3 surfaceNormal;
    vec3 worldPosition;
    mat3 worldTBN;         // WORLD space, for the exporter's shading
    vec3 worldNormal;      // un-perturbed; the exporter reflects off THIS
    flat float winding;    // +1 normal, -1 where u_model mirrors
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

mat4 skin_matrix()
{
    if (u_skinned == 0) return mat4(1.0);

    ivec4 b = ivec4(a_bone_idx) / 3;
    b = clamp(b, ivec4(0), ivec4(MAX_TANK_BONES - 1));

    mat4 m = a_bone_wt.x * u_bones[b.x]
           + a_bone_wt.y * u_bones[b.y]
           + a_bone_wt.z * u_bones[b.z]
           + a_bone_wt.w * u_bones[b.w];

    // NORMALISE BY THE WEIGHT SUM. Bigworld stores weights as bytes and they
    // round DOWN, so a vertex's four weights can total less than 255 - TEPY hit
    // this as a visible X-shift on 240 of G78's 3046 ribbon verts and fixed it
    // the same way (its v1.118.106). Without this the skin matrix is scaled by
    // the shortfall and the vertex drifts toward the origin.
    float wsum = a_bone_wt.x + a_bone_wt.y + a_bone_wt.z + a_bone_wt.w;
    return (wsum > 1e-4) ? m / wsum : mat4(1.0);
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

    // The skin runs BEFORE the model transform, so everything below - the world
    // and view matrices, the normal matrices, the winding - is untouched.
    mat4 skin = skin_matrix();
    vec3 p_local = vec3(skin * vec4(a_pos, 1.0));
    mat3 skin3 = mat3(skin);
    n_local = normalize(skin3 * n_local);
    t_local = normalize(skin3 * t_local);
    b_local = normalize(skin3 * b_local);

    mat4 modelView = view * u_model;
    mat3 normalMatrix = mat3(transpose(inverse(modelView)));
    mat3 worldNormalMatrix = mat3(transpose(inverse(u_model)));

    vec3 n = normalize(normalMatrix * n_local);
    vec3 t = normalize(normalMatrix * t_local);
    vec3 b = normalize(normalMatrix * b_local);

    vec3 wn = normalize(worldNormalMatrix * n_local);
    vec3 wt = normalize(worldNormalMatrix * t_local);
    vec3 wb = normalize(worldNormalMatrix * b_local);
    // A stream without tangents unpacks to (0,0,1): build a frame from the
    // normal alone so the normal map still reads as "flat" rather than junk.
    if (a_tangent == 0u) {
        vec3 up = abs(n.y) < 0.99 ? vec3(0.0, 1.0, 0.0) : vec3(1.0, 0.0, 0.0);
        t = normalize(cross(up, n));
        b = cross(n, t);

        vec3 wup = abs(wn.y) < 0.99 ? vec3(0.0, 1.0, 0.0) : vec3(1.0, 0.0, 0.0);
        wt = normalize(cross(wup, wn));
        wb = cross(wn, wt);
    }

    vs_out.TC1 = a_uv0 + uv_scroll;
    vs_out.TC2 = a_uv1;
    vs_out.viewPosition = vec3(modelView * vec4(p_local, 1.0));
    vs_out.TBN = mat3(t, b, n);
    vs_out.surfaceNormal = n;
    vs_out.worldPosition = vec3(u_model * vec4(p_local, 1.0));
    vs_out.worldTBN = mat3(wt, wb, wn);
    vs_out.worldNormal = wn;

    // WHICH WAY THE TRIANGLES COME OUT WOUND, and it is NOT the determinant.
    //
    // gl_FrontFacing is decided by winding, and the fragment stage needs it to
    // flip the normal on back faces because culling is off. Getting the sign
    // wrong inverts the lighting on whatever it is wrong about.
    //
    // The obvious test - determinant(mat3(u_model)) < 0 - is wrong here, and
    // this shader shipped with it. u_model carries CreateScale(-1,1,1) from
    // MirrorX, plus another CreateScale(1,1,-1) on skinned parts from
    // FlipSkinnedZ, so its determinant is NEGATIVE on the hull and turret and
    // POSITIVE on the chassis, tracks and gun. That reads as "the skinned parts
    // are not mirrored", and they were left uncorrected.
    //
    // But the skinned Z flip is not a mirror of the geometry - it is UNDOING
    // one. TankRenderer's own note: "Skinned parts are stored with Z reversed
    // relative to the hull and turret", so their vertex data arrives already
    // mirrored and already wound backwards. The matrix flip puts the geometry
    // back where it belongs; the winding was reversed in the buffer and stays
    // reversed. Net: BOTH kinds render with reversed winding, and the only
    // real mirror is MirrorX.
    //
    // So the app passes the one true sign rather than the shader inferring a
    // false one from a matrix that describes two different things.
    vs_out.winding = u_winding;

    gl_Position = projection * modelView * vec4(p_local, 1.0);
}
