#version 450 core

#extension GL_ARB_shading_language_include : require

#define USE_PERVIEW_UBO
#include "common.h" //! #include "../common.h"

// The card that floats over a tank: its ID and two condition bars, already
// drawn into one cell of the card atlas by TankCards.
//
// No vertex data. The quad is built from gl_VertexID against the empty VAO,
// the same way lamp_bulb.vert builds its disc, so there is no buffer to keep
// in step with the instance list as tanks are added or removed.

uniform vec3 anchor;    // world position on the ground under the tank
uniform float lift;     // metres above it the card floats
uniform float aspect;   // card height / width, from the atlas cell
uniform float target_px;// how tall the card wants to be on screen
uniform vec2 size_clamp;// metres, min and max, so it cannot run away
uniform vec2 uv_off;    // this tank's cell in the atlas
uniform vec2 uv_size;
uniform vec2 fade;      // metres: fully opaque by .x, gone by .y

out vec2 vUV;           // 0..1 across the CARD, for the corner rounding
out vec2 vAtlas;        // where to sample
out float vFade;

void main(void)
{
    // (0,0) (1,0) (0,1) (1,1) - a triangle strip's four corners, y up.
    vec2 c = vec2(float(gl_VertexID & 1), float(gl_VertexID >> 1));
    vUV = c;

    // NO FLIP. The card is rasterised through an ortho whose top edge is the
    // card's y=0, so the cell's first texel row in the atlas is the BOTTOM of
    // the card - which is where the quad's own y=0 corner is. Flipping v here
    // was the obvious thing to write and it stands every marker on its head.
    vAtlas = uv_off + c * uv_size;

    // LIFTED IN WORLD SPACE, then billboarded in view space. The lift has to
    // be world +Y or the card leans with the camera and stops reading as
    // "above the tank"; the facing has to be view space or there is a
    // billboard basis to derive and something to get wrong when the camera
    // rolls. Doing each in the space it belongs in costs one extra transform.
    vec4 vc = view * vec4(anchor + vec3(0.0, lift, 0.0), 1.0);
    float dist = max(-vc.z, 1e-3);

    // A MARKER IS READ, NOT LOOKED AT. Sized in pixels rather than metres so
    // the ID stays legible from the far side of the map, which is the whole
    // point of it - a card scaled in metres is a smear at 400 m and a
    // billboard in the face at 5 m. Clamped at both ends in metres so it
    // still recedes a little near the camera instead of pinning to the
    // screen, and so a distant one cannot grow past its own tank.
    float px_per_m = projection[1][1] * 0.5 * resolution.y / dist;
    float h = clamp(target_px / max(px_per_m, 1e-6), size_clamp.x, size_clamp.y);
    float w = h / max(aspect, 1e-3);

    // Fade with range rather than cut. Thirty markers at 800 m is confetti,
    // and a hard cutoff makes them blink in and out as the camera drifts.
    vFade = 1.0 - clamp((dist - fade.x) / max(fade.y - fade.x, 1e-3), 0.0, 1.0);

    vc.xy += (c * 2.0 - 1.0) * vec2(w, h) * 0.5;
    gl_Position = projection * vc;
}
