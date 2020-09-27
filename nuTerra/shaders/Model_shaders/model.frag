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
    vec2 UV2;
    vec3 worldPosition;
    mat3 TBN;
    flat uint material_id;
} fs_in;

struct MaterialProperties
{
    sampler2D maps[4];        /* 0  .. 32 */
    uint shader_type;         /* 32 .. 36 */
    bool g_useNormalPackDXT1; /* 36 .. 40 */
    float alphaReference;     /* 40 .. 44 */
    bool alphaTestEnable;     /* 44 .. 48 */
    vec4 g_atlasIndexes;      /* 48 .. 56 */
    vec4 g_atlasSizes;        /* 56 .. 72 */
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

// ================================================================================
// globals
MaterialProperties thisMaterial = material[fs_in.material_id];
vec3 normalBump;
// ================================================================================

// ================================================================================
// functions
// ================================================================================
void get_normal()
{
    float alphaCheck = gColor.a;
    if (thisMaterial.g_useNormalPackDXT1) {
        normalBump = (texture(thisMaterial.maps[1], fs_in.UV).rgb * 2.0f) - 1.0f;
    } else {
        vec4 normal = texture(thisMaterial.maps[1], fs_in.UV);
        normalBump.xy = normal.ag * 2.0 - 1.0;
        normalBump.z = sqrt(1.0 - dot(normalBump.xy, normalBump.xy));
        alphaCheck = normal.r;
    }
    if (thisMaterial.alphaTestEnable && alphaCheck < thisMaterial.alphaReference) {
        discard;
    }
    gNormal.xyz = fs_in.TBN * normalBump.xyz;
}

// ================================================================================
// Main start
// ================================================================================
void main(void)
{
    float renderType = 64.0/255.0; // 64 = PBS, 63 = light/bump
    thisMaterial.g_atlasSizes = vec4(1.0);
    switch (thisMaterial.shader_type) {
    case FX_PBS_ext:
        gColor = texture(thisMaterial.maps[0], fs_in.UV); // color
        gGMF.rg = texture(thisMaterial.maps[2], fs_in.UV).rg; // gloss/metal
        get_normal();
        break;
    case FX_PBS_ext_dual:
        gColor = texture(thisMaterial.maps[0], fs_in.UV); // color
        gColor *= texture(thisMaterial.maps[3], fs_in.UV2); // color
        gColor.rgb *= 1.5; // this will need to tweaking
        gGMF.rg = texture(thisMaterial.maps[2], fs_in.UV).rg; // gloss/metal
        get_normal();
        break;

    case FX_PBS_ext_detail:
        gColor = texture(thisMaterial.maps[0], fs_in.UV);
        break;

    case FX_PBS_tiled_atlas:
        gColor = texture(thisMaterial.maps[0], fs_in.UV);
        break;

    case FX_PBS_tiled_atlas_global:
        gColor = vec4(1.0, 1.0, 0.0, 1.0);
        break;

    case FX_lightonly_alpha:
        // gColor = texture(thisMaterial.maps[0], fs_in.UV);
        gColor = vec4(0.0,0.0,1.0,1.0); // debug
        break;

    case FX_unsupported:
        gColor = vec4(1.0, 1.0, 1.0, 1.0);
        break;

    default:
        gColor = vec4(0.0, 0.0, 0.0, 1.0);
    }

    gColor.a = 1.0;
    gPosition = fs_in.worldPosition;
    gGMF.b = renderType; // 64 = PBS, 63 = light/bump
}
