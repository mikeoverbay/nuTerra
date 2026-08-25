#version 450 core

#extension GL_ARB_shading_language_include : require

#define USE_PERVIEW_UBO
#include "common.h" //! #include "../common.h"

// Forward water over the lit frame. Deep colour, fresnel curve and reflection
// tint are the AUTHORED per-body values out of BWWa, not invented constants -
// the same numbers the game's own water reads.
//
// Reflection is the sky cube for now. The game uses baked planar probes
// (environments/<env>/probes/water/*_pmrem.dds); those and SSR-fed reflection
// are the follow-up, this is the credible baseline.

layout(binding = 0) uniform sampler2D rippleA;   // frame i of the 8-frame loop
layout(binding = 1) uniform sampler2D rippleB;   // frame i+1
layout(binding = 2) uniform samplerCube sky;
layout(binding = 3) uniform sampler2DShadow sun_shadow_map;
layout(binding = 4) uniform sampler2D lit_scene;   // the resolved frame (gColor)
layout(binding = 5) uniform sampler2D scene_pos;   // gPosition - VIEW space
layout(binding = 6) uniform sampler2D scene_gmf;   // .b carries the surface flag
layout(binding = 7) uniform sampler2D scene_nrm;   // view space, 0..1 encoded

uniform float exclude_band;

uniform float frame_lerp;
uniform vec4 deep_color;
uniform vec2 fresnel;       // x bias, y exponent
uniform vec2 sun_glint;     // x exponent, y scale - authored per body at +0xB0
uniform vec3 sun_tint;      // +0xE0 - colours the GLINT, never the sky
uniform vec3 sun_dir;
uniform mat4 sunViewProj;
uniform int has_sun_shadow;

in VS_OUT
{
    vec3 worldPos;
    float aux;
} fs_in;

layout(location = 0) out vec4 fragColor;

// Same AG layout as every other normal map in the game.
vec3 ripple_normal(vec2 uv)
{
    vec4 a = mix(texture(rippleA, uv), texture(rippleB, uv), frame_lerp);
    vec2 xy = a.ag * 2.0 - 1.0;
    return vec3(xy.x, 0.0, xy.y);
}

