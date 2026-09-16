#version 450 core

#extension GL_ARB_bindless_texture : require
#extension GL_ARB_shading_language_include : require

#include "common.h" //! #include "../common.h"

in Block
{
    vec2 uv;
    flat uvec2 texHandle;
    flat uint flags;
    float trunk_r;
    flat uint obj_id;
} fs_in;

// Moments for the MSM path. Discarded by the pipeline when the bake FBO has no
// colour attachment - see sun_depth_terrain.frag.
layout(location = 0) out vec4 moments;

// ---- THE FLIGHT BAKE'S KEY CHANNEL -----------------------------------------
//
// Written at location 1, NOT 0. Location 0 is the Moment Shadow Map's four
// moments and has been since the sun bake needed them; taking it would have
// silently replaced the shadow data with a key byte.
//
// The flight bake attaches its key texture at ColorAttachment1 and names it as
// the SECOND entry of its draw-buffer array, so location 1 lands there and
// location 0 goes to None. The sun bake names neither, so on that pass both
// writes are discarded and this costs a few ALU in a once-per-load job.
// Trees are one kind, so this is a constant rather than a lookup.
layout(location = 1) out vec4 bake_key;
const float BAKE_KIND_TREE = 3.0 / 255.0;

// ---- THE FLIGHT BAKE'S ID CHANNEL ------------------------------------------
//
// Location 2: WHICH tree, not just that a tree is here. The vertex stage
// already has the placement index; this carries it out so the bake can say a
// texel belongs to that linden rather than to tree cover in general.
//
// THE TRUNK PASS MUST NOT REACH THIS ATTACHMENT. That pass runs with the depth
// test off and ColorLogicOp OR, which is how a trunk records itself under its
// own canopy - and a logic op applies to every enabled draw buffer, integer
// ones included. ORing ids together would produce numbers that name no object
// at all. MapFlightBake.draw_trunks masks this buffer off for the duration;
// the write below still happens and is discarded, which is cheaper than a
// branch and cannot be got wrong from in here.
layout(location = 2) out uvec4 bake_id;

// ---- THE TRUNK PASS --------------------------------------------------------
//
// OFF BY DEFAULT, so the sun cascades and the lamp shadows run exactly as they
// did - they want the whole tree and get it.
//
// The flight bake turns it on for one extra pass to answer a different
// question: what would a TANK hit. A tank drives under a tree. It does not
// drive through the trunk, and it does not collide with a branch eight metres
// up, so the canopy that is right for a camera is wrong for a vehicle.
//
// That pass writes the key channel ONLY, with the depth test off and the
// colour logic op set to OR, because a trunk always loses a depth test to its
// own canopy - the leaves are directly above it. ORing a bit in sidesteps the
// ordering entirely: the bit records that a trunk stands at this texel no
// matter what else won the surface.
uniform bool u_trunk_only = false;
// Set PER DRAW CALL by MapTrees.sun_depth_pass, from that species' measured
// collision radius. The default is the old global ceiling and applies only to
// a caller that forgets - the shadow bakes never take this branch.
uniform float u_trunk_radius = 0.6;

// 0x80. Deliberately above the key's own 0..7 so one byte carries both: the
// low bits stay the kind and nothing downstream that already reads them has to
// change, as long as it masks.
const float BAKE_TRUNK_BIT = 128.0 / 255.0;

void main(void)
{
    if (u_trunk_only) {
        // Bark only, and only the part of it standing close to the tree's own
        // axis. Leaf cards are not structure; limbs are bark but they are over
        // a tank's head. What survives both tests is the column at the base.
        //
        // Ground cover has no bark part at all - ivy, lavender, roses, the
        // vines and the bushes are leaf geometry only - so they write nothing
        // here and vanish from the obstacle set. That is the right answer: a
        // tank drives through a bush.
        if ((fs_in.flags & 1u) == 0u || fs_in.trunk_r > u_trunk_radius) {
            discard;
        }
        bake_key = vec4(BAKE_TRUNK_BIT, 0.0, 0.0, 1.0);
        moments = vec4(0.0);
        bake_id = uvec4(fs_in.obj_id, 0u, 0u, 0u);
        return;
    }

    // Without this a leaf card casts the shadow of a rectangle. The atlas is
    // mostly empty space, so the cutout is what makes the shadow leaf shaped.
    // Bark (flag bit 0) is exempt - trunks are opaque and some species' bark
    // alpha is a spec mask, not coverage.
    //
    // MIP AWARE, THE SAME CURVE tree.frag USES, and for the same reason -
    // which this pass needed even more than the beauty pass does and did not
    // have. Alpha mipmaps average toward the atlas mean, so a FIXED 0.5 stops
    // passing anything once the sample is small enough. Measured on the four
    // atlases, as the fraction of texels still clearing 0.5:
    //
    //              mip 3   mip 4   mip 5   mip 6
    //   Olive       8.1%    3.3%    0.0%    0.0%
    //   Cypress     9.4%    5.3%    1.2%    0.0%
    //   Bush_Wild  19.4%   15.7%    8.6%    0.0%
    //   Linden     15.9%   13.3%   10.9%    6.2%
    //
    // The bake rasterises the whole map top down at about 0.17 m a texel, so a
    // metre-wide leaf card out of a 512 atlas is sampled around mip 7 - past
    // the point every one of those reaches zero. The olive died first and the
    // linden last, and that is exactly the order they came out of the bake:
    // an olive bush with a 6.7 x 8.8 m canopy was reduced to a 1.9 x 1.7 m
    // core setting 9% of its texels, while the linden beside it set 71%.
    // Nothing was wrong with the geometry - both species' foliage faces the
    // same way, mean |ny| 0.51 against 0.50 - it was this line.
    if ((fs_in.flags & 1u) == 0u) {
        float mip = textureQueryLod(sampler2D(fs_in.texHandle), fs_in.uv).x;
        float cutoff = 0.5 / (1.0 + mip * 0.55);
        if (texture(sampler2D(fs_in.texHandle), fs_in.uv).a < cutoff) {
            discard;
        }
    }

    float z  = gl_FragCoord.z;
    float z2 = z * z;
    moments = vec4(z, z2, z2 * z, z2 * z2);
    bake_key = vec4(BAKE_KIND_TREE, 0.0, 0.0, 1.0);
    bake_id = uvec4(fs_in.obj_id, 0u, 0u, 0u);
}
