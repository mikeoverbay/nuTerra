#version 450 core

#extension GL_ARB_shading_language_include : require

#define USE_PERVIEW_UBO
#include "common.h" //! #include "../common.h"

// Trail particles: one camera-facing quad each, all of them in one instanced
// draw.
//
// ONE DRAW FOR THE WHOLE MAP. Thirty tanks with several rounds in the air is
// thousands of particles, and a draw call each would cost more than the
// particles do. The per-instance data is four floats of position-and-size and
// four of colour, written into one buffer each frame; the quad itself still
// comes from gl_VertexID, so there is no geometry to store.

layout(location = 0) in vec4 a_pos_size;   // xyz world, w radius in metres
layout(location = 1) in vec4 a_colour;     // rgb, a
// The frame's rectangle inside the master atlas, (u0, v0, u1, v1). A
// degenerate rectangle - u1 <= u0 - means this particle has no artwork and the
// fragment stage draws a soft round puff instead, which is what the vapour
// trail uses.
layout(location = 2) in vec4 a_uv;

out vec2 vOffset;
out vec4 vColour;
out vec4 vUV;

void main(void)
{
    vec2 c = vec2(float(gl_VertexID & 1), float(gl_VertexID >> 1)) * 2.0 - 1.0;
    vOffset = c;
    vColour = a_colour;
    vUV = a_uv;

    // Built in VIEW space so it faces the camera without a billboard basis to
    // derive - the same trick the lamp bulbs and the muzzle flashes use.
    vec4 vc = view * vec4(a_pos_size.xyz, 1.0);
    vc.xy += c * a_pos_size.w;
    gl_Position = projection * vc;
}
