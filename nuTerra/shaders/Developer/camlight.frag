#version 450 core

#extension GL_ARB_shading_language_include : require

#define USE_PERVIEW_UBO
#include "common.h" //! #include "../common.h"

in vec3 fNormal;
in vec3 fWorld;

uniform vec3 light_color;
uniform float alpha;
uniform vec3 eye;

out vec4 outColor;

void main(void)
{
    // Rim weighting, so a sphere reads as a VOLUME rather than a flat disc.
    //
    // Facing the camera the surface is nearly transparent and what is behind it
    // shows through; toward the silhouette it thickens. That is roughly what a
    // real volume of glowing air does, and more usefully it means several
    // overlapping ranges stay legible instead of stacking into one solid blob.
    vec3 V = normalize(eye - fWorld);
    float facing = abs(dot(normalize(fNormal), V));
    float rim = 1.0 - facing;

    // Both faces are drawn - culling is off so that flying INSIDE a light still
    // shows its far shell rather than nothing at all. That doubles the alpha
    // through the middle, which the low floor here keeps in hand.
    float a = alpha * (0.12 + 0.88 * rim * rim);

    outColor = vec4(light_color, a);
}
