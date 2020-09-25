// gWriter fragment Shader. We will use this as a template for other shaders
#version 450 core

#extension GL_ARB_bindless_texture : require

// Output
layout (location = 0) out vec4 gColor;
layout (location = 1) out vec3 gNormal;
layout (location = 2) out vec3 gGMF;
layout (location = 3) out vec3 gPosition;

// Input from vertex shader
in VS_OUT
{
    vec2 UV;
    vec3 worldPosition;
    mat3 TBN;
    flat uint material_id;
} fs_in;

struct MaterialProperties
{
    sampler2D map1;
    sampler2D map2;
    sampler2D map3;
    sampler2D map4;

    uint shader_type;
    uint reserved;
};

// Material block
layout (binding = 2, std430) readonly buffer MATERIALS
{
    MaterialProperties material[];
};


// Shader types
#define FX_PBS_ext                1
#define FX_PBS_ext_dual           2
#define FX_PBS_ext_detail         3
#define FX_PBS_tiled_atlas        4
#define FX_PBS_tiled_atlas_global 5
#define FX_lightonly_alpha        6
#define FX_unsupported            7


void main(void)
{
    const MaterialProperties thisMaterial = material[fs_in.material_id];

    switch (thisMaterial.shader_type) {
    case FX_PBS_ext:
        gColor = texture(thisMaterial.map1, fs_in.UV);
        break;
    case FX_PBS_ext_dual:
        gColor = texture(thisMaterial.map1, fs_in.UV);
        break;
    case FX_PBS_ext_detail:
        gColor = texture(thisMaterial.map1, fs_in.UV);
        break;
    case FX_PBS_tiled_atlas:
        gColor = vec4(1.0, 0.0, 1.0, 1.0);
        break;
    case FX_PBS_tiled_atlas_global:
        gColor = vec4(1.0, 1.0, 0.0, 1.0);
        break;
    case FX_lightonly_alpha:
        gColor = texture(thisMaterial.map1, fs_in.UV);
        break;
    case FX_unsupported:
        gColor = vec4(1.0, 1.0, 1.0, 1.0);
        break;
    default:
        gColor = vec4(0.0, 0.0, 0.0, 1.0);
    }

    gColor.a = 1.0;
    gPosition = fs_in.worldPosition;
}
