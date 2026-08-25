#version 450 core

#extension GL_ARB_shading_language_include : require

#define USE_COMMON_PROPERTIES_UBO
#define USE_TERRAIN_LAYERS_UBO
#include "common.h" //! #include "../common.h"

layout (location = 0) out vec4 gColor;
layout (location = 1) out vec4 gNormal;
layout (location = 2) out vec2 gSpecular;   // r specular, g baked horizon shadow

layout(binding = 0) uniform sampler2D global_AM;

layout(binding = 1) uniform sampler2DArray at[8];
layout(binding = 9) uniform sampler2D mixtexture[4];
layout(binding = 13) uniform sampler2D horizon_map;


uniform int page_mip;
uniform int active_layers;   // slots actually loaded; see layerMask in the notes
uniform int has_horizon;

in VS_OUT {
    vec4 Vertex;
    vec3 worldPosition;
    vec2 UV;
    vec2 Global_UV;
    flat uint map_id;
} fs_in;


//==============================================================
// texture outline stuff
float B[8];
const vec4 test_colors[8] = {
    vec4(1.0,  1.0,  0.0,  0.0),
    vec4(0.0,  1.0,  0.0,  0.0),
    vec4(0.0,  0.0,  1.0,  0.0),
    vec4(1.0,  1.0,  0.0,  0.0),
    vec4(1.0,  0.0,  1.0,  0.0),
    vec4(1.0,  0.65, 0.0,  0.0),
    vec4(1.0,  0.49, 0.31, 0.0),
    vec4(0.5,  0.5,  0.5,  0.0)
};
//==============================================================


/*===========================================================*/
// https://www.gamedev.net/articles/programming/graphics/advanced-terrain-texture-splatting-r3287/
vec4 blend(vec4 texture1, float a1, vec4 texture2, float a2) {
 float depth = 0.95;
 float ma = max(texture1.a + a1, texture2.a + a2) - depth;
 float b1 = max(texture1.a + a1 - ma, 0);
 float b2 = max(texture2.a + a2 - ma, 0);
 return (texture1 * b1 + texture2 * b2) / (b1 + b2);
 }

 //have to do this because we need the alpha in the am textures.
vec4 blend_normal(vec4 n1, vec4 n2, vec4 texture1, float a1, vec4 texture2, float a2) {
 float depth = 0.5;
 float ma = max(texture1.a + a1, texture2.a + a2) - depth;
 float b1 = max(texture1.a + a1 - ma, 0);
 float b2 = max(texture2.a + a2 - ma, 0);
 return (n1 * b1 + n2 * b2) / (b1 + b2);
 }
/*===========================================================*/

// Converion from AG map to RGB vector.
vec4 convertNormal(vec4 norm){
    vec3 n;
    n.xy = clamp(norm.ag*2.0-1.0, -1.0 ,1.0);;
    float dp = min(dot(n.xy, n.xy),1.0);
    n.z = clamp(sqrt(-dp+1.0),-1.0,1.0);
    n = normalize(n);
    n.x *= -1.0;
    return vec4(n,0.0);
}


/*===========================================================*/

// The game's own layer projection, transcribed from its terrain shader:
//
//     u = dot(U.xyz, worldPos)        (dp3 - W is IGNORED)
//     v = dot(V.xyz, worldPos)
//     uv = (u, -v) + 0.5
//
// against the REAL world position, height included. What was here before
// synthesized a flat chunk-local point instead - x recentred by 50 with z not,
// height zeroed, and the dot taken in vec4 so each layer's U.w/V.w leaked in
// as a per-layer UV shift. Three separate offsets against the game, which is
// why no combination of axis flips ever quite matched: the textures were not
// mirrored, they were displaced.
//
// worldPosition is genuine world space in this pass (chunk.modelMatrix only,
// no camera). The display mirror negates x, so undo it - the projections were
// authored for game space.
vec2 get_transformed_uv(in vec4 U, in vec4 V) {
    vec3 wp = vec3(-fs_in.worldPosition.x, fs_in.worldPosition.y, fs_in.worldPosition.z);
    return vec2(dot(U.xyz, wp), -dot(V.xyz, wp)) + 0.5;
}

