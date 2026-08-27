#version 450 core

#extension GL_ARB_shading_language_include : require

#define USE_PERVIEW_UBO
#define USE_COMMON_PROPERTIES_UBO
#define USE_MIPLEVEL_FUNCTION
#define USE_VT_FUNCTIONS
#include "common.h" //! #include "../common.h"


layout (location = 0) out vec4 gColor;
layout (location = 1) out vec3 gNormal;
layout (location = 2) out vec4 gGMF;
layout (location = 3) out vec3 gPosition;
layout (location = 4) out vec3 gSurfaceNormal;


layout(binding = 0) uniform usampler2D PageTable;
layout(binding = 1) uniform sampler2DArray ColorTextureAtlas;
layout(binding = 2) uniform sampler2DArray NormalTextureAtlas;
layout(binding = 3) uniform sampler2DArray SpecularTextureAtlas;

uniform int vt_debug;
uniform float vt_debug_mix;
uniform int vt_debug_mode;   // 0 = page colours, 1 = mip-blend greyscale
uniform int vt_nearest_mip;  // test lever: snap the trilinear blend off


in VS_OUT {
    mat3 TBN;
    vec3 worldPosition;
    vec2 Global_UV;
    vec3 worldNormal;
} fs_in;


void main(void)
{
    // CALC MIP LEVEL
    float miplevel = MipLevel(fs_in.Global_UV, props.VirtualTextureSize);
    miplevel = clamp(miplevel, 0, log2(props.PageTableSize) - 1);

    const float mip1 = floor(miplevel);
    const float mip2 = mip1 + 1;
    // FRACTAL PART OF MIPLEVEL. The nearest-mip lever exists because coarse
    // pages are baked independently and adjacent mips can pick different
    // layer mixes - sweeping this blend during a camera glide morphs the far
    // field ("settling-in" shading flicker). Snapping isolates that carrier.
    float mipfract = miplevel - mip1;
    if (vt_nearest_mip != 0) mipfract = mipfract < 0.5 ? 0.0 : 1.0;

    // GET PAGES FOR TRILINEAR FILTERING
    // PAGE1 : MIP1
    // PAGE2 : MIP1 + 1
    const uvec2 page1 = SampleTable(PageTable, fs_in.Global_UV, mip1);
    const uvec2 page2 = SampleTable(PageTable, fs_in.Global_UV, mip2);

    // TRILINEAR FILTERING BETWEEN MIP1 AND MIP2
    const vec4 color_sample1 = SampleAtlas(ColorTextureAtlas, page1, fs_in.Global_UV);
    const vec4 color_sample2 = SampleAtlas(ColorTextureAtlas, page2, fs_in.Global_UV);
    gColor = mix(color_sample1, color_sample2, mipfract);

    // TRILINEAR FILTERING BETWEEN MIP1 AND MIP2
    const vec3 n_sample1 = SampleAtlas(NormalTextureAtlas, page1, fs_in.Global_UV).xyz;
    const vec3 n_sample2 = SampleAtlas(NormalTextureAtlas, page2, fs_in.Global_UV).xyz;
    gNormal.xyz = normalize(fs_in.TBN * (mix(n_sample1, n_sample2, mipfract) * 2.0 - 1.0)) * 0.5 + 0.5;

    // TRILINEAR FILTERING BETWEEN MIP1 AND MIP2
    const vec2 spec_sample1 = SampleAtlas(SpecularTextureAtlas, page1, fs_in.Global_UV).rg;
    const vec2 spec_sample2 = SampleAtlas(SpecularTextureAtlas, page2, fs_in.Global_UV).rg;
    const float specular_sample1 = spec_sample1.r;
    const float specular_sample2 = spec_sample2.r;

    // The map-wide sun shadow used to be applied here, as gColor.rgb *=
    // horizon_shade. That was wrong in two ways: albedo feeds the ambient term
    // as well as the direct one, so it darkened the sky fill that should still
    // be there in shade, and being a terrain shader it could never reach the
    // static models. It now lives in deferred.frag as baked_sun_shadow(), which
    // multiplies the sun term only and covers models as well.
    //
    // .g of the specular atlas still carries the game's own horizon angle data
    // from t_mixer, which is inert today (build_horizon_texture returns Nothing,
    // so has_horizon is 0 and the value is a flat 1.0). Left in place for when
    // that format is cracked; deliberately not multiplied into albedo either.
    // Wetness mask, .a - the slot the G-buffer has always documented as
    // "Wetness in a" and terrain has always written as zero.
    //
    // The page carries the height-shaped half in its alpha (see t_mixer). The
    // other half is slope: water does not cling to a hillside. The game gates
    // on the terrain normal's up component with a very sharp curve - full
    // wetness only on near-level ground, everything else floored at 0.6.
    //
    // fs_in.worldNormal is a misnomer, like every other one in this renderer:
    // normalMatrix is view * model, so it arrives in VIEW space. invView takes
    // it back to world, where .y is actually up.
    const vec3 n_world = normalize(mat3(invView) * normalize(fs_in.worldNormal));
    const float flatness = max((n_world.y - 0.99) * 100.0, 0.6);
    const float wetness = clamp(gColor.a * flatness, 0.0, 1.0);

    gGMF = vec4(0.2, mix(specular_sample1, specular_sample2, mipfract), GFLAG_TERRAIN, wetness);

    gPosition = fs_in.worldPosition;
    // gSurfaceNormal is Rgb8, so it has to carry a 0..1 encoding - writing the
    // raw signed normal clamps every negative component away.
    gSurfaceNormal = normalize(fs_in.worldNormal) * 0.5 + 0.5;

    // VT page debug (Settings -> VT, toggled from the key window): tint the
    // albedo by the resident page's colour but leave the normal/spec writes
    // and the lighting path alone, so whatever is flickering keeps flickering
    // visibly under the tint.
    if (vt_debug != 0) {
        vec3 dbg = (vt_debug_mode == 1) ? vec3(mipfract)
                                        : VTDebugColor(page1, fs_in.Global_UV);
        gColor.rgb = mix(gColor.rgb, dbg, vt_debug_mix);
    }
}
