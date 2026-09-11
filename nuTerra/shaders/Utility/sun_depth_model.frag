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
}
