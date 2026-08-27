#version 450 core

// Drawn with attach_CF like the terrain wire: colour into gColor, and
// gGMF = 0 (GFLAG_UNLIT) so the deferred pass leaves the lines alone.

layout (location = 0) out vec4 gColor;
layout (location = 1) out vec4 gGMF;

void main()
{
    gColor = vec4(0.0, 1.0, 1.0, 1.0);
    gGMF = vec4(0.0);
}
