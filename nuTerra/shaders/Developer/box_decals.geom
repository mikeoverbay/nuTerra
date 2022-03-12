#version 450 core

#extension GL_ARB_shading_language_include : require

#define USE_PERVIEW_UBO
#define USE_DECALS_SSBO
#include "common.h" //! #include "../common.h"

layout (points) in;
layout (line_strip, max_vertices = 24) out;

void main(void)
{
    const DecalGLInfo thisDecal = decals[gl_PrimitiveIDIn];

    const mat4 MVP = viewProj * thisDecal.matrix;
    const vec3 bmin = vec3(-1.0);
    const vec3 bmax = vec3(1.0);

    gl_Position = MVP * vec4(bmin, 1.0);
    EmitVertex();

    gl_Position = MVP * vec4(bmin.xy, bmax.z, 1.0);
    EmitVertex();

    EndPrimitive(); // 1

    gl_Position = MVP * vec4(bmin.xy, bmax.z, 1.0);
    EmitVertex();

    gl_Position = MVP * vec4(bmin.x, bmax.yz, 1.0);
    EmitVertex();

    EndPrimitive(); // 2

    gl_Position = MVP * vec4(bmin.x, bmax.yz, 1.0);
    EmitVertex();

    gl_Position = MVP * vec4(bmin.x, bmax.y, bmin.z, 1.0);
    EmitVertex();

    EndPrimitive(); // 3

    gl_Position = MVP * vec4(bmin.x, bmax.y, bmin.z, 1.0);
    EmitVertex();

    gl_Position = MVP * vec4(bmin, 1.0);
    EmitVertex();

    EndPrimitive(); // 4

    gl_Position = MVP * vec4(bmax.x, bmin.yz, 1.0);
    EmitVertex();

    gl_Position = MVP * vec4(bmax.x, bmin.y, bmax.z, 1.0);
    EmitVertex();

    EndPrimitive(); // 5

    gl_Position = MVP * vec4(bmax.x, bmin.y, bmax.z, 1.0);
    EmitVertex();

    gl_Position = MVP * vec4(bmax, 1.0);
    EmitVertex();

    EndPrimitive(); // 6

    gl_Position = MVP * vec4(bmax, 1.0);
    EmitVertex();

    gl_Position = MVP * vec4(bmax.xy, bmin.z, 1.0);
    EmitVertex();

    EndPrimitive(); // 7

    gl_Position = MVP * vec4(bmax.xy, bmin.z, 1.0);
    EmitVertex();

    gl_Position = MVP * vec4(bmax.x, bmin.yz, 1.0);
    EmitVertex();

    EndPrimitive(); // 8

    gl_Position = MVP * vec4(bmin, 1.0);
    EmitVertex();

    gl_Position = MVP * vec4(bmax.x, bmin.yz, 1.0);
    EmitVertex();

    EndPrimitive(); // 9

    gl_Position = MVP * vec4(bmin.xy, bmax.z, 1.0);
    EmitVertex();

    gl_Position = MVP * vec4(bmax.x, bmin.y, bmax.z, 1.0);
    EmitVertex();

    EndPrimitive(); // 10

    gl_Position = MVP * vec4(bmin.x, bmax.yz, 1.0);
    EmitVertex();

    gl_Position = MVP * vec4(bmax, 1.0);
    EmitVertex();

    EndPrimitive(); // 11

    gl_Position = MVP * vec4(bmin.x, bmax.y, bmin.z, 1.0);
    EmitVertex();

    gl_Position = MVP * vec4(bmax.xy, bmin.z, 1.0);
    EmitVertex();

    EndPrimitive(); // 12
}
