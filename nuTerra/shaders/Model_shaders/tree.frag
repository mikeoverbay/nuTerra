#version 450 core

#extension GL_ARB_bindless_texture : require
#extension GL_ARB_shading_language_include : require

#include "common.h" //! #include "../common.h"

layout (location = 0) out vec4 gColor;
layout (location = 1) out vec3 gNormal;
layout (location = 2) out vec4 gGMF;
layout (location = 3) out vec3 gPosition;
layout (location = 4) out vec3 gSurfaceNormals;

#ifdef PICK_MODELS
layout (location = 5) out uint gPick;
// One id per species draw (TREE_PICK_BASE band), set by MapTrees.draw.
uniform uint pick_id;
#endif

in VS_OUT
{
    vec2 TC;
    vec3 worldPosition;
    vec3 normal;
    flat uvec2 texHandle;
} fs_in;

void main(void)
{
    vec4 albedo = texture(sampler2D(fs_in.texHandle), fs_in.TC);

    // Foliage cutout - the leaf atlas is mostly empty space.
    //
    // Which is exactly why a fixed 0.5 test erases trees at distance: alpha
    // mipmaps average toward the atlas mean (~0.2), so past a few hundred
    // metres every texel fails the test and the card renders zero fragments.
    // The geometry was never the problem - the discard was. Lowering the
    // threshold with the mip level keeps at least the densest texels alive at
    // any distance, so a far tree stays a tree instead of thinning to nothing.
    float mip = textureQueryLod(sampler2D(fs_in.texHandle), fs_in.TC).x;
    float cutoff = 0.5 / (1.0 + mip * 0.55);
    if (albedo.a < cutoff) {
        discard;
    }

    const float renderType = GFLAG_MODEL;

    vec3 n = normalize(fs_in.normal);
    // Leaf cards are two sided, so a genuine back face wants its normal flipped.
    //
    // gl_FrontFacing is inverted for trees - the instance matrix carries a -1 x
    // scale and a mirror reverses winding - so tree.vert pre-negates the normal
    // to cancel that. Do not "fix" this by dropping the test: it is correct for
    // the two sided case, and the mirror is handled where the mirror lives.
    if (!gl_FrontFacing) {
        n = -n;
    }

    gColor = vec4(pow(albedo.rgb, vec3(1.0 / 1.3)), 0.0);
    gNormal = n * 0.5 + 0.5;
    gGMF.r = 0.15;          // gloss
    gGMF.g = 0.0;           // metal
    gGMF.b = renderType;
    gGMF.a = 0.0;
    gPosition = fs_in.worldPosition;
    gSurfaceNormals = n * 0.5 + 0.5;

#ifdef PICK_MODELS
    // The leaf cutout discards above, so picks land on visible foliage only.
    gPick = pick_id;
#endif
}