vec4 crop( sampler2DArray samp, in vec2 uv , in float layer, int id)
{
    vec2  dx_vtc        = dFdx(uv*1024.0);
    vec2  dy_vtc        = dFdy(uv*1024.0);
    float delta_max_sqr = max(dot(dx_vtc, dx_vtc), dot(dy_vtc, dy_vtc));
    float mipLevel = 0.5 * log2(delta_max_sqr);

    vec2 cropped = fract(uv) * vec2(0.875, 0.875) + vec2(0.0625, 0.0625);

    //----- test texture outlines -----
    B[id] = 0.0;
    if (cropped.x < 0.065 ) B[id] = 1.0;
    if (cropped.x > 0.935 ) B[id] = 1.0;
    if (cropped.y < 0.065 ) B[id] = 1.0;
    if (cropped.y > 0.935 ) B[id] = 1.0;
    //-----

    return textureLod( samp, vec3(cropped, layer), mipLevel);
    }

vec4 crop2( sampler2DArray samp, in vec2 uv , in float layer)
{
    vec2  dx_vtc        = dFdx(uv*1024.0);
    vec2  dy_vtc        = dFdy(uv*1024.0);
    float delta_max_sqr = max(dot(dx_vtc, dx_vtc), dot(dy_vtc, dy_vtc));
    float mipLevel = 0.5 * log2(delta_max_sqr);

    vec2 cropped = fract(uv) * vec2(0.875, 0.875) + vec2(0.0625, 0.0625);

    return textureLod( samp, vec3(cropped, layer), mipLevel);
    }

vec4 crop3( sampler2DArray samp, in vec2 uv , in float layer)
{

    uv *= vec2(0.125, 0.125);

    vec2  dx_vtc        = dFdx(uv*1024.0);
    vec2  dy_vtc        = dFdy(uv*1024.0);
    float delta_max_sqr = max(dot(dx_vtc, dx_vtc), dot(dy_vtc, dy_vtc));

    float mipLevel = 0.5 * log2(delta_max_sqr);

    //uv += vec2(offset.x , offset.y);
    vec2 cropped = fract(uv)* vec2(0.875, 0.875) + vec2(0.0625, 0.0625);

    return textureLod( samp, vec3(cropped, layer), mipLevel);
    }


