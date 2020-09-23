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
    sampler2D colorMap;
    sampler2D normalMap;
    sampler2D GMF_Map;
    uint reserved1;
    uint reserved2;
};

// Material block
layout (binding = 2, std430) readonly buffer MATERIALS
{
    MaterialProperties material[];
};

void main(void)
{
    const MaterialProperties thisMaterial = material[fs_in.material_id];

    gColor = vec4(texture(thisMaterial.colorMap, fs_in.UV).rgb, 1.0);
    gPosition = fs_in.worldPosition;
}
