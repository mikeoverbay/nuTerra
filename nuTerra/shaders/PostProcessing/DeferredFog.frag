#version 450 core

#extension GL_ARB_shading_language_include : require

#define USE_PERVIEW_UBO
#define USE_COMMON_PROPERTIES_UBO
#include "common.h" //! #include "../common.h"

layout (location = 0) out vec4 gColor;

layout (binding = 0) uniform sampler2D noiseMap;
layout (binding = 1) uniform sampler2D depthMap;
layout (binding = 2) uniform sampler2D gPosition;
layout (binding = 3) uniform sampler2D gColor_in;
// The FX accumulation (smoke, fire): premultiplied colour, coverage in alpha.
// FX cards write colour but no position, so over the dome they inherited the
// sky's full fog and vanished. Where the FX cover a pixel, the fog is scaled
// back by that coverage - the card keeps its own look, the sky behind it
// still fogs. fx_cover is 0 on frames where the FX block did not run, so a
// stale buffer is never read.
layout (binding = 4) uniform sampler2D gFX_cover;
uniform float fx_cover;

uniform float uv_scale;
uniform float time;
uniform vec2 move_vector;

// The fog's shape. Computed HERE from gPosition, not read from the frame's
// alpha: that alpha travelled through the window's back buffer, which has no
// alpha bits, so it arrived as 1.0 and the pass was a flat gamma lift with no
// depth in it. Measured: fog_level 0.55 and 1.0 rendered the same frame.
uniform float fog_density;   // per metre, distance term 1 - exp(-d * dist)
uniform float fog_height;    // metres above the floor to thin to 1/e
uniform float fog_floor;     // world Y, resolved by the caller from MEAN + offset
uniform float fog_noise;     // 0..1, how much the drifting noise patches it
uniform vec3  fog_tint_ovr;  // sRGB; the map's colour unless overridden
// How much fog the SKY gets, 0..1, times fog_level. The sky is the far end of
// every ray, but replacing the dome outright wiped the sky texture; this
// mixes the tint over it and leaves the sky showing through.
uniform float fog_sky;
// Size of the noise, metres per cell. Small billows at street scale, large
// rolls across the map.
uniform float fog_noise_m;

// Sub-LSB dither amplitude in DESTINATION LSBs, 0 to disable. See the note at
// the write in main().
uniform float dither_amt;

// One value per pixel, from the pixel's position and nothing else.
//
// Deliberately NOT animated. A still has to be reproducible - two runs at the
// same cam= must differ only by the thing being tested - and a time term makes
// every capture its own image. Same reasoning as lamp_fog's Bayer.
float hash12(vec2 p)
{
    vec3 p3 = fract(vec3(p.xyx) * 0.1031);
    p3 += dot(p3, p3.yzx + 33.33);
    return fract((p3.x + p3.y) * p3.z);
}


const vec3 tr = vec3 (0.5 ,0.5 , 0.5);
const vec3 bl = vec3(-0.5, -0.5, -0.5);

void clip(vec3 v) {
    if (v.x > tr.x || v.x < bl.x ) discard;
    if (v.y > tr.y || v.y < bl.y ) discard;
    if (v.z > tr.z || v.z < bl.z ) discard;
}
//----------------------------------------------------------------------------------------
float Hash(in vec2 p, in float scale)
{
    // This is tiling part, adjusts with the scale...
    p = mod(p, scale);
    return fract(sin(dot(p, vec2(35.6898, 24.3563))) * 353753.373453);
}


//----------------------------------------------------------------------------------------
float cellNoise(in vec2 x, in float scale )
{
    x *= scale;

    vec2 p = floor(x);
    vec2 f = fract(x);
    f = f*f*(3.0-2.0*f);
    //f = (1.0-cos(f*3.1415927)) * .5;
    float res = mix(mix(Hash(p,  scale ),
        Hash(p + vec2(1.0, 0.0), scale), f.x),
        mix(Hash(p + vec2(0.0, 1.0), scale),
        Hash(p + vec2(1.0, 1.0), scale), f.x), f.y);
    return res;
}

//----------------------------------------------------------------------------------------
float NoiseFBM(in vec2 p, float numCells, int octaves)
{
    float f = 0.0;
    
    // Change starting scale to any integer value...
    p = mod(p, vec2(numCells));
    float amp = 0.5;
    float sum = 0.0;
    
    for (int i = 0; i < octaves; i++)
    {
        f += cellNoise(p, numCells) * amp;
        sum += amp;
        amp *= 0.5;

        // numCells must be multiplied by an integer value...
        numCells *= 2.0;
    }

    return f / sum;
}

// 3D value noise. The 2D field varied over the ground plane only, so every
// point up a wall shared one value and facades striped vertically. Three
// axes, trilinear, four octaves; the scroll moves it along XZ.
float fog_noise_hash3(vec3 p)
{
    return fract(sin(dot(p, vec3(35.6898, 24.3563, 51.2117))) * 353753.373453);
}

float fog_noise_cell3(vec3 x)
{
    vec3 p = floor(x);
    vec3 f = fract(x);
    f = f * f * (3.0 - 2.0 * f);
    float c000 = fog_noise_hash3(p), c100 = fog_noise_hash3(p + vec3(1, 0, 0));
    float c010 = fog_noise_hash3(p + vec3(0, 1, 0)), c110 = fog_noise_hash3(p + vec3(1, 1, 0));
    float c001 = fog_noise_hash3(p + vec3(0, 0, 1)), c101 = fog_noise_hash3(p + vec3(1, 0, 1));
    float c011 = fog_noise_hash3(p + vec3(0, 1, 1)), c111 = fog_noise_hash3(p + vec3(1, 1, 1));
    return mix(mix(mix(c000, c100, f.x), mix(c010, c110, f.x), f.y),
               mix(mix(c001, c101, f.x), mix(c011, c111, f.x), f.y), f.z);
}