void main(void)
{
    const vec2 mix_coords = vec2(1.0 - fs_in.UV.x, fs_in.UV.y);

    float Mix[8];
    Mix[0] = texture(mixtexture[0], mix_coords.xy).a;
    Mix[1] = texture(mixtexture[0], mix_coords.xy).g;
    Mix[2] = texture(mixtexture[1], mix_coords.xy).a;
    Mix[3] = texture(mixtexture[1], mix_coords.xy).g;

    Mix[4] = texture(mixtexture[2], mix_coords.xy).a;
    Mix[5] = texture(mixtexture[2], mix_coords.xy).g;
    Mix[6] = texture(mixtexture[3], mix_coords.xy).a;
    Mix[7] = texture(mixtexture[3], mix_coords.xy).g;

    // Slots past the ones this chunk actually loaded carry a zeroed projection,
    // which samples one fixed texel of whatever happens to be bound, and the
    // blend map channel behind them is not necessarily zero. Mask them out - the
    // game does the same thing with layerMask, and layer_count had been tracked
    // by the loader all along without anything ever reading it.
    for (int i = 0; i < 8; ++i) {
        if (i >= active_layers) {
            Mix[i] = 0.0;
        }
    }

    const vec4 global = texture(global_AM, fs_in.Global_UV);

    vec4 t[8];      // am map
    vec4 mt[8];     // am macro 
    float mth[8];   // macro height in alpha
    float th[8];    // am height
    vec4 n[8];      // normal map
    vec4 mn[8];     // macro normal map
    float sw[8];    // splat weight, normalised
    float pv[8];    // height * splat, the height-blend contender
    float ssum = 0.0;

    float height = 0.0;
    for (int i = 0; i < 8; ++i) {
        // create UV projections
        const vec2 tuv = get_transformed_uv(L.U[i], L.V[i]); 

        // Get AM maps,crop and set Test outline blend flag
        t[i] = crop(at[i], tuv, 0.0, i);

        mt[i] = crop3(at[i], tuv, 2.0);

    //u_xlat10 = max(u_xlat10, vec4(0.00392156886, 0.00392156886, 0.00392156886, 0.00392156886));
        mth[i] = max(mt[i].w,0.00392156886);

    //u_xlat14.xyz = u_xlat12.xyz;
        vec3 tv = mt[i].xyz;

    //u_xlat14.xyz = clamp(u_xlat14.xyz, 0.0, 1.0);
        tv = clamp(tv, vec3(0.0), vec3(1.0));

    //u_xlat14.xyz = (-u_xlat12.xyz) + u_xlat14.xyz;
        tv = -mt[i].xyz + tv;

    //u_xlat12.xyz = g_blockDataPS[1].blendMacroInfluence[3].xxx * u_xlat14.xyz + u_xlat12.xyz;
        mt[i].xyz = L.r2[i].xxx * tv + mt[i].xyz;

        // specular is in red channel of the normal maps.
        // Ambient occlusion is in the Blue channel.
        // Green and Alpha are normal values.
        n[i] = crop2(at[i], tuv, 1.0);
        mn[i] = crop3(at[i], tuv, 3.0);

        // get the ambient occlusion
        t[i].rgb *= n[i].b;
        mt[i].rgb *= mn[i].b;

        // Mix macro. The per-layer constants are the close-up blend; on top of
        // that the game fades the whole thing toward pure macro as a page's mip
        // rises (g_vtTileParams.w), so distant pages lose the micro detail
        // entirely instead of tiling it. macro_fade of 0 keeps the old
        // behaviour at every distance.
        float macro_blend = clamp(float(page_mip) * props.macro_fade, 0.0, 1.0);

        // L.r2[i].x is the game's blendMacroInfluence: how much of the macro
        // texture shows through at close range. Fishing Bay reads 0, 0.1, 0.04,
        // 0.06 across its layers - small, as an influence should be.
        //
        // This used to be micro * min(r2.x, 1) + macro * (r2.y + 1). With
        // r2 = (0, 0) that is 0 * micro + 1 * macro - pure macro, sampled by
        // crop3 at uv * 0.125, so every layer rendered as its own macro texture
        // at eight times the scale. On Grass_Lawn_Green_33 that turned poppies
        // into red patches across the whole map.
        float macro_inf = clamp(L.r2[i].x, 0.0, 1.0);

        vec3 micro_rgb = mix(t[i].rgb, mt[i].rgb, macro_inf);
        t[i].rgb = mix(micro_rgb, mt[i].rgb, macro_blend);

        // All four channels of the normal map, not just rgb.
        //
        // This is an AG normal map: convertNormal reads .ag, so X lives in
        // ALPHA and Y in green - with .r specular and .b ambient occlusion.
        // Blending only .rgb left the alpha untouched, so at distance, where
        // macro_blend reaches 1, the normal's Y came from the macro texture
        // while its X still came from the micro one. Two halves of two
        // different normals, normalized together into a direction that is
        // neither. Specular rode along in .r with the same split, and the game
        // lerps gloss micro-to-macro exactly like the normal.
        vec4 micro_n = mix(n[i], mn[i], macro_inf);
        n[i]         = mix(micro_n, mn[i], macro_blend);

        // the game lerps the height the same way, using the macro alpha
        t[i].a = mix(t[i].a, mth[i], macro_blend);

        ssum += Mix[i];
    }

    // Height blend, transcribed from the game's own VT baker
    // (shaders/terrain/terrain2_5_virtual_texture, blob 13):
    //
    //     s = splat / sum(splat)              normalise the splat weights first
    //     p = max(height, 1/255) * s          contender is the PRODUCT
    //     ma = max over all 8 of p
    //     w = max(p + blendHeight - ma, 0) * s    splat applied a second time
    //     w /= sum(w)
    //
    // The second splat multiply is what keeps an unpainted layer out no matter
    // how tall its height map is, so no explicit gate is needed. The 1/255 floor
    // on height is the same one the outland shader uses, and the reason the dead
    // mth[] line in this file has that constant in it.
    //
    // What was here before was Mix[i] *= t[i].a + bias, pow(Mix, 1/0.7),
    // normalise - a plain weighted average. Every painted layer contributed in
    // proportion always, so two textures interpenetrated across the whole
    // transition instead of meeting where their height maps cross.
    ssum = max(ssum, 1e-6);

    float ma = 0.0;
    for (int i = 0; i < 8; ++i) {
        sw[i] = Mix[i] / ssum;
        // height_contrast is ours, not the game's - it runs at 1.0 by default,
        // which is exactly the formula above. Below 1 lifts mid heights toward
        // 1 so the splat dominates and the winning texture stops sitting so
        // heavy across a transition; above 1 makes the boundary hug the relief.
        pv[i] = pow(max(t[i].a, 0.00392156886), props.height_contrast) * sw[i];
        ma = max(ma, pv[i]);
    }

    float f = 0.0;
    for (int i = 0; i < 8; ++i) {
        Mix[i] = max(pv[i] + props.blend_height - ma, 0.0) * sw[i];
        f += Mix[i];
    }
    f = max(f, 1e-6);

    vec4 base = vec4(0.0);

    // The winning layer, for the normal. The game does not blend normals at
    // all: it runs an argmax over the blend weights and takes a single normal
    // sample from whichever layer won, while albedo blends across all of them.
    //
    // Averaging eight normal maps pulls every one of them toward the mean, which
    // is flat - so the surface loses its relief exactly where two textures meet,
    // and the terrain reads soft and washed out even when the albedo is right.
    int win = 0;
    float win_w = -1.0;

    for (int i = 0; i < 8; ++i) {
        Mix[i] /= f;

        base += t[i] * Mix[i];

        if (Mix[i] > win_w) {
            win_w = Mix[i];
            win = i;
        }

        // Displacement, weighted the same way the colour is. The game does
        // not use the raw height: each layer authors a remap -
        //
        //     h' = min(h^r1.z, 1) * r1.x + r1.y
        //
        // (its VT baker runs exactly this as log/mul/exp/mad against the
        // microDisplacement constants). The gamma is what lets a layer say
        // "only my deep grooves displace" or "everything above the base
        // does". Guarded: a zero gamma would make pow() blow up, and a layer
        // authored without a remap falls back to the old linear scale.
        if (L.r1[i].z > 0.001) {
            height += (min(pow(t[i].a, L.r1[i].z), 1.0) * L.r1[i].x + L.r1[i].y) * Mix[i];
        } else {
            height += t[i].a * Mix[i] * L.r1[i].x;
        }
    }

    // The dominant layer supplies the whole surface response, not just the
    // normal: .ag is the normal, .r the specular that reaches gSpecular, .b
    // the ambient occlusion. Taking them from one layer keeps them consistent
    // with each other - a normal from one texture lit with another's specular
    // is the kind of mismatch that reads as "wrong material" without ever
    // looking obviously broken.
    vec4 out_n = n[win];

    // global
    float c_l = length(base.rgb) + base.a + global.a+0.25;
    float g_l = length(global.rgb) - global.a-base.a;

    // rem to remove global content
    base.rgb = (base.rgb * c_l + global.rgb * g_l) / 1.8;

    // wetness
    base = blend(base, base.a+0.75, vec4(props.waterColor, props.waterAlpha), global.a);

    // Texture outlines
    if (props.show_test_textures) {
        for (int i = 0; i < 8; ++i) {
            base = mix(base, base + test_colors[i], B[i] * Mix[i]);
        }
    }

    // Baked terrain self shadowing, straight into the page so it costs nothing
    // per frame. terrain2/horizonshadows stores a horizon angle per texel, which
    // is why the game can keep it across a day/night cycle - we use it as a flat
    // large scale shading term for now.
    float horizon = 1.0;
    if (has_horizon != 0) {
        horizon = texture(horizon_map, mix_coords).r;
    }

    // The map-wide sun shadow used to be sampled here and folded into
    // gSpecular.g at page bake time. It now lives in deferred.frag instead.
    //
    // A page is baked once, long before anything is drawn on top of it, so a
    // shadow written here lands ahead of the projected decals - and ahead of the
    // ambient/direct split, which is the half that made shade read as black.
    // Sampling in the final render puts it after both, and reaches the static
    // models as well, which a terrain page never could.
    gSpecular = vec2(out_n.r, horizon);

    //gColor = gColor* 0.001 + r1_8;
    gColor.rgb = base.rgb;
    // Wetness, and the reason this channel exists.
    //
    // The global AM's alpha is a wetness mask - the game samples exactly this
    // and nothing else uses it. Subtracting the blended layer height is what
    // makes water pool: it collects in the low parts of the surface relief
    // instead of coating it evenly, so a cobbled road goes wet between the
    // stones rather than over them. The 0.4 is the game's constant.
    //
    // NOTE: if the global AM is ever loaded as BC1 this is meaningless - DXT1
    // has no usable alpha, which is why the game gates the whole term on a
    // g_dxt1FormatGlobalAM flag and forces wetness to zero when it is set.
    //
    // The flatness half of the mask is applied in TerrainLQ/HQ, where the
    // terrain's own surface normal is available; a page has no idea what slope
    // it will be pasted onto.
    gColor.a = clamp(global.a - 0.4 * height, 0.0, 1.0);

    gNormal.xyz = normalize(convertNormal(out_n).xyz) * 0.5 + 0.5;
    gNormal.w = height;
}
