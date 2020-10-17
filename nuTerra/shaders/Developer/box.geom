#version 450 core

#extension GL_ARB_shading_language_include : require
#include "common.h"

layout (points) in;
layout (line_strip, max_vertices = 24) out;

layout (binding = PER_FRAME_DATA_BASE, std140) uniform PerView {
    mat4 view;
    mat4 projection;
    mat4 viewProj;
    mat4 invViewProj;
    vec3 cameraPos;
};

layout (binding = MATRICES_BASE, std430) readonly buffer MODEL_MATRIX_BLOCK
{
    ModelInstance models[];
};

void main(void)
{
    const ModelInstance thisModel = models[gl_PrimitiveIDIn];

    const mat4 MVP = viewProj * thisModel.matrix;
    const vec3 bmin = thisModel.bmin;
    const vec3 bmax = thisModel.bmax;

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
