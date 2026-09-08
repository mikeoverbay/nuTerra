#version 450 core

#extension GL_ARB_shading_language_include : require

#define USE_COMMON_PROPERTIES_UBO
#include "common.h" //! #include "../common.h"

// The pass draws premultiplied, blend (ONE, ONE_MINUS_SRC_ALPHA), matching the
// volumetric FX pass so both composite the same way.

layout (binding = 0) uniform sampler2D atlas;
uniform int wireMode;   // debug: draw untextured so motion can be judged alone
layout (binding = 3) uniform sampler2D gPosition;   // view space, for soft edges

// Soft-particle range as a FRACTION of the card's half-extent.
//
// A fixed 0.5 m was right for a card a metre across and a knife edge on the
// 7 to 36 m cards the monastery smoke actually spawns. A card is a plane at
// one view depth; where that plane crosses a roof, the depth test cuts the
// card along the intersection line - a straight line, because both are
// planes - and half a metre of fade on a roof seen at that angle is two or
// three pixels wide. Scaling the range with the card keeps the fade the same
// proportion of the card whatever it grows to. Floored at the old 0.5 m so a
// newborn card is never harder than it was.
uniform float soft_frac;

// Lighting.
//
// The cards used to be pasted onto the frame unlit, unexposed and untonemapped:
// texture times the authored tint, straight into a buffer the deferred pass
// had already put through correct(). Every card was one flat colour, and
// where two emitters author different colours - smoke_Big drifts to
// (0.55, 0.68, 0.88) over its life, the slow and fast emitters stay grey -
// the sprite silhouette of the front card punched a hard-edged hole in the
// one behind. That is what the "bands" on the monastery plume were.
//
// The game lights these sprites; the authored blue is a tint ON the lighting,
// not a display colour. So: the same probe the ground and the volumetric
// meshes read, evaluated for a card facing the sky, the same AMBIENT and
// ambient_sat scaling deferred.frag applies to it, a flat share of the sun,
// and then the same tone curve the scene went through. Cards of different
// tints then land close enough in tone that their overlaps stop reading as
// edges, and the smoke follows the exposure slider like everything else.
uniform vec3  sh_ambient[9];
uniform int   sh_enabled;
uniform int   card_lit;       // 0 = the old path, byte for byte, for the A/B
uniform float card_ambient;   // gain on the probe term
uniform float card_sun;       // flat share of the sun

layout (location = 0) out vec4 outColor;

in VS_OUT
{
    vec2 uv;
    vec4 colour;
    float viewDist;
    float halfSize;
} fs_in;

// Ramamoorthi & Hanrahan irradiance - the same expression as deferred.frag's
// eval_sh_irradiance and volumetric.vert's fx_sh_irradiance, so all three
// passes agree about the sky.
vec3 card_sh_irradiance(vec3 n)
{
    const float c1 = 0.429043, c2 = 0.511664, c3 = 0.743125;
    const float c4 = 0.886227, c5 = 0.247708;

    return c4 * sh_ambient[0]
         + 2.0 * c2 * (sh_ambient[1] * n.y + sh_ambient[2] * n.z + sh_ambient[3] * n.x)
         + 2.0 * c1 * (sh_ambient[4] * n.x * n.y
                     + sh_ambient[5] * n.y * n.z
                     + sh_ambient[7] * n.x * n.z)
         + c3 * sh_ambient[6] * n.z * n.z
         - c5 * sh_ambient[6]
         + c1 * sh_ambient[8] * (n.x * n.x - n.y * n.y);
}

// deferred.frag's correct(), rgb only, expression for expression - including
// the "1.0 / props.GAMMA_LEVEL*0.5" that parses as (1/GAMMA_LEVEL)*0.5. A copy
// rather than an include so the deferred shader is not touched; if correct()
// changes, this changes with it or the smoke drifts off the scene.
vec3 card_tone(vec3 hdr)
{
    vec3 mapped = vec3(1.0) - exp(-hdr * props.tonemap_exposure);
    mapped = pow(mapped, vec3(1.0 / 1.2));
    mapped = pow(mapped, vec3(1.0 / props.GAMMA_LEVEL*0.5));
    return mapped;
}

void main(void)
{
    if (wireMode != 0) {
        // Opaque, no texture, no soft fade - just the card outline. The colour
        // carries the particle's age so flow is readable: green new, red old.
        outColor = vec4(fs_in.colour.rgb, 1.0);
        return;
    }

    const vec4 tex = texture(atlas, fs_in.uv);

    // Soft particles, the same rule the volumetric pass uses: fade a card out
    // as it approaches whatever is behind it, so it does not cut a hard line
    // into the ground. Nothing drawn leaves gPosition at zero, which is sky and
    // must read as infinitely far. The range scales with the card - see
    // soft_frac above.
    const vec3 scenePos  = texelFetch(gPosition, ivec2(gl_FragCoord.xy), 0).xyz;
    const float sceneDist = (abs(scenePos.z) < 1e-6) ? 1e30 : -scenePos.z;
    const float softRange = max(0.5, soft_frac * fs_in.halfSize);
    const float softFade  = clamp((sceneDist - fs_in.viewDist) / softRange, 0.0, 1.0);

    float alpha = tex.a * fs_in.colour.a * softFade;
    if (alpha <= 0.002) discard;

    vec3 rgb = tex.rgb * fs_in.colour.rgb;

    if (card_lit != 0) {
        // A card faces the camera, but what lights smoke is the sky above it,
        // so the probe is read for an UP-facing normal. One normal for every
        // card is deliberate: it is what makes cards of different tints
        // converge, and a per-card normal would put the silhouettes back.
        vec3 ambient;
        if (sh_enabled != 0) {
            vec3 irr = max(card_sh_irradiance(vec3(0.0, 1.0, 0.0)), vec3(0.0));
            // deferred.frag's desaturation toward the probe's own luminance,
            // then its AMBIENT level - the ground's chain, not a new one.
            float lum = dot(irr, vec3(0.299, 0.587, 0.114));
            irr = mix(vec3(lum), irr, props.ambient_sat);
            ambient = irr * props.AMBIENT;
        } else {
            // The flat stand-in the ground falls back to without a probe.
            ambient = props.ambientColorForward * (props.AMBIENT * 3.0);
        }
        // Sun as deferred.frag builds it - Sun Tint between white and the
        // map's colour, Sun Strength the level - but flat: smoke has no
        // normal to take a cosine against and is not shadowed.
        const vec3 sun = mix(vec3(1.0), props.sunColor, props.sun_tint) * props.sun_strength;
        const vec3 light = ambient * card_ambient + sun * card_sun;
        rgb = card_tone(rgb * light);
    }

    outColor = vec4(rgb * alpha, alpha);
}
