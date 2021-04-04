#version 450 core

#extension GL_ARB_shading_language_include : require

#define USE_PERVIEW_UBO
#define USE_INDIRECT_SSBO
#define USE_INDIRECT_DOUBLE_SIDED_SSBO
#define USE_MODELINSTANCES_SSBO
#define USE_CANDIDATE_DRAWS_SSBO
#include "common.h" //! #include "../common.h"

out VS_OUT
{
    vec3 bboxCtr;
    vec3 bboxDim;
    flat uint model_id;
    flat uint objid;
} vs_out;

layout (location = 0) uniform int numAfterFrustum;

void main(void)
{
    vs_out.objid = gl_VertexID;

    if (gl_VertexID >= numAfterFrustum) {
        vs_out.model_id = draw[command_double_sided[gl_VertexID - numAfterFrustum].baseInstance].model_id;
    } else {
        vs_out.model_id = draw[command[gl_VertexID].baseInstance].model_id;
    }
    const ModelInstance thisModel = models[vs_out.model_id];

    const vec3 bmin = thisModel.bmin;
    const vec3 bmax = thisModel.bmax;

    vs_out.bboxCtr = ((bmin + bmax) * 0.5);
    vs_out.bboxDim = ((bmax - bmin) * 0.5);
}
