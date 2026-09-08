#version 450 core

#extension GL_ARB_bindless_texture : require
#extension GL_ARB_shading_language_include : require

#define USE_MATERIALS_SSBO
#include "common.h" //! #include "../common.h"

// The lamp panes again, this time into the FX buffer so they reach the BLOOM.
//
// WHY A SECOND DRAW. model_lamp.frag writes the G-BUFFER, which the deferred
// resolve turns into the frame. The bloom is built from a different buffer
// entirely - fx_bright.frag reads gFX_HDR - so a pane that only writes the
// G-buffer is lit but never glows. Rather than plumb the G-buffer into the
// bright pass, which would drag every emissive surface on the map into it, the
// panes are simply drawn once more into the buffer the glow is made from.
//
// Cheap: the same geometry, no normals, no material lookups beyond two texture
// reads, and only the glass survives the discard.
//
// Runs BEFORE build_fx_glow and AFTER the resolve, in the same slot as the bulb
// sprite - the halo is the whole point of drawing them there.
//
// Additive contract, same as the bulb sprite: premultiplied colour with ZERO
// alpha. Under the FX buffer's One / OneMinusSrcAlpha that is dst + src, so the
// pane ADDS light and covers nothing. Alpha in that buffer is accumulated
// coverage, and a light source has none.

in VS_OUT
{
    vec2 TC1;
    vec3 worldPosition;
    mat3 TBN;
    flat uint material_id;
#ifdef PICK_MODELS
    flat uint model_id;
#endif
} fs_in;

uniform float pane_gain;
uniform vec3  pane_color;   // already linear
uniform float glow_gain;    // how much of the pane reaches the bloom

layout(location = 0) out vec4 fragColor;

void main(void)
{
    const MaterialProperties thisMaterial = material[fs_in.material_id];

    // The same mask, read the same way, as the cutout and the emissive pass.
    float mask = texture(sampler2D(thisMaterial.maps[1]), fs_in.TC1).r;
    if (mask >= thisMaterial.alphaReference) discard;   // metalwork: not a light

    vec4 co = texture(sampler2D(thisMaterial.maps[0]), fs_in.TC1);
    float lum = dot(co.rgb, vec3(0.299, 0.587, 0.114));

    fragColor = vec4(pane_color * (lum * pane_gain * glow_gain), 0.0);
}
