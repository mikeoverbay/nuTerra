#version 450 core

#extension GL_ARB_shading_language_include : require

#define USE_PERVIEW_UBO
#include "common.h" //! #include "../common.h"

// Card particles. One instanced triangle strip per particle, turned to face the
// camera in view space so no CPU-side billboarding is needed.

layout (location = 0) in vec4 inPosSize;   // xyz world centre, w half-extent
layout (location = 1) in vec4 inColour;    // straight rgba
layout (location = 2) in vec4 inUV;        // xy atlas cell origin, zw cell size

out VS_OUT
{
    vec2 uv;
    vec4 colour;
    float viewDist;
} vs_out;

void main(void)
{
    // Unit quad corners from the vertex id, no vertex buffer needed.
    const vec2 corner = vec2((gl_VertexID == 1 || gl_VertexID == 3) ? 1.0 : -1.0,
                             (gl_VertexID >= 2) ? 1.0 : -1.0);

    const vec4 centreView = view * vec4(inPosSize.xyz, 1.0);
    // Offsetting in view space is what makes the card face the camera.
    const vec4 posView = vec4(centreView.xy + corner * inPosSize.w, centreView.zw);

    gl_Position = projection * posView;

    vs_out.uv       = inUV.xy + (corner * 0.5 + 0.5) * inUV.zw;
    vs_out.colour   = inColour;
    vs_out.viewDist = -centreView.z;
}
