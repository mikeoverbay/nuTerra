#version 450 core

#extension GL_ARB_shading_language_include : require

#define USE_PERVIEW_UBO
#include "common.h" //! #include "../common.h"

layout(location = 0) in vec3 vertexPosition;

uniform mat4 mvp;

out VS_OUT {
    flat mat4 invMVP;
    flat vec3 s_vector;
} vs_out;

void main(void)
{
    gl_Position = mvp * vec4(vertexPosition, 1.0);
    vs_out.invMVP = inverse(mvp);
    vs_out.s_vector = vec3( mvp * vec4( 0.0 ,1.0 ,0.0 ,0.0) );
}
