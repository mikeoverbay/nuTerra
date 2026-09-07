#version 450 core

#extension GL_ARB_shading_language_include : require

#define USE_PERVIEW_UBO
#include "common.h" //! #include "../common.h"

// The visible SOURCE of a lamp: a small, very bright, camera-facing disc at the
// bulb itself, drawn into the FX buffer so the bright pass and blur that
// already serve fire turn it into the glare a real lamp has.
//
// No vertex data. The quad is built from gl_VertexID against the empty VAO, so
// there is no buffer to allocate, upload or keep in step with the light set.

uniform vec3 centre;    // world position of the bulb
uniform float radius;   // its size in metres
uniform float min_px;   // ... but never smaller than this on screen

out vec2 vOffset;       // -1..1 across the disc

void main(void)
{
    vec2 c = vec2(float(gl_VertexID & 1), float(gl_VertexID >> 1)) * 2.0 - 1.0;
    vOffset = c;

    // Built in VIEW space, which is what makes the disc face the camera: the
    // offset is applied after the rotation, so there is no billboard basis to
    // derive and nothing to get wrong when the camera rolls.
    vec4 vc = view * vec4(centre, 1.0);

    // A bulb is small, and a small thing far away lands inside a single pixel,
    // where it flickers as the camera moves and the sample point crosses on
    // and off it. Hold a floor in PIXELS so a distant lamp keeps a steady core
    // - which is what the eye sees at night anyway, since the glare of a
    // street lamp does not shrink to nothing with distance.
    float dist = max(-vc.z, 1e-3);
    float px_per_m = projection[1][1] * 0.5 * resolution.y / dist;
    float r = max(radius, min_px / max(px_per_m, 1e-6));

    vc.xy += c * r;
    gl_Position = projection * vc;
}
