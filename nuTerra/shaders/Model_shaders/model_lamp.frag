#version 450 core

#extension GL_ARB_bindless_texture : require
#extension GL_ARB_shading_language_include : require

#define USE_MATERIALS_SSBO
#include "common.h" //! #include "../common.h"

// A street lamp, drawn in one pass with its GLASS EMISSIVE.
//
// WHY ITS OWN SHADER. The pane needs a per-fragment branch that no other
// material wants, and model.frag serves thirteen shader families through a
// subroutine table - putting this there would add a test in front of every PBS
// surface on every map to serve two models. So the lamp materials are routed to
// their own bucket in cull.comp and drawn here. Nothing else changes.
//
// WHY IT IS NOT IN THE DEPTH PREPASS. Same reason the glass bucket is not: the
// prepass draws `indirect` and `indirect_dbl_sided` only, and the colour pass
// then runs DepthFunc EQUAL against it. A pane whose texels the prepass had
// discarded could never be shaded - and discarding them is exactly what the
// cutout does. So this bucket does its own depth test at DepthFunc GREATER with
// DepthMask TRUE, laying its own depth, and the cutout stays where it belongs:
// in the shadow bakes, where it lets the bulb's light out.
//
// WHICH TEXELS ARE GLASS. The red channel of the normal map, which is the mask
// tools/make_lamp_patch.py paints and TexturePatch applies at load. The same
// number the depth passes use as a cutout is read here as "this is glass" -
// one mask, two readings.
//
// PRE TONEMAP, deliberately. This writes the G-buffer, so the pane gets fog and
// exposure like any other surface and softens with distance. The bulb sprite is
// an FX card drawn after the resolve and gets neither, which is right for a lens
// flare and wrong for a lit pane in fog - see docs/game_water.md for the same
// trap in another pass.

layout (location = 0) out vec3 gColor;
layout (location = 1) out vec3 gNormal;
layout (location = 2) out vec4 gGMF;
layout (location = 3) out vec3 gPosition;
#ifdef PICK_MODELS
layout (location = 4) out uint gPick;
#endif
layout (location = 5) out vec4 gAux;

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

uniform float pane_gain;    // how hard the glass glows
uniform vec3  pane_color;   // the LAMP's own colour, already linear

const MaterialProperties thisMaterial = material[fs_in.material_id];

vec3 normalBump;

void get_normal()
{
    vec4 nrm = texture(sampler2D(thisMaterial.maps[1]), fs_in.TC1);
    if (thisMaterial.g_useNormalPackDXT1) {
        normalBump = nrm.rgb * 2.0 - 1.0;
    } else {
        // DXT5nm: X in alpha, Y in green, red free for the cutout - which is
        // why the mask can live there without costing the normal anything.
        normalBump.xy = nrm.ag * 2.0 - 1.0;
        float dp = min(dot(normalBump.xy, normalBump.xy), 1.0);
        normalBump.z = clamp(sqrt(-dp + 1.0), -1.0, 1.0);
    }
    normalBump = normalize(normalBump);
    gNormal.xyz = normalize(fs_in.TBN * normalBump);
}

void main(void)
{
    vec4 co = texture(sampler2D(thisMaterial.maps[0]), fs_in.TC1);
    float mask = texture(sampler2D(thisMaterial.maps[1]), fs_in.TC1).r;

    get_normal();
    gPosition = fs_in.worldPosition;
    gAux = vec4(0.0);

    if (mask < thisMaterial.alphaReference) {
        // THE GLASS. Unlit and emissive: gGMF.b of GFLAG_GLOW tells
        // deferred.frag to skip lighting this pixel while still giving it fog
        // and the tonemapper, which is the whole point of being here rather
        // than in an FX card.
        //
        // The LAMP's colour, carrying the art's variation as brightness.
        //
        // Hue from the light and detail from the texture, rather than one or
        // the other: the pane's own albedo is a fixed cream, so using it neat
        // meant a blue lamp still glowed cream. Taking luminance keeps the
        // grime and the falloff painted into the pane while the colour comes
        // from whatever the bulb is actually set to.
        float lum = dot(co.rgb, vec3(0.299, 0.587, 0.114));
        gColor = pane_color * (lum * pane_gain);
        gGMF.rg = vec2(0.0);          // no gloss, no metal - it is a light now
        gGMF.b = GFLAG_GLOW;
        gGMF.a = 0.0;
        // Face the normal at the viewer. A pane lit from inside has no surface
        // relief worth keeping, and the ironwork's normals would otherwise
        // shade a surface that is not being shaded at all.
        gNormal.xyz = normalize(fs_in.TBN[2]);
    } else {
        // THE METALWORK. Ordinary PBS, so the post and the ironwork look
        // exactly as they did before this pass existed.
        gColor = co.rgb * thisMaterial.g_colorTint.rgb;
        gGMF.gr = texture(sampler2D(thisMaterial.maps[2]), fs_in.TC1).rg;
        gGMF.b = GFLAG_MODEL;
        gGMF.a = 0.0;
    }

    gNormal.xyz = gNormal.xyz * 0.5 + 0.5;

#ifdef PICK_MODELS
    gPick.r = fs_in.model_id + 1;
#endif
}
