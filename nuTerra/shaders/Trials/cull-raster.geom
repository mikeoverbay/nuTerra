#version 450 core

#extension GL_ARB_shading_language_include : require

#define USE_PERVIEW_UBO
#define USE_INDIRECT_SSBO
#define USE_INDIRECT_DOUBLE_SIDED_SSBO
#define USE_MODELINSTANCES_SSBO
#define USE_CANDIDATE_DRAWS_SSBO
#define USE_VISIBLES_SSBO
#include "common.h" //! #include "../common.h"

layout (points) in;
layout (triangle_strip, max_vertices = 14) out;

flat out int objid;

void main(void)
{
    objid = gl_PrimitiveIDIn;

#ifdef DBL_SIDED
    const ModelInstance thisModel = models[draw[command_double_sided[gl_PrimitiveIDIn].baseInstance].model_id];
#else
    const ModelInstance thisModel = models[draw[command[gl_PrimitiveIDIn].baseInstance].model_id];
#endif

    const vec3 bmin = thisModel.bmin * 1.01;
    const vec3 bmax = thisModel.bmax * 1.01;

    const mat4 MVP = viewProj * thisModel.matrix;
    vec4 p1 = MVP * vec4(bmax.x, bmin.y, bmax.z, 1.0);
    vec4 p2 = MVP * vec4(bmin.xy, bmax.z, 1.0);
    vec4 p3 = MVP * vec4(bmax, 1.0);
    vec4 p4 = MVP * vec4(bmin.x, bmax.yz, 1.0);
    vec4 p5 = MVP * vec4(bmax.x, bmin.yz, 1.0);
    vec4 p6 = MVP * vec4(bmin, 1.0);
    vec4 p7 = MVP * vec4(bmin.x, bmax.y, bmin.z, 1.0);
    vec4 p8 = MVP * vec4(bmax.xy, bmin.z, 1.0);
    
    gl_Position = p4;
    EmitVertex();

    gl_Position = p3;
    EmitVertex();

    gl_Position = p7;
    EmitVertex();

    gl_Position = p8;
    EmitVertex();

    gl_Position = p5;
    EmitVertex();

    gl_Position = p3;
    EmitVertex();

    gl_Position = p1;
    EmitVertex();

    gl_Position = p4;
    EmitVertex();

    gl_Position = p2;
    EmitVertex();

    gl_Position = p7;
    EmitVertex();

    gl_Position = p6;
    EmitVertex();

    gl_Position = p5;
    EmitVertex();

    gl_Position = p2;
    EmitVertex();

    gl_Position = p1;
    EmitVertex();
}
