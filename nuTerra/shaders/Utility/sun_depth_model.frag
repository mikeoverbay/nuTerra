#version 450 core

#extension GL_ARB_bindless_texture : require
#extension GL_ARB_shading_language_include : require

#define USE_MATERIALS_SSBO
#include "common.h" //! #include "../common.h"

in Block
{
    flat uint material_id;
    vec2 uv;
    flat uint kind;
    flat uint obj_id;
} fs_in;

// Four power moments of this fragment's depth, for the Moment Shadow Map path.
//
// Written unconditionally. When MSM is off the bake FBO has no colour
// attachment and DrawBuffer is None, so these writes are discarded by the
// pipeline and the cost is a few ALU during a once-per-load pass.
//
// Depth arrives in 0..1 - the engine runs ClipDepthMode.ZeroToOne - and the
// moments are taken on it directly. deferred.frag biases toward the moments of
// a uniform distribution on 0..1 when it reconstructs.
//
// The depth test still decides which fragment lands here, so these are the
// moments of the nearest occluder, which is what MSM wants. Filtering across
// neighbours happens afterwards, in msm_blur and the mip chain.
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
layout(location = 1) out vec4 bake_key;

// ---- THE FLIGHT BAKE'S ID CHANNEL ------------------------------------------
//
// Location 2, the third attachment: WHICH object is on top, where the key
// channel says only what KIND it is. The owner's ask - "render ids so we know
// what is what on the map, colors is not enough" - and the reason Path Studio's
// route signature has to split a kind mask into components today: two adjacent
// buildings are one blob of key 1 and it cannot tell them apart.
//
// Zero means nothing was drawn here, so every id is BIASED BY ONE. The same
// bias ModelPicker already uses on its own pick buffer, for the same reason.
//
// The id space is one flat range shared with the trees: models take 1 ..
// id_model_count, trees the block above it. The meta says where the join is,
// and the sidecar names every range.
layout(location = 2) out uvec4 bake_id;

void main(void)
{
    // Cutout, so a fence, a grate or a foliage card casts its own shape into
    // the bake instead of a solid rectangle. The cascades have always done
    // this (mDepthWrite.frag, mDepthWrite_light.frag) and so have the trees
    // (sun_depth_tree.frag); models in the map-wide bake were the one path
    // that did not, so they cast solid silhouettes.
    //
    // Same test as those, deliberately character for character: PBS packs
    // cutout alpha in the NORMAL map's red channel, and only glow/lightonly
    // cards carry it in the diffuse's alpha - reading diffuse.a for everything
    // is the obvious version and it is wrong.
    //
    // The discard has to happen before the moments are written, or a rejected
    // fragment still contributes depth to the MSM path.
    const MaterialProperties thisMaterial = material[fs_in.material_id];

    if (thisMaterial.alphaTestEnable) {
        float alpha = thisMaterial.alphaFromDiffuse
            ? texture(sampler2D(thisMaterial.maps[0]), fs_in.uv).a
            : texture(sampler2D(thisMaterial.maps[1]), fs_in.uv).r;
        if (alpha < thisMaterial.alphaReference) {
            discard;
        }
    }

    float z  = gl_FragCoord.z;
    float z2 = z * z;
    moments = vec4(z, z2, z2 * z, z2 * z2);

    // The key as a byte in red. The depth test has already decided this is the
    // topmost thing at the texel, so what survives is the kind of whatever is
    // actually on top - no sorting and no second pass.
    bake_key = vec4(float(fs_in.kind) / 255.0, 0.0, 0.0, 1.0);

    // Same fragment, same depth test, so the kind and the id at a texel always
    // describe the SAME surface. Two passes could not promise that.
    bake_id = uvec4(fs_in.obj_id, 0u, 0u, 0u);
}
