#version 450 core

layout(lines) in;
layout(triangle_strip, max_vertices = 4) out;

// Expand each line segment into a screen-space quad.
//
// glLineWidth is why this exists. In a core profile the aliased line width range
// is very often exactly 1..1, so asking for 2 gets silently clamped to a
// hairline - and a one pixel line has no antialiasing, so how much of each pixel
// it covers depends on the angle it crosses the grid at. Rotate the camera and
// the same line changes brightness; zoom and it changes apparent thickness.
// Neither is the line changing, only its coverage.
//
// A quad sized in PIXELS is immune to both: the width is whatever we ask for
// regardless of orientation, and fEdge below lets the fragment stage feather the
// two long edges so there are no stair steps left to shimmer.

in vec4 gsCol[];
in vec3 gsWorld[];

out vec4 fCol;
out vec3 fWorld;

// -1 at one long edge, +1 at the other. The fragment shader fades on |fEdge|.
out float fEdge;

uniform vec2 viewport;   // pixels
uniform float line_px;   // full ribbon width, pixels

void main(void)
{
    vec4 a = gl_in[0].gl_Position;
    vec4 b = gl_in[1].gl_Position;

    // A segment crossing the near plane cannot be divided by w without
    // producing nonsense, and unlike GL_LINES nothing clips it for us before
    // this runs. Drop it. The route passes through the eye while flying, so
    // this fires constantly - and it costs nothing, because the fragment stage
    // already blanks everything within hide_near of the camera anyway.
    if (a.w <= 1e-4 || b.w <= 1e-4) {
        return;
    }

    vec2 na = a.xy / a.w;
    vec2 nb = b.xy / b.w;

    // Direction in PIXELS, so the perpendicular is a pixel-space perpendicular.
    // Taking it in NDC would make the width depend on the aspect ratio.
    vec2 d = (nb - na) * viewport;
    if (dot(d, d) < 1e-12) {
        return;                 // zero length after projection, no direction
    }
    d = normalize(d);
    vec2 perp = vec2(-d.y, d.x);

    // NDC spans 2 units across `viewport` pixels, so one pixel is 2/viewport.
    // Half of line_px pixels is therefore line_px/viewport.
    vec2 off = perp * (line_px / viewport);

    fCol = gsCol[0]; fWorld = gsWorld[0]; fEdge = -1.0;
    gl_Position = vec4(a.xy + off * a.w, a.z, a.w);
    EmitVertex();

    fCol = gsCol[0]; fWorld = gsWorld[0]; fEdge = 1.0;
    gl_Position = vec4(a.xy - off * a.w, a.z, a.w);
    EmitVertex();

    fCol = gsCol[1]; fWorld = gsWorld[1]; fEdge = -1.0;
    gl_Position = vec4(b.xy + off * b.w, b.z, b.w);
    EmitVertex();

    fCol = gsCol[1]; fWorld = gsWorld[1]; fEdge = 1.0;
    gl_Position = vec4(b.xy - off * b.w, b.z, b.w);
    EmitVertex();

    EndPrimitive();
}
