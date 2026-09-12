#version 450 core
#extension GL_ARB_shading_language_include : require
// The PerView block (invView) in common.h is behind this define, as
// deferred.frag sets it; without it the block is compiled out and invView
// does not exist - C1503, and the whole resolve fails to compile.
#define USE_PERVIEW_UBO
#include "common.h" //! #include "../common.h"

// THE SUN SHADOW FROM FOUR TILES, resolved once a frame.
//
// MapSunShadow bakes the sun's fitted ortho box as a 2 x 2 grid of depth
// tiles, each a quadrant of the box at its own full resolution. This pass
// projects every pixel's position into the FULL box with the same matrix the
// single map used, reads which quadrant it landed in, maps into that tile
// (allowing for the overlap each tile was baked with), takes the same four
// taps the single map takes, and writes the result to a screen-sized R8.
// deferred.frag then reads one texel and knows nothing about tiles.
//
// tile_mask says which tiles the view frustum touches this frame; a pixel
// whose tile is not on it is lit and costs nothing.

layout(binding = 0) uniform sampler2D gPosition;        // VIEW space
layout(binding = 1) uniform sampler2DShadow tile0;      // quadrant x0 y0
layout(binding = 2) uniform sampler2DShadow tile1;      // x1 y0
layout(binding = 3) uniform sampler2DShadow tile2;      // x0 y1
layout(binding = 4) uniform sampler2DShadow tile3;      // x1 y1

// THE TANKS, one 512 layer each, rebuilt every frame by MapTankShadow.
//
// The sun map is baked once and cannot hold anything that moves, so a tank
// casts from its own map or not at all. Combined here rather than in
// deferred.frag because this pass already turns a world position into a shadow
// factor, and everything downstream reads what it writes - so a tank shadow
// lands on terrain, models and trees without any of them knowing.
layout(binding = 5) uniform sampler2DArrayShadow tank_maps;

#define MAX_TANK_CASTERS 32
uniform int   tank_count;
uniform mat4  tank_vp[MAX_TANK_CASTERS];
// xyz the tank's centre, w the radius outside which this layer cannot possibly
// shadow anything. The whole projection is skipped on that test - the owner's
// "doing the math for them stopped if they are out of range".
uniform vec4  tank_sphere[MAX_TANK_CASTERS];
uniform float tank_fade_near;   // metres: full strength up to here
uniform float tank_fade_far;    // metres: gone by here
uniform vec3  tank_eye;
// DEBUG: write the TANK factor alone into R instead of combining it with the
// baked map, so deferred.frag can paint exactly where tanks shadow and nowhere
// else. Without it a tank shadow falling inside a building's shadow is
// invisible - min() has already taken the darker - which is the one case you
// most want to see while getting this working.
uniform int   tank_debug;

uniform mat4  sunViewProj;   // the FULL box, as the single map
uniform int   tile_mask;
uniform float tile_texel;    // 1 / tile size
uniform float tile_pad;      // the overlap, as a fraction of the quadrant

in vec2 texCoord;

// TWO CHANNELS: the factor, and WHICH TILE GAVE IT.
//
// R is the shadow factor, exactly as before. G says which of the four tiles
// this pixel was sampled from, so the deferred pass can tint by it and the
// four can be SEEN to be in use - the alternative is trusting a mask in a log
// and a seam nobody can point at.
//
// Encoded as (k + 1) / 4, so 0 means "no tile" - lit, out of range, or a tile
// the frustum does not touch - and the four used values land exactly on 64,
// 128, 192 and 255 of a byte. Decoding is int(g * 4 + 0.5) - 1.
layout(location = 0) out vec2 o_shadow;

const float NO_TILE = 0.0;

float tap(int k, vec2 uv, float z)
{
    if (k == 0) return texture(tile0, vec3(uv, z));
    if (k == 1) return texture(tile1, vec3(uv, z));
    if (k == 2) return texture(tile2, vec3(uv, z));
    return texture(tile3, vec3(uv, z));
}

