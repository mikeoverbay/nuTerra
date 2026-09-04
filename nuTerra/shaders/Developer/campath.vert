#version 450 core

#extension GL_ARB_shading_language_include : require

#define USE_PERVIEW_UBO
#include "common.h" //! #include "../common.h"

// A baked camera path, drawn as world-space line segments. One buffer, one
// draw: the whole route plus its heading and tilt ticks are pre-built on the
// CPU in MapCamPath, because the path only changes when a map loads.

layout(location = 0) in vec3 vPos;
layout(location = 1) in vec4 vCol;

// To the GEOMETRY stage, not the fragment one - campath.geom widens each
// segment into a screen-space quad and forwards these along.
out vec4 gsCol;
out vec3 gsWorld;

void main(void)
{
    gsCol = vCol;
    gsWorld = vPos;
    gl_Position = viewProj * vec4(vPos, 1.0);
}
