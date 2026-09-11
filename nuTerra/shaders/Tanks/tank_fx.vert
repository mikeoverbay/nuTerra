#version 450 core

#extension GL_ARB_shading_language_include : require

#define USE_PERVIEW_UBO
#include "common.h" //! #include "../common.h"

// One puff: the flash at a muzzle or the burst where a round landed.
//
// No vertex data. The quad comes from gl_VertexID against the empty VAO, the
// same way lamp_bulb.vert builds its disc.

uniform vec3 centre;    // world position
uniform float radius;   // metres, this frame

out vec2 vOffset;       // -1..1 across the disc
out float vDepth;       // the centre's own 0..1 depth, for the occlusion test
out vec4 vCentre;       // clip-space CENTRE, so the test asks about the point

void main(void)
{
    vec2 c = vec2(float(gl_VertexID & 1), float(gl_VertexID >> 1)) * 2.0 - 1.0;
    vOffset = c;

    // BUILT IN VIEW SPACE, which is what makes it face the camera: the offset
    // is applied after the rotation, so there is no billboard basis to derive
    // and nothing to get wrong when the camera rolls.
    vec4 vc = view * vec4(centre, 1.0);

    // Kept before the offset. A flash is a point that happens to be drawn as a
    // disc, so the occlusion test asks about the point rather than about each
    // corner of the card standing in for it - per-corner, a burst against a
    // hillside has its lower corners buried and loses its bottom edge to a
    // straight line.
    vCentre = projection * vc;

    vc.xy += c * radius;
    gl_Position = projection * vc;

    // Already 0..1: the engine runs ClipControl(..., ZeroToOne), so clip depth
    // is not the -1..1 range that would need a *0.5+0.5 remap. lamp_bulb.vert
    // shipped that remap and it squashed every threshold into the top half of
    // the range.
    vDepth = vCentre.z / max(vCentre.w, 1e-6);
}
