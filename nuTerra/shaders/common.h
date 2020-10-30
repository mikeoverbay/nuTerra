#define PARAMETERS_BASE 0

// Uniforms Blocks
#define TERRAIN_LAYERS_UBO_BASE 0
#define PER_VIEW_UBO_BASE 1

// SSBO
#define MATRICES_BASE 0
#define DRAW_CANDIDATES_BASE 1
#define INDIRECT_BASE 2
#define MATERIALS_BASE 3
#define LODS_BASE 4
#define INDIRECT_GLASS_BASE 5

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
    vec4 g_atlasIndexes;      /* 0   .. 16 */
    vec4 g_atlasSizes;        /* 16  .. 32 */
    vec4 g_colorTint;         /* 32  .. 48 */
    vec4 dirtParams;          /* 48  .. 64 */
    vec4 dirtColor;           /* 64  .. 80 */
    vec4 g_tile0Tint;         /* 80  .. 96 */
    vec4 g_tile1Tint;         /* 96  .. 112 */
    vec4 g_tile2Tint;         /* 112 .. 128 */
    vec4 g_tileUVScale;       /* 128 .. 144 */
    sampler2D maps[6];        /* 144 .. 192 */
    uint shader_type;         /* 192 .. 196 */
    bool g_useNormalPackDXT1; /* 196 .. 200 */
    float alphaReference;     /* 200 .. 204 */
    bool alphaTestEnable;     /* 204 .. 208 */
    int texAddressMode;       /* 208 .. 212 */
    bool g_enableAO;          /* 212 .. 216 */
};

#ifdef USE_PERVIEW_UBO
layout(binding = PER_VIEW_UBO_BASE, std140) uniform PerView {
    mat4 view;
    mat4 projection;
    mat4 viewProj;
    mat4 invViewProj;
    vec3 cameraPos;
    vec2 resolution;
};
#endif

#ifdef USE_MODELINSTANCES_SSBO
layout(binding = MATRICES_BASE, std430) readonly buffer ModelInstances
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
layout(binding = INDIRECT_BASE, std430) writeonly buffer Indirect
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
