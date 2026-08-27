#version 450 core

// Drawn with attach_CF like the terrain wire: colour into gColor, and
// gGMF = 0 (GFLAG_UNLIT) so the deferred pass leaves the lines alone.

layout (location = 0) out vec4 gColor;
layout (location = 1) out vec4 gGMF;

uniform vec3 wire_color;

void main()
{
    gColor = vec4(wire_color, 1.0);
    gGMF = vec4(0.0);
}
