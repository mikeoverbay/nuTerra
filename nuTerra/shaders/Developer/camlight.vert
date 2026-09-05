#version 450 core

#extension GL_ARB_shading_language_include : require

#define USE_PERVIEW_UBO
#include "common.h" //! #include "../common.h"

// A light placed in Path Studio, drawn as a translucent sphere the size of its
// range. One unit sphere is uploaded once and every light reuses it - the
// centre and the radius arrive as uniforms, so the buffer never changes and
// there is nothing to rebuild when a path is reloaded.

layout(location = 0) in vec3 vPos;   // unit sphere, radius 1, centred on origin

uniform vec3 centre;
uniform float radius;

out vec3 fNormal;
out vec3 fWorld;

void main(void)
{
    // The unit sphere's position IS its normal, before scaling. Taking it here
    // rather than from a second attribute keeps the vertex to three floats.
    fNormal = normalize(vPos);

    vec3 w = centre + vPos * radius;
    fWorld = w;
    gl_Position = viewProj * vec4(w, 1.0);
}
