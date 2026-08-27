#version 450 core

// Outland albedo bake, accumulation pass - one additive draw per tile.
//
// The game ships no baked outland albedo: PBS_ext_outland samples a single
// albedo texture the engine bakes at load from the cascade tilemap and the
// map's outland tile set. This is that bake.
//
// A tilemap texel (RGBA4 nibbles) is NOT four weights: it is two layers,
//   r = tile index A,  g = tile index B,  b = weight A,  a = weight B
// (verified offline against 101_dday / 05_prohorovka: the two index nibbles
// take exactly as many distinct values as the map has tiles - 11 and 8 -
// while weight A saturates at 15 almost everywhere).
//
// Indices must never be interpolated, so the tilemap is texelFetched at the
// four surrounding texels and only the WEIGHT of the current tile is blended
// bilinearly - the weight field of one tile is linear, so that is exact.
// Accumulated with ONE,ONE blending into RGBA16F: rgb = tile.rgb * w, a = w.
// The resolve pass divides by total weight.

layout(location = 0) out vec4 accum;

layout(binding = 0) uniform sampler2D tile_map;
layout(binding = 1) uniform sampler2D tile;

uniform int   tile_index;
uniform float tile_repeats; // cascade span m / tileScale (tileScale = m per repeat)

in vec2 texCoord;

float corner_weight(ivec2 tc, ivec2 tmax)
{
    vec4 m = texelFetch(tile_map, clamp(tc, ivec2(0), tmax), 0);
    ivec4 n = ivec4(round(m * 15.0));
    return ((n.x == tile_index) ? float(n.z) : 0.0)
         + ((n.y == tile_index) ? float(n.w) : 0.0);
}

float weight_at(vec2 p, ivec2 tmax)
{
    ivec2 p0 = ivec2(floor(p));
    vec2 f = fract(p);
    return mix(mix(corner_weight(p0,               tmax),
                   corner_weight(p0 + ivec2(1, 0), tmax), f.x),
               mix(corner_weight(p0 + ivec2(0, 1), tmax),
                   corner_weight(p0 + ivec2(1, 1), tmax), f.x), f.y);
}

void main(void)
{
    ivec2 tms = textureSize(tile_map, 0);
    vec2 p = texCoord * vec2(tms) - 0.5;
    ivec2 tmax = tms - 1;

    // The 4-bit weights staircase authored ramps: adjacent texels differ by
    // whole levels, so every high-contrast tile transition bakes as a stack
    // of contour rings (the near-sheet "banding" - it is in the TEXTURE, not
    // the mesh). A one-texel tent - four bilinear taps at half-texel offsets
    // - reconstructs the smooth ramp the authors quantized, at the cost of
    // ~a texel of tile-boundary softness nothing can see at outland range.
    float w = 0.25 * (weight_at(p + vec2(-0.5, -0.5), tmax)
                    + weight_at(p + vec2( 0.5, -0.5), tmax)
                    + weight_at(p + vec2(-0.5,  0.5), tmax)
                    + weight_at(p + vec2( 0.5,  0.5), tmax));
    w /= 15.0;

    vec3 c = texture(tile, texCoord * tile_repeats).rgb;
    accum = vec4(c * w, w);
}
