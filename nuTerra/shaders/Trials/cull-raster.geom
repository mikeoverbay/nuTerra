#version 450 core

#extension GL_ARB_shading_language_include : require

#define USE_PERVIEW_UBO
#define USE_MODELINSTANCES_SSBO
#include "common.h" //! #include "../common.h"

layout (points, invocations = 6) in;
layout (triangle_strip, max_vertices = 4) out;

in VS_OUT
{
    vec3 bboxCtr;
    vec3 bboxDim;
    flat uint model_id;
    flat uint objid;
} gs_in[1];

out GS_OUT {
    flat uint objid;
} gs_out;

void main(void)
{
    vec3 faceNormal = vec3(0);
    vec3 edgeBasis0 = vec3(0);
    vec3 edgeBasis1 = vec3(0);

    const uint id = gl_InvocationID % 3;
    const float proj = gl_InvocationID < 3 ? -1 : 1;

    if (id == 0) {
        faceNormal.x = gs_in[0].bboxDim.x;
        edgeBasis0.y = gs_in[0].bboxDim.y;
        edgeBasis1.z = gs_in[0].bboxDim.z;
    } else if(id == 1) {
        faceNormal.y = gs_in[0].bboxDim.y;
        edgeBasis1.x = gs_in[0].bboxDim.x;
        edgeBasis0.z = gs_in[0].bboxDim.z;
    } else if(id == 2) {
        faceNormal.z = gs_in[0].bboxDim.z;
        edgeBasis0.x = gs_in[0].bboxDim.x;
        edgeBasis1.y = gs_in[0].bboxDim.y;
    }

    const ModelInstance thisModel = models[gs_in[0].model_id];

    faceNormal = mat3(thisModel.matrix) * (faceNormal) * proj;
    edgeBasis0 = mat3(thisModel.matrix) * (edgeBasis0);
    edgeBasis1 = mat3(thisModel.matrix) * (edgeBasis1) * proj;

    vec3 worldCtr = (thisModel.matrix * vec4(gs_in[0].bboxCtr, 1)).xyz;

    gs_out.objid = gs_in[0].objid;
    gl_Position = viewProj * vec4(worldCtr + (faceNormal - edgeBasis0 - edgeBasis1),1);
    EmitVertex();

    gs_out.objid = gs_in[0].objid;
    gl_Position = viewProj * vec4(worldCtr + (faceNormal + edgeBasis0 - edgeBasis1),1);
    EmitVertex();

    gs_out.objid = gs_in[0].objid;
    gl_Position = viewProj * vec4(worldCtr + (faceNormal - edgeBasis0 + edgeBasis1),1);
    EmitVertex();

    gs_out.objid = gs_in[0].objid;
    gl_Position = viewProj * vec4(worldCtr + (faceNormal + edgeBasis0 + edgeBasis1),1);
    EmitVertex();
}
