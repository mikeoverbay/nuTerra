#version 450 core

// Full-screen quad, the same recipe as FXAA.vert: four corners from `rect`
// through the ortho ProjectionMatrix the post passes run under.
//
// It used to be a box the size of the map, drawn inside-out with the front
// faces discarded. That only fogged pixels the box covered on screen, so the
// outland beyond the map's bounds - and the sky above the box - could go
// unfogged depending on where the camera stood. Fog is a property of every
// pixel; a screen quad says so.

layout (location = 0) uniform mat4 ProjectionMatrix;
layout (location = 1) uniform vec4 rect;

void main(void)
{
    vec2 co;
    if (gl_VertexID == 0)      co = rect.xw;
    else if (gl_VertexID == 1) co = rect.xy;
    else if (gl_VertexID == 2) co = rect.zw;
    else                       co = rect.zy;
    gl_Position = ProjectionMatrix * vec4(co, 0.0, 1.0);
}
