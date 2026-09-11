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

uniform mat4  sunViewProj;   // the FULL box, as the single map
uniform int   tile_mask;
uniform float tile_texel;    // 1 / tile size
uniform float tile_pad;      // the overlap, as a fraction of the quadrant

in vec2 texCoord;
layout(location = 0) out float o_shadow;

float tap(int k, vec2 uv, float z)
{
    if (k == 0) return texture(tile0, vec3(uv, z));
    if (k == 1) return texture(tile1, vec3(uv, z));
    if (k == 2) return texture(tile2, vec3(uv, z));
    return texture(tile3, vec3(uv, z));
}

void main(void)
{
    vec3 view_pos = texelFetch(gPosition, ivec2(gl_FragCoord.xy), 0).xyz;
    vec3 world_pos = (invView * vec4(view_pos, 1.0)).xyz;

    vec4 sp = sunViewProj * vec4(world_pos, 1.0);
    sp.xyz /= sp.w;
    // xy to 0..1; z already arrives 0..1 (ClipDepthMode.ZeroToOne), as in
    // deferred.frag's single-map path.
    sp.xy = sp.xy * 0.5 + 0.5;

    // Outside the depth range or the footprint: lit, as the single map.
    if (sp.z > 1.0 || sp.z < 0.0 ||
        any(lessThan(sp.xy, vec2(0.0))) || any(greaterThan(sp.xy, vec2(1.0)))) {
        o_shadow = 1.0;
        return;
    }

    // Which quadrant, and where inside it.
    ivec2 q = ivec2(greaterThanEqual(sp.xy, vec2(0.5)));
    int k = q.x + 2 * q.y;
    if ((tile_mask & (1 << k)) == 0) {
        o_shadow = 1.0;
        return;
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
    o_shadow = s * 0.25;
}
