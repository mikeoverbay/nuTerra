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
uniform float card_px;  // card HEIGHT on screen, in pixels. Exactly.
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

    // THE POINT IS PROJECTED, THE QUAD IS NOT.
    //
    // This used to build the card in VIEW space and let the projection carry
    // it, with the size worked back through the perspective divide so it came
    // out near a target pixel height. That is a perspective quad wearing a
    // screen-space size: it is still a rectangle standing in the world, so it
    // keeps foreshortening, it keeps needing metre clamps at both ends to stop
    // it filling the screen up close or vanishing at range, and those clamps
    // are exactly where the size stops being constant.
    //
    // So only the ANCHOR goes through the camera. Its clip position divides
    // down to one point on screen, and the card is then four corners around
    // that point measured in PIXELS - no divide, no foreshortening, no clamp.
    // A marker is part of the interface, not part of the scene.
    vec3 world = anchor + vec3(0.0, lift, 0.0);
    vec4 vc = view * vec4(world, 1.0);
    vec4 clip = projection * vc;

    // BEHIND THE CAMERA IS NOT OFF SCREEN. Divide by a negative w and the
    // point lands mirrored through the origin, so a tank behind you puts its
    // card on the opposite side of the screen, right way up and perfectly
    // legible. Push the whole quad outside the clip volume instead.
    if (clip.w <= 1e-4) {
        gl_Position = vec4(2.0, 2.0, 2.0, 1.0);
        vFade = 0.0;
        return;
    }

    vec2 ndc = clip.xy / clip.w;

    // SNAPPED TO THE PIXEL GRID. The cell is minified on the way down - 256 by
    // 80 into about 150 by 46 - and with the card free to sit on any fraction
    // of a pixel the sample points crawl across the glyphs as the tank moves,
    // which reads as the digits boiling. Rounding the CENTRE (not the corners)
    // keeps the card's own size exact while holding its texels still.
    vec2 centre_px = (ndc * 0.5 + 0.5) * resolution;
    centre_px = floor(centre_px + 0.5);
    ndc = (centre_px / resolution) * 2.0 - 1.0;

    // Half extents in NDC: NDC spans 2 units across `resolution` pixels, so a
    // half-size of P pixels is P/resolution.
    vec2 half_px = vec2(card_px / max(aspect, 1e-3), card_px) * 0.5;
    vec2 off = (c * 2.0 - 1.0) * half_px / resolution;

    // Fade with range rather than cut. Thirty markers at 800 m is confetti,
    // and a hard cutoff makes them blink in and out as the camera drifts.
    // Range is still a world distance - the card no longer shrinks with it, so
    // this is now the only thing that says "far away".
    float dist = max(-vc.z, 1e-3);
    vFade = 1.0 - clamp((dist - fade.x) / max(fade.y - fade.x, 1e-3), 0.0, 1.0);

    // z = 0: the pass runs with the depth test off, over the finished frame.
    gl_Position = vec4(ndc + off, 0.0, 1.0);
}
