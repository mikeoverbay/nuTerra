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
// How far the glare reaches, as a multiple of the core's radius. The QUAD
// grows by this; the core does not - see the frag, which rescales d.
uniform float glare_ext;
uniform float see_thru; // how deep an occluder a bulb still shines through, m

out vec2 vOffset;       // -1..1 across the disc
out float vDim;         // how much the pixel floor inflated this bulb
out float vDepth;       // the centre's own 0..1 depth, for the core's z test
out vec4 vCentre;       // clip-space CENTRE, for the occlusion test
out float vThresh;      // window depth the occlusion test compares against

void main(void)
{
    vec2 c = vec2(float(gl_VertexID & 1), float(gl_VertexID >> 1)) * 2.0 - 1.0;
    vOffset = c;

    // Built in VIEW space, which is what makes the disc face the camera: the
    // offset is applied after the rotation, so there is no billboard basis to
    // derive and nothing to get wrong when the camera rolls.
    vec4 vc = view * vec4(centre, 1.0);

    // Kept before the offset. The occlusion test asks about the BULB, which is
    // a point, not about each corner of the card standing in for it.
    vCentre = projection * vc;
    float dist = max(-vc.z, 1e-3);

    // The bulb sits INSIDE its fixture, so the depth buffer in front of it
    // holds the housing, which is nearer. A plain per-pixel depth test then
    // carves the middle out of the disc and leaves only the rim that overhangs
    // the silhouette - a donut where a glare should be. A centre-only test is
    // no better: it hides the bulb completely.
    //
    // So the test is against a point see_thru metres IN FRONT of the bulb: a
    // bulb shines through its own glass and its own hood, and still not
    // through a wall. Computed here, where the projection is to hand, so the
    // fragment stage never has to invert it - which also keeps this correct
    // whatever near and far are.
    vec4 nc = projection * vec4(0.0, 0.0, -max(dist - see_thru, 0.01), 1.0);
    // NO 0.5 remap. The engine runs ClipControl(..., ZeroToOne), so clip depth
    // is ALREADY 0..1 and *0.5+0.5 squashed every threshold into the top half
    // of the range - which is why this only ever let a bulb through when
    // something very near was in front of it.
    vThresh = nc.z / nc.w;

    // A bulb is small, and a small thing far away lands inside a single pixel,
    // where it flickers as the camera moves and the sample point crosses on
    // and off it. It is also sub-texel in the QUARTER resolution glow buffer,
    // where the blur's nine taps are 2.7 texels apart - miss the core and what
    // comes out is the kernel's own grid instead of a halo. Hold a floor in
    // pixels so neither happens.
    float px_per_m = projection[1][1] * 0.5 * resolution.y / dist;
    float r_true = radius;
    float r_floor = min_px / max(px_per_m, 1e-6);
    float r = max(r_true, r_floor);

    // A hard floor on its own makes every distant bulb exactly the same size,
    // so a lamp stops receding at the crossover and the whole town reads as a
    // heap of identical balls. Dim it by however much the floor inflated it
    // instead: the halo is built from what clears the glow threshold, so a
    // dimmer core leaves a smaller halo and the lamp goes on shrinking to the
    // eye while keeping the footprint the blur needs.
    //
    // By the RADIUS ratio rather than the area. Conserving energy properly is
    // the area ratio and it is too strong - a lamp two streets away would be
    // gone, when what a street lamp actually does at that range is stay
    // plainly visible.
    vDim = min(r_true / r, 1.0);

    // The quad carries the halo and the spikes as well as the core, so it is
    // the full glare radius. The core keeps its own size inside it.
    vc.xy += c * r * max(glare_ext, 1.0);
    gl_Position = projection * vc;

    // The bulb's OWN depth, taken at its centre rather than per corner: the
    // quad is a billboard standing in for a point, so every pixel of it is
    // at the same distance as far as occlusion is concerned. Already 0..1,
    // ClipControl being ZeroToOne.
    vDepth = vCentre.z / max(vCentre.w, 1e-6);
}
