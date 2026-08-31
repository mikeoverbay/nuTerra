#version 450 core

#extension GL_ARB_shading_language_include : require

#define USE_PERVIEW_UBO
#include "common.h" //! #include "../common.h"

layout(location = 0) in vec3 vertexPosition;

uniform mat4 mvp;

// Decal boxes are thin slabs: the face is local XY, local Z is the thickness,
// and for a decal lying on the ground that Z is world up. Measured on Abbey the
// slabs run 0.19 to 5.18 units thick against 7 to 26 across, so terrain that
// dips below the authored plane falls straight out of the box and the frag
// clips it away. Fatten that axis before the transform to give it some room.
const float DECAL_SLACK = 2.5;

out VS_OUT {
    flat mat4 invMVP;
} vs_out;

void main(void)
{
    // Applied before mvp, so it scales the cube in its own space. The frag
    // reconstructs decal space through invMVP and clips at +-0.5, so it has to
    // come from the same matrix or the box grows while the clip does not.
    mat4 slack = mat4(1.0);
    slack[2][2] = DECAL_SLACK;
    mat4 m = mvp * slack;

    gl_Position = m * vec4(vertexPosition, 1.0);
    vs_out.invMVP = inverse(m);
}
