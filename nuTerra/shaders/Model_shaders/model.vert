// gWriter vertex Shader. We will use this as a template for other shaders
#version 460 core

#extension GL_ARB_shader_draw_parameters : require

layout(location = 0) in vec3 vertexPosition;
layout(location = 1) in vec4 vertexNormal;
layout(location = 2) in vec2 vertexTexCoord1;

struct CandidateDraw
{
    vec3 bmin;
    uint model_id;
    vec3 bmax;
    uint material_id;
    uint count;
    uint firstIndex;
    uint baseVertex;
    uint baseInstance;
};

layout (binding = 0, std140) readonly buffer MODEL_MATRIX_BLOCK
{
    mat4 model_matrix[];
};

layout (binding = 1, std430) readonly buffer CandidateDraws
{
    CandidateDraw draw[];
};

out VS_OUT
{
    vec2 UV;
    vec3 worldPosition;
    mat3 TBN;
    flat uint material_id;
} vs_out;

uniform mat4 projection;
uniform mat4 view;

void main(void)
{
    const CandidateDraw thisDraw = draw[gl_BaseInstanceARB];

    vs_out.material_id = thisDraw.material_id;
    vs_out.UV = vertexTexCoord1;

    mat4 modelView = view * model_matrix[thisDraw.model_id];

    // Transform position & normal to world space
    vs_out.worldPosition = vec3(modelView * vec4(vertexPosition, 1.0f));

    // Calculate vertex position in clip coordinates
    gl_Position = projection * modelView * vec4(vertexPosition, 1.0f);
}
