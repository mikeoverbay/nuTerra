#version 450 core

// Depth-only, one tank into one layer of the tank shadow array.
//
// IT DREW A UNIT CUBE UNTIL 2026-09-14, and the header here said so: "a box
// today and the mesh tomorrow". Everything around it was right - the layers,
// the matrices, the ZeroToOne remap, the range test, the fade, and
// tank_factor's transform in sun_shadow_tiles.frag, which is the SAME MATH as
// the baked path that has always produced a correct fence shadow. The only
// wrong thing was what got drawn, so every tank cast a soft rectangular slab:
// a cuboid projected along the sun angle. Worth remembering when a shadow
// looks wrong - check the geometry before the depth conventions, which is
// where I spent the first hour.
//
// The mesh is SKINNED, so this has to deform it exactly as tank_gbuffer.vert
// does or the shadow drifts off the tank casting it. Position only - a depth
// pass needs no TBN - but the skin itself must match term for term.
layout(location = 0) in vec3  vPos;
layout(location = 5) in uvec4 a_bone_idx;
layout(location = 6) in vec4  a_bone_wt;

uniform mat4 mvp;

// The palette, uploaded by the same code that feeds the beauty pass so the two
// cannot disagree. u_skinned 0 is the identity skin, which is what a rigid
// part - and the old box - takes.
const int MAX_TANK_BONES = 64;
uniform mat4 u_bones[MAX_TANK_BONES];
uniform int  u_skinned;

// Lifted from tank_gbuffer.vert, position half only. The two must stay
// identical: a shadow skinned differently from its caster is worse than no
// shadow, because it looks deliberate.
//
// The DIVIDE BY THREE is SC_UBYTE4_REVERSE_PADDED - the byte is
// palette_index * 3, because the engine's own binding is a flat vec4[N*3] of
// 3x4 affines. The WEIGHT-SUM NORMALISE is because Bigworld stores weights as
// bytes that round DOWN, so four of them can total under 255; without it the
// skin matrix is scaled by the shortfall and the vertex drifts toward the
// origin.
mat4 skin_matrix()
{
    if (u_skinned == 0) return mat4(1.0);

    ivec4 b = ivec4(a_bone_idx) / 3;
    b = clamp(b, ivec4(0), ivec4(MAX_TANK_BONES - 1));

    mat4 m = a_bone_wt.x * u_bones[b.x]
           + a_bone_wt.y * u_bones[b.y]
           + a_bone_wt.z * u_bones[b.z]
           + a_bone_wt.w * u_bones[b.w];

    float wsum = a_bone_wt.x + a_bone_wt.y + a_bone_wt.z + a_bone_wt.w;
    return (wsum > 1e-4) ? m / wsum : mat4(1.0);
}

void main(void)
{
    gl_Position = mvp * (skin_matrix() * vec4(vPos, 1.0));
}