void main(void)
{
    // Two octaves, different scales, drifting against each other so the tiling
    // never sits still long enough to read.
    // Feature size tuned by eye on Monastery - 3x larger than first cut.
    vec3 n_hi = ripple_normal(fs_in.worldPos.xz * 0.047);
    vec3 n_lo = ripple_normal(fs_in.worldPos.xz * 0.0143 + vec2(0.37, 0.19));
    vec3 n = vec3(0.0, 1.0, 0.0);
    n.xz = (n_hi.xz + n_lo.xz) * 0.5 * 0.35;
    n.y = sqrt(max(1.0 - dot(n.xz, n.xz), 0.0));

    vec3 V = normalize(cameraPos - fs_in.worldPos);
    float ndv = max(dot(n, V), 0.0);

    // The authored curve: constant + (1-constant) * (1-N.V)^exponent.
    float F = clamp(fresnel.x + (1.0 - fresnel.x) * pow(1.0 - ndv, fresnel.y), 0.0, 1.0);

    vec3 R = reflect(-V, n);
    // Undo the display mirror for the cube lookup, same reason deferred flips
    // its reflection vector. If the sun sits on the wrong side of the water,
    // this sign is the first suspect.
    vec3 R_cube = vec3(-R.x, R.y, R.z);

    // The sky, untinted. This used to be multiplied by the +0xE0 colour on the
    // theory it was a reflection tint; Mines authors that field (1.0,0.7,0.3)
    // and the whole lake went orange. It is the SUN tint, and it belongs on
    // the glint below - Monastery only got away with it by authoring white.
    vec3 refl = texture(sky, R_cube).rgb;

    // Terrain and models, by marching the reflected ray through the frame -
    // same technique as the wet-terrain SSR, and water is its best case: the
    // hill across the lake is on screen directly above its own reflection.
    // Where the ray hits, the lit scene replaces the sky; where it leaves the
    // screen or misses, the sky stands. A cubemap can never put a building in
    // the water - only the frame knows where the buildings are.
    vec3 Pv = (view * vec4(fs_in.worldPos, 1.0)).xyz;

    {
        vec3 Rv = normalize(mat3(view) * R);

        // Rays toward the camera have nothing in front of them to hit.
        if (Rv.z < -0.02) {
            const int   STEPS     = 48;
            const float STRIDE    = 0.4;
            const float THICKNESS = 2.5;

            vec3 pos = Pv;
            for (int i = 0; i < STEPS; ++i) {
                pos += Rv * (STRIDE * (1.0 + float(i) * 0.09));

                vec4 clip = projection * vec4(pos, 1.0);
                if (clip.w <= 0.0) break;
                vec2 uv = (clip.xy / clip.w) * 0.5 + 0.5;
                if (any(lessThan(uv, vec2(0.0))) || any(greaterThan(uv, vec2(1.0)))) break;

                float sz = texture(scene_pos, uv).z;
                if (sz >= 0.0) continue;   // sky pixel, nothing to hit

                float behind = sz - pos.z;
                if (behind > 0.0 && behind < THICKNESS) {
                    // Fade at the frame border so reflections do not end in a
                    // hard vertical cut - the signature SSR artifact.
                    vec2 e = smoothstep(vec2(0.0), vec2(0.1), uv) *
                             (1.0 - smoothstep(vec2(0.9), vec2(1.0), uv));
                    refl = mix(refl, texture(lit_scene, uv).rgb, e.x * e.y);
                    break;
                }
            }
        }
    }

    // Sun glint: explicit, coloured by the authored tint, and gated by the
    // baked shadow - sun colour does not land on water the sun cannot see.
    // The sky reflection above deliberately stays: shade blocks the sun, not
    // the sky.
    float shade = 1.0;
    if (has_sun_shadow != 0) {
        vec4 sp = sunViewProj * vec4(fs_in.worldPos, 1.0);
        sp.xyz /= sp.w;
        sp.xy = sp.xy * 0.5 + 0.5;
        if (sp.z <= 1.0 && sp.z >= 0.0 &&
            all(greaterThanEqual(sp.xy, vec2(0.0))) && all(lessThanEqual(sp.xy, vec2(1.0)))) {
            shade = texture(sun_shadow_map, sp.xyz);
        }
    }
    // The authored exponent and scale are calibrated for the game's HDR
    // water, where the composition pass tone-maps the result back down. Fed
    // straight into this LDR forward pass, power 10..29 is a huge soft wash at
    // over half strength. Recalibrate rather than replace: the per-body ratios
    // stay authored, the constants put them in this pipeline's range.
    const float GLINT_SHARPEN = 8.0;   // higher = tighter sparkle
    const float GLINT_LEVEL   = 0.30;  // overall brightness
    float glint = pow(max(dot(R, sun_dir), 0.0), sun_glint.x * GLINT_SHARPEN)
                * sun_glint.y * GLINT_LEVEL * shade;

    vec3 col = mix(deep_color.rgb, refl, F) + sun_tint * glint;

    // More transparent looking straight down, near opaque at a grazing angle -
    // which is also where the reflection is, so the two arrive together.
    float alpha = clamp(0.72 + 0.28 * F, 0.0, 1.0);

    // Edge softening. The scene position under this fragment says how much
    // water sits between the surface and the bed along the view ray; fade in
    // over the first couple of metres so the shoreline is a wash, not a
    // hard polygon edge. Same idea as the game's g_softDepth. The glint fades
    // with it - a sun sparkle on ankle-deep water at the very shore reads as
    // an artifact of the fade otherwise.
    const float SOFT_DEPTH = 2.0;
    vec2 suv = gl_FragCoord.xy / resolution;
    float sz = texture(scene_pos, suv).z;
    if (sz < 0.0) {
        float water_depth = Pv.z - sz;   // both negative into the screen

        // Boat masking. A pixel whose underlying surface is a MODEL sitting
        // within a couple of metres of the waterline is a deck or hull
        // interior - the lakebed is terrain, and it is deep. The game does
        // this properly with excluded_water hull volumes baked into the boat
        // models; this is the viewer's stand-in from data already in the
        // G-buffer.
        //
        // The up-facing test is what keeps it stable as the camera moves. The
        // first cut masked on flag + depth alone, and from a low camera the
        // rays beside a hull first-hit the SUBMERGED HULL SIDE - also a model,
        // also inside the band - so a stripe of water around the boat vanished
        // and the waterline slid down the hull as the camera did: boats
        // appeared to sink. Decks and interiors face up; hull sides face out.
        // Only up-facing model surfaces mask, so the hull keeps its water and
        // its waterline whatever the camera does.
        uint flag = uint(texture(scene_gmf, suv).b * 255.0 + 0.5);
        if ((flag & 0xF8u) == 64u && water_depth < exclude_band) {
            vec3 n_view = texture(scene_nrm, suv).xyz * 2.0 - 1.0;
            float up = (mat3(invView) * n_view).y;
            if (up > 0.6) {
                discard;
            }
        }

        float soft = smoothstep(0.0, SOFT_DEPTH, water_depth);
        alpha *= soft;
        col -= sun_tint * glint * (1.0 - soft);
    }

    fragColor = vec4(col, alpha);
}
