#define PARAMETERS_BASE 0

// Uniforms Blocks
#define TERRAIN_LAYERS_UBO_BASE 0
#define PER_VIEW_UBO_BASE 1
#define COMMON_PROPERTIES_UBO_BASE 2
#define SHADOW_MAPPING_UBO_BASE 3

// ---------------------------------------------------------------------------
// gGMF.b packs two unrelated things into one byte, the way the game's
// texObjKind alpha does: the high bits pick the lighting path in deferred.frag,
// the low 3 bits name the surface kind for decal masking.
//
// A decal's influenceType from space.bin is a bitmask over those kinds, tested
// as (influence >> kind) & 1. The game runs the same test through a 256x8
// "bitwise LUT" texture, which is only a precomputed bit extraction - that is
// why no such texture ships in the packages.
//
// Observed influence bits across 453,564 decals on 64 maps: 1, 2, 4 and 5 only.
// Bit 1 is terrain (97% of all decals). Bits 4 and 5 are two distinct non
// terrain classes; the texture names point at buildings and paved surfaces,
// but that split is not confirmed, so roads currently answer to both.
#define GBUF_RENDER_MASK 248u
#define GBUF_KIND_MASK     7u

#define KIND_TERRAIN 1u
#define KIND_MODEL   4u
#define KIND_ROAD    5u

#define GFLAG_UNLIT   (  0.0 / 255.0)
#define GFLAG_MODEL   ( 68.0 / 255.0)   //  64 | KIND_MODEL
#define GFLAG_ROAD    ( 69.0 / 255.0)   //  64 | KIND_ROAD
#define GFLAG_TERRAIN (129.0 / 255.0)   // 128 | KIND_TERRAIN
#define GFLAG_SKY     (255.0 / 255.0)

// Decode gGMF.b. Compare the render bits, never the raw byte - the low 3 bits
// now carry the surface kind, so an == 64 test no longer matches a model.
#define GBUF_RENDER(b)      (uint((b) * 255.0 + 0.5) & 192u)
#define GBUF_KIND(b)        (uint((b) * 255.0 + 0.5) & GBUF_KIND_MASK)
#define GBUF_RENDER_MODEL   64u
#define GBUF_RENDER_TERRAIN 128u

// Kinds nuTerra actually writes into gGMF.b. A decal whose influence names none
// of these cannot be resolved - bit 5's surface has no G-buffer representation
// here, since road meshes bake into the VT page rather than drawing into the
// G-buffer - so those decals are let through rather than dropped.
#define GBUF_KNOWN_KINDS ((1u << KIND_TERRAIN) | (1u << KIND_MODEL))
// ---------------------------------------------------------------------------

// SSBO
#define MATRICES_BASE 0
#define DRAW_CANDIDATES_BASE 1
#define INDIRECT_BASE 2
#define MATERIALS_BASE 3
#define LODS_BASE 4
#define INDIRECT_GLASS_BASE 5
#define INDIRECT_DBL_SIDED_BASE 6
#define VISIBLES_BASE 8
#define VISIBLES_DBL_SIDED_BASE 9
#define TERRAIN_CHUNK_INFO_BASE 10

struct CandidateDraw
{
    uint model_id; // points to ModelInstance
    uint material_id; // points to MaterialProperties
    uint count;
    uint firstIndex;
    uint baseVertex;
    uint baseInstance;
    uint lod_level;
};

struct DrawElementsIndirectCommand
{
    uint count;
    uint instanceCount;
    uint firstIndex;
    uint baseVertex;
    uint baseInstance;
};

struct ModelInstance
{
    mat4 matrix;
    mat4 cached_mvp;
    vec3 bmin;
    uint lod_offset; // points to ModelLoD
    vec3 bmax;
    uint lod_count;
    uint batch_count; // hack!!!
    uint reserved1;
    uint reserved2;
    uint reserved3;
};

struct ModelLoD
{
    uint draw_offset;
    uint draw_count;
};

struct MaterialProperties
{
    vec4 g_atlasIndexes;
    vec4 g_colorTint;
    vec4 dirtParams;
    vec4 dirtColor;
    vec4 g_tile0Tint;
    vec4 g_tile1Tint;
    vec4 g_tile2Tint;
    vec4 g_tileUVScale;
    vec4 g_detailInfluences;
    vec4 g_detailRejectTiling;
    uvec2 maps[12];
    uint shader_type;
    uint texAddressMode;
    float alphaReference;
    bool g_useNormalPackDXT1;
    bool alphaTestEnable;
    bool g_enableAO;
    bool double_sided;
};

#ifdef USE_TERRAIN_LAYERS_UBO
layout(std140, binding = TERRAIN_LAYERS_UBO_BASE) uniform Layers {
    vec4 U[8];
    vec4 V[8];
    vec4 r1[8];
    vec4 r2[8];
    vec4 s[8];
} L;
#endif

#ifdef USE_PERVIEW_UBO
layout(binding = PER_VIEW_UBO_BASE, std140) uniform PerView {
    mat4 view;
    mat4 projection;
    mat4 viewProj;
    mat4 invViewProj;
    mat4 invView;
    vec3 cameraPos;
    uint pad;
    vec2 resolution;
};

layout(binding = SHADOW_MAPPING_UBO_BASE, std140) uniform ShadowMapping {
    mat4 lightSpaceMatrices[4];
};

