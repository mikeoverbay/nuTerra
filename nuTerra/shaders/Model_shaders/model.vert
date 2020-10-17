// gWriter vertex Shader. We will use this as a template for other shaders
#version 460 core

#extension GL_ARB_bindless_texture : require
#extension GL_ARB_shader_draw_parameters : require
#extension GL_ARB_shading_language_include : require
#include "common.h"

layout(location = 0) in vec3 vertexPosition;
layout(location = 1) in vec4 vertexNormal;
layout(location = 2) in vec4 vertexTangent;
layout(location = 3) in vec4 vertexBinormal;
layout(location = 4) in vec2 vertexTexCoord1;
layout(location = 5) in vec2 vertexTexCoord2;

layout (binding = MATRICES_BASE, std430) readonly buffer MODEL_MATRIX_BLOCK
{
    ModelInstance models[];
};

layout (binding = DRAW_CANDIDATES_BASE, std430) readonly buffer CandidateDraws
{
    CandidateDraw draw[];
};

// Material block
layout (binding = MATERIALS_BASE, std430) readonly buffer MATERIALS
{
    MaterialProperties material[];
};

out VS_OUT
{
    vec2 TC1;
    vec2 TC2;
    vec3 worldPosition;
    mat3 TBN;
    flat uint material_id;
    flat uint model_id;
    vec2 UV1;
    vec2 UV2;
    vec2 UV3;
    vec2 UV4;
    vec2 scale_123;
    vec2 scale_4;
    vec2 offset_123;
    vec2 offset_4;
} vs_out;

uniform mat4 projection;
uniform mat4 view;

void main(void)
{
    const CandidateDraw thisDraw = draw[gl_BaseInstanceARB];
    const ModelInstance thisModel = models[thisDraw.model_id];
    const MaterialProperties thisMaterial = material[thisDraw.material_id];

    vs_out.material_id = thisDraw.material_id;
    vs_out.model_id = thisDraw.model_id;

    vs_out.TC1 = vertexTexCoord1;
    vs_out.TC2 = vertexTexCoord2;

    mat4 modelView = view * thisModel.matrix;
    mat3 normalMatrix = mat3(transpose(inverse(modelView)));

    // Transform position & normal to world space
    vs_out.worldPosition = vec3(modelView * vec4(vertexPosition, 1.0f));

    vec3 t = normalize(normalMatrix * vertexTangent.xyz);
    vec3 b = normalize(normalMatrix * vertexBinormal.xyz);
    vec3 n = normalize(normalMatrix * vertexNormal.xyz);

    vs_out.TBN = mat3(t, b, n);

    // Calculate vertex position in clip coordinates
    gl_Position = projection * modelView * vec4(vertexPosition, 1.0f);

    //============================================
    //Calculate UV1 to UV4
    //============================================
    vec4 At_size = thisMaterial.g_atlasSizes;

    ivec2 isize = textureSize(thisMaterial.maps[0],0);
    vec2 image_size;
    image_size.x = float(isize.x); //to float. AMD hates using int values with floats.
    image_size.y = float(isize.y);

    float padSize = 0.0625;
    float textArea = 0.875;

    vec2 halfPixel = vec2(0.5/image_size.x,0.5/image_size.y); // 1/2 pixel offset;
    vec2 offset = vec2(padSize/At_size.x, padSize/At_size.y);// + halfPixel; // border offset scaled by atlas tile count
    vs_out.offset_123 = offset;
    //common scale for UV1, UV2 and UV3
    vs_out.scale_123.x = 1.0 / At_size.x;         // UV length of one tile with border.
    vs_out.scale_123.y = 1.0 / At_size.y;
    vec2 UVs;
    //============================================
    vec2 tile;
    float index = thisMaterial.g_atlasIndexes.x;
    tile.y = floor(index/At_size.x);        // gets tile loaction in y
    tile.x = index - tile.y * At_size.x;    // gets tile location in x
    vs_out.UV1.x = tile.x * vs_out.scale_123.x;        // 0.0625 to 0.875 + (loc X * UV with border).
    vs_out.UV1.y = tile.y * vs_out.scale_123.y;        // 0.0625 to 0.875 + (loc Y * UV with border).

    index = thisMaterial.g_atlasIndexes.y;
    tile.y = floor(index/At_size.x);
    tile.x = index - tile.y * At_size.x;
    vs_out.UV2.x = tile.x * vs_out.scale_123.x;
    vs_out.UV2.y = tile.y * vs_out.scale_123.y;

    index = thisMaterial.g_atlasIndexes.z;
    tile.y = floor(index/At_size.x);
    tile.x = index - tile.y * At_size.x;
    vs_out.UV3.x = tile.x * vs_out.scale_123.x;
    vs_out.UV3.y = tile.y * vs_out.scale_123.y;

    vs_out.scale_123 = vs_out.scale_123 * vec2(textArea);
    //UV4 is used for blend.
    vs_out.scale_4.x = 1.0 / At_size.z;
    vs_out.scale_4.y = 1.0 / At_size.w;

    index = thisMaterial.g_atlasIndexes.w;
    tile.y = floor(index/At_size.z);
    tile.x = index - tile.y * At_size.z;

    vs_out.UV4.x = tile.x*vs_out.scale_4.x;
    vs_out.UV4.y = tile.y*vs_out.scale_4.y;

}
