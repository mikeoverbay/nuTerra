#version 450 core
#extension GL_ARB_bindless_texture : require
#extension GL_ARB_shader_draw_parameters : require
#extension GL_ARB_shading_language_include : require

#define USE_MODELINSTANCES_SSBO
#define USE_CANDIDATE_DRAWS_SSBO
#include "common.h" //! #include "../common.h"

layout(location = 0) in vec3 vp;

uniform mat4 Ortho_Project;

out vec4 v_position;

void main(void)
{
    const CandidateDraw thisDraw = draw[gl_BaseInstanceARB];
    const ModelInstance thisModel = models[thisDraw.model_id];
    mat4 modelView = Ortho_Project * thisModel.matrix;

    gl_Position = modelView * vec4(vp.x, vp.y, vp.z, 1.0);
    v_position = gl_Position;
}
