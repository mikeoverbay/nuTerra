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

in VS_OUT {
    flat mat4 invMVP;
    flat mat4 invDecal;
} fs_in;


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

void main()
{
    if ( gl_FrontFacing ) discard;

    vec2 uv = gl_FragCoord.xy / resolution;
    vec3 vpos = texture(gPosition, uv).rgb;            // VIEW space
    vec4 deferred_mix = texture(gColor_in, uv);        // display-referred

    // Sky leaves gPosition at its clear value. It is the far end of every
    // ray, so it gets the full distance term rather than none.
    float dist = length(vpos);
    bool  sky  = dist < 0.001;

    // Distance: Beer-Lambert toward the tint.
    float f = sky ? 1.0 : 1.0 - exp(-fog_density * dist);

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
        vec4 dp = fs_in.invDecal * vec4(vpos, 1.0);
        vec2 loc = (dp.xy + 0.5) * vec2(uv_scale) + move_vector;
        float n = NoiseFBM(loc, 8.0, 8);               // 0..1, mean ~0.5
        f *= mix(1.0, 0.6 + 0.8 * n, fog_noise);
    }

    // Smoke and fire are in front of whatever they cover; fog them by their
    // coverage, not by the sky behind them.
    f *= 1.0 - clamp(texture(gFX_cover, uv).a, 0.0, 1.0) * fx_cover;

    // Both inputs are display-referred, so the mix is done there and the
    // tint is the sRGB colour as authored. No gamma pass on top.
    gColor = vec4(mix(deferred_mix.rgb, fog_tint_ovr, clamp(f * props.fog_level, 0.0, 1.0)),
                  deferred_mix.a);
}
