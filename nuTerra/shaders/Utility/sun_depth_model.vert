#version 450 core

#extension GL_ARB_shader_draw_parameters : require
#extension GL_ARB_shading_language_include : require

#define USE_CANDIDATE_DRAWS_SSBO
#define USE_MODELINSTANCES_SSBO
#include "common.h" //! #include "../common.h"

layout(location = 0) in vec3 vertexPosition;
layout(location = 4) in vec2 vertexTexCoord1;

uniform mat4 sunViewProj;

// One model instance to leave out of this render, BIASED BY ONE, so that
// zero - which is what an int uniform reads as when nobody sets it - means
// "leave nothing out".
//
// Biased rather than sentinelled on -1 because this shader is shared with the
// SUN bake, which does not set it. Zero is a real instance, so a plain -1
// sentinel would have the sun quietly drop instance 0 out of its own shadows
// until something else happened to write the uniform.
//
// The lamp bake sets it per lamp: a light must not be shadowed by the model
// carrying it. See MapLampShadow.draw_models for what that measured.
uniform int skip_instance_p1;

// Carried solely so the fragment stage can run the cutout test. Same two
// members, same order, as mDepthWrite_light.vert hands its own fragment stage.
out Block
{
    flat uint material_id;
    vec2 uv;
} vs_out;

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
    // Sent outside the clip volume on every axis rather than discarded in the
    // fragment stage - the triangle never rasterises, so it costs nothing and
    // the depth-only fragment shader is not involved at all.
    if (int(thisDraw.model_id) + gl_InstanceID + 1 == skip_instance_p1)
    {
        vs_out.material_id = thisDraw.material_id;
        vs_out.uv = vertexTexCoord1;
        gl_Position = vec4(2.0, 2.0, 2.0, 1.0);
        return;
    }

    const mat4 model = models[thisDraw.model_id + gl_InstanceID].matrix;

    // material_id comes off the DRAW, not the instance. A shadow draw command
    // is emitted once per primitive GROUP with instanceCount = batch.count
    // behind a single baseInstance, and a primitive group carries exactly one
    // material - so this is uniform across the batch even though the model
    // matrix above is not.
    vs_out.material_id = thisDraw.material_id;
    vs_out.uv = vertexTexCoord1;

    gl_Position = sunViewProj * model * vec4(vertexPosition, 1.0);
}
