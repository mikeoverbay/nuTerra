#version 450 core

#extension GL_ARB_shading_language_include : require

#define USE_PERVIEW_UBO
#include "common.h" //! #include "../common.h"

// The light volume for one lamp's fog scattering.
//
// The same unit sphere the overlay uses, scaled to the lamp's range. Drawing
// geometry rather than a full screen quad is the whole point: a lamp only
// scatters inside its own radius, so the rasteriser does the culling and a
// pixel nowhere near a lamp costs nothing at all.

layout(location = 0) in vec3 vPos;   // unit sphere, radius 1, centred on origin

uniform vec3 centre;
uniform float radius;

out vec3 fWorld;

void main(void)
{
    vec3 w = centre + vPos * radius;
    fWorld = w;
    gl_Position = viewProj * vec4(w, 1.0);
}
