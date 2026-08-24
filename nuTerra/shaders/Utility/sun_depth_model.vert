#version 450 core

#extension GL_ARB_shader_draw_parameters : require
#extension GL_ARB_shading_language_include : require

#define USE_CANDIDATE_DRAWS_SSBO
#define USE_MODELINSTANCES_SSBO
#include "common.h" //! #include "../common.h"

layout(location = 0) in vec3 vertexPosition;

uniform mat4 sunViewProj;

void main(void)
{
    // models[].matrix is the world transform. cached_mvp is baked for the camera
    // and is no use from the sun's point of view.
    const CandidateDraw thisDraw = draw[gl_BaseInstanceARB];

    // + gl_InstanceID is not optional. The shadow draw commands carry
    // instanceCount = batch.count behind a single baseInstance, so
    // gl_BaseInstanceARB is the same value for every instance in the batch.
    // Without the offset the whole batch renders stacked on the first instance
    // and only one model in it casts a shadow. mDepthWrite_light.vert, which
    // feeds the cascades from this same buffer, indexes it the same way.
    const mat4 model = models[thisDraw.model_id + gl_InstanceID].matrix;

    gl_Position = sunViewProj * model * vec4(vertexPosition, 1.0);
}