// p in cells. Same base scale as the 2D field had (32 cells per noise_m), so
// a tuned map keeps its look; the octaves halve in size and weight.
float fog_fbm3(vec3 p)
{
    float f = 0.0, amp = 0.5, sum = 0.0;
    for (int i = 0; i < 4; i++)
    {
        f += fog_noise_cell3(p) * amp;
        sum += amp;
        amp *= 0.5;
        p *= 2.0;
    }
    return f / sum;
}

void main()
{
    vec2 uv = gl_FragCoord.xy / resolution;
    vec3 vpos = texture(gPosition, uv).rgb;            // VIEW space
    vec4 deferred_mix = texture(gColor_in, uv);        // display-referred

    // Sky leaves gPosition at its clear value. It is the far end of every
    // ray, so it gets the full distance term rather than none.
    float dist = length(vpos);
    bool  sky  = dist < 0.001;

    // Distance: Beer-Lambert toward the tint. The sky takes fog_sky instead
    // of the full term, so the dome shows through the haze.
    float f = sky ? fog_sky : 1.0 - exp(-fog_density * dist);

    // Height: full below the floor, thinning by fog_height above it. The ray
    // is judged at its end point; for a ground-hugging camera that is the
    // surface, which is where the fog sits.
    float wy = sky ? fog_floor : (invView * vec4(vpos, 1.0)).y;
    f *= exp(-max(0.0, wy - fog_floor) / max(fog_height, 1.0));

    // Drift. The old pass multiplied a fractal cloud by six and mixed it in
    // as colour, which is what read as cloudy patchiness. Now a gentle
    // modulation of the amount, off the sky, and adjustable down to zero.
    if (fog_noise > 0.0 && !sky)
    {
        // World XZ, in units of ~350 m per noise cell so the drift reads at
        // street scale rather than per-metre grain.
        // Along the RAY, not at the surface. Sampled only where the ray ends,
        // the field was painted onto the walls as mottle - fog with a wall's
        // shape. The banks live in the air between the eye and the wall, so
        // the amount is the average of the field over that path: four points
        // from a fifth of the way out to the surface.
        vec3 wp = (invView * vec4(vpos, 1.0)).xyz;
        vec3 scroll = vec3(move_vector.x, 0.0, move_vector.y) * 8.0;
        float k = (uv_scale * 8.0) / max(fog_noise_m, 1.0);
        // FIXED-LENGTH steps anchored at the surface, walking back toward the
        // eye. Four samples at fractions of the path slid through the field
        // whenever the camera moved or zoomed, and the pattern swam. Anchored
        // at the surface with a set stride, a sample never moves - it is only
        // added or dropped at the near end as the distance changes. 10 m a
        // step, up to 8: past 80 m the fog is saturated at any useful density
        // and the field there cannot show.
        const float STEP_M = 10.0;
        int count = int(clamp(floor(dist / STEP_M), 1.0, 8.0));
        vec3 back = normalize(cameraPos - wp) * STEP_M;
        float n = 0.0;
        for (int i = 0; i < count; ++i)
            n += fog_fbm3((wp + back * (float(i) + 0.5)) * k + scroll);
        n /= float(count);                               // 0..1, mean ~0.5
        // 0.4 .. 1.6 at full strength: billows, not a tremor. Mean stays 1.
        f *= mix(1.0, 0.4 + 1.2 * n, fog_noise);
    }

    // Smoke and fire are in front of whatever they cover; fog them by their
    // coverage, not by the sky behind them.
    f *= 1.0 - clamp(texture(gFX_cover, uv).a, 0.0, 1.0) * fx_cover;

    // Both inputs are display-referred, so the mix is done there and the
    // tint is the sRGB colour as authored. No gamma pass on top.
    vec3 out_rgb = mix(deferred_mix.rgb, fog_tint_ovr, clamp(f * props.fog_level, 0.0, 1.0));

    // Sub-LSB dither, because everything from here on is 8 bit.
    //
    // gColor is Rgba8 and the frame round trips through the 8 bit default back
    // buffer more than once (perform_SSAA_Pass, then copy_default_to_gColor
    // before the base rings and again before this pass). Nothing in that chain
    // dithers, and smoke is the one thing that cannot survive it - a wide,
    // smooth, low amplitude gradient is exactly what 256 levels turns into
    // contour bands. It looks perfect in the gFX_HDR viewer right up to the
    // moment it lands in 8 bits, which is why the banding read as a bloom
    // problem when the bloom has no smoke in it at all.
    //
    // HERE and not in fx_composite: that pass BLENDS premultiplied colour, so
    // a dither on its rgb breaks the rgb = colour * coverage invariant and
    // lands as additive noise on every pixel the FX never covered. This pass
    // writes gColor outright and owns the whole pixel, so the amount is in the
    // destination's own units and nothing else has to hold.
    //
    // Triangular PDF, not uniform: uniform dither leaves the noise floor
    // modulated by the signal, TPDF at 1 LSB does not.
    if (dither_amt > 0.0) {
        float r1 = hash12(gl_FragCoord.xy);
        float r2 = hash12(gl_FragCoord.xy + 17.0);
        out_rgb += (r1 + r2 - 1.0) * (dither_amt / 255.0);
    }

    gColor = vec4(out_rgb, deferred_mix.a);
}