// MUST match the splits in MapScene.ShadowMappingPass
const float cascadePlaneDistances[3] = {20.0, 75.0, 250.0};
const int cascadeCount = 3;
#endif

#ifdef USE_COMMON_PROPERTIES_UBO
layout(binding = COMMON_PROPERTIES_UBO_BASE) uniform CommonProperties {
    vec3 waterColor;
    float waterAlpha;
    vec3 fog_tint;
    float tess_level;
    vec3 sunColor;
    float mapMaxHeight;
    vec3 ambientColorForward;
    float mapMinHeight;
    vec2 map_size;
    float MEAN;
    float AMBIENT;
    float BRIGHTNESS;
    float SPECULAR;
    float GRAY_LEVEL;
    float GAMMA_LEVEL;
    float fog_level;
    float blend_macro_influence;
    float blend_global_threshold;
    float VirtualTextureSize;
    float AtlasScale;
    float PageTableSize;
    bool use_shadow_mapping;
    bool show_test_textures;
    float tonemap_exposure;
    float sun_strength;
    float sun_tint;
    float ambient_sat;
    float blend_height;
    float disabled_blend_height;
    float height_contrast;
    float macro_fade;
    float horizon_strength;
    float shadow_strength;
    float _pad_g;
    float _pad_h;
} props;
#endif

#ifdef USE_MODELINSTANCES_SSBO
layout(binding = MATRICES_BASE, std430) buffer ModelInstances
{
    ModelInstance models[];
};
#endif

#ifdef USE_CANDIDATE_DRAWS_SSBO
layout(binding = DRAW_CANDIDATES_BASE, std430) readonly buffer CandidateDraws
{
    CandidateDraw draw[];
};
#endif

#ifdef USE_MATERIALS_SSBO
layout(binding = MATERIALS_BASE, std430) readonly buffer Materials
{
    MaterialProperties material[];
};
#endif

#ifdef USE_LODS_SSBO
layout(binding = LODS_BASE, std430) readonly buffer ModelLoDs
{
    ModelLoD lods[];
};
#endif

#ifdef USE_INDIRECT_SSBO
layout(binding = INDIRECT_BASE, std430) buffer Indirect
{
    DrawElementsIndirectCommand command[];
};
#endif

#ifdef USE_INDIRECT_GLASS_SSBO
layout(binding = INDIRECT_GLASS_BASE, std430) writeonly buffer IndirectGlass
{
    DrawElementsIndirectCommand command_glass[];
};
#endif

#ifdef USE_INDIRECT_DOUBLE_SIDED_SSBO
layout(binding = INDIRECT_DBL_SIDED_BASE, std430) buffer IndirectDoubleSided
{
    DrawElementsIndirectCommand command_double_sided[];
};
#endif

#ifdef USE_VISIBLES_SSBO
layout(std430, binding = VISIBLES_BASE) buffer visibleBuffer {
    int visibles[];
};
layout(std430, binding = VISIBLES_DBL_SIDED_BASE) buffer visibleDblSidedBuffer {
    int visibles_dbl_sided[];
};
#endif

#ifdef USE_TERRAIN_CHUNK_INFO_SSBO
struct TerrainChunkInfo {
    mat4 modelMatrix;
    vec2 g_uv_offset;
    uint pad1;
    uint pad2;
};

layout(std430, binding = TERRAIN_CHUNK_INFO_BASE) readonly buffer TerrainChunkInfoBuffer {
    TerrainChunkInfo chunks[];
};
#endif

#ifdef USE_MIPLEVEL_FUNCTION
// This function estimates mipmap levels
// How far the sharp axis may run ahead of the blurred one. The game asks for 4
// on its own decal atlases in space.settings, so 4 is in keeping.
const float VT_MAX_ANISO = 4.0;

float MipLevel(vec2 uv, float size)
{
    vec2 dx = dFdx(uv * size);
    vec2 dy = dFdy(uv * size);

    // Squared lengths of the two axes of the pixel's footprint.
    const float major = max(dot(dx, dx), dot(dy, dy));
    float minor = min(dot(dx, dx), dot(dy, dy));

    // Taking the major axis - which is what a plain max() does - picks a mip
    // coarse enough for the longest direction and blurs the short one to match.
    // At a glancing angle the footprint is long and thin, so the whole surface
    // goes soft. Take the minor axis instead, but do not let it run more than
    // VT_MAX_ANISO ahead of the major or the long direction starts to shimmer.
    // Squared, because these are squared lengths.
    minor = max(minor, major / (VT_MAX_ANISO * VT_MAX_ANISO));

    return max(0.5 * log2(minor), 0);
}
#endif

#ifdef USE_VT_FUNCTIONS
// This function samples the page table and returns the page's
// position and mip level.
uvec2 SampleTable(usampler2D table, vec2 uv, float mip)
{
    const vec2 offset = fract(uv * props.PageTableSize) / props.PageTableSize;
    const uint pck = textureLod(table, uv - offset, mip).r;
    return uvec2((pck >> 5), (pck & 31));
}

// This functions samples from the texture atlas and returns the final color
vec4 SampleAtlas(sampler2DArray atlas, uvec2 page, vec2 uv)
{
    const float mipsize = exp2(page.y);
    uv = fract(uv * props.PageTableSize / mipsize);
    return texture(atlas, vec3(uv, page.x));
}
#endif