// The baked map's answer, unchanged. Lifted into a function so its three
// early-outs stay early-outs while main can still apply the tanks afterwards -
// a tank standing on ground the sun map does not cover, or on a tile the
// frustum does not touch, must still cast.
float baked_factor(vec3 world_pos, out float tile_id)
{
    tile_id = NO_TILE;

    vec4 sp = sunViewProj * vec4(world_pos, 1.0);
    sp.xyz /= sp.w;
    // xy to 0..1; z already arrives 0..1 (ClipDepthMode.ZeroToOne), as in
    // deferred.frag's single-map path.
    sp.xy = sp.xy * 0.5 + 0.5;

    // Outside the depth range or the footprint: lit, as the single map.
    if (sp.z > 1.0 || sp.z < 0.0 ||
        any(lessThan(sp.xy, vec2(0.0))) || any(greaterThan(sp.xy, vec2(1.0)))) {
        return 1.0;
    }

    // Which quadrant, and where inside it.
    ivec2 q = ivec2(greaterThanEqual(sp.xy, vec2(0.5)));
    int k = q.x + 2 * q.y;
    if ((tile_mask & (1 << k)) == 0) {
        return 1.0;
    }
    vec2 f = sp.xy * 2.0 - vec2(q);                    // 0..1 across the quadrant
    // The tile was baked over the quadrant plus tile_pad each side, so the
    // quadrant occupies the middle of the texture.
    vec2 uv = (f + tile_pad) / (1.0 + 2.0 * tile_pad);

    // Four taps a texel apart, the same filter as the single map.
    float s = 0.0;
    s += tap(k, uv + vec2(-tile_texel, -tile_texel), sp.z);
    s += tap(k, uv + vec2( tile_texel, -tile_texel), sp.z);
    s += tap(k, uv + vec2(-tile_texel,  tile_texel), sp.z);
    s += tap(k, uv + vec2( tile_texel,  tile_texel), sp.z);
    tile_id = float(k + 1) * 0.25;
    return s * 0.25;
}

// What the tanks cast at this point. 1.0 is nothing.
float tank_factor(vec3 world_pos)
{
    if (tank_count == 0) return 1.0;

    // FADE BY DISTANCE FROM THE EYE, decided once rather than per tank: a
    // shadow that switched off the instant a tank crossed the range boundary
    // would pop, and it is the viewer's distance that decides whether any of
    // this is visible at all.
    float d = distance(world_pos, tank_eye);
    float vis = 1.0 - smoothstep(tank_fade_near, tank_fade_far, d);
    if (vis <= 0.0) return 1.0;

    float lit = 1.0;
    for (int i = 0; i < tank_count; ++i) {
        // THE EARLY OUT. Outside this tank's sphere there is nothing it could
        // shadow here, so the matrix multiply, the divide and the fetch are all
        // skipped. With thirty tanks on a map most pixels take this branch for
        // most of them, which is what makes one map per tank affordable.
        vec3  c = tank_sphere[i].xyz;
        float r = tank_sphere[i].w;
        if (dot(world_pos - c, world_pos - c) > r * r) continue;

        vec4 p = tank_vp[i] * vec4(world_pos, 1.0);
        p.xyz /= p.w;
        p.xy = p.xy * 0.5 + 0.5;
        if (p.z > 1.0 || p.z < 0.0 ||
            any(lessThan(p.xy, vec2(0.0))) || any(greaterThan(p.xy, vec2(1.0)))) continue;

        // A hardware comparison fetch, as the tiles use: the depth test and the
        // filter in one. Bias is in the polygon offset at bake time, not here.
        lit = min(lit, texture(tank_maps, vec4(p.xy, float(i), p.z)));
    }

    // Fade toward unshadowed rather than toward black.
    return mix(1.0, lit, vis);
}

void main(void)
{
    vec3 view_pos  = texelFetch(gPosition, ivec2(gl_FragCoord.xy), 0).xyz;
    vec3 world_pos = (invView * vec4(view_pos, 1.0)).xyz;

    float tile_id;
    float s = baked_factor(world_pos, tile_id);

    // Darkest wins. A point already in a building's shadow cannot be made
    // lighter by a tank standing clear of it.
    float tf = tank_factor(world_pos);
    if (tank_debug != 0) {
        o_shadow = vec2(tf, tile_id);
        return;
    }
    o_shadow = vec2(min(s, tf), tile_id);
}
