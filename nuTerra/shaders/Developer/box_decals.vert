#version 450 core

#extension GL_ARB_shading_language_include : require

#define USE_PERVIEW_UBO
#define USE_DECALS_SSBO
#include "common.h" //! #include "../common.h"

layout(location = 0) in vec3 vertexPosition;

void main(void)
{
    const DecalGLInfo thisDecal = decals[gl_InstanceID];
    gl_Position = viewProj * thisDecal.matrix * vec4(vertexPosition, 1.0);
}
