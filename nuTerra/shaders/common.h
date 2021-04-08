#define PARAMETERS_BASE 0

// Uniforms Blocks
#define TERRAIN_LAYERS_UBO_BASE 0
#define PER_VIEW_UBO_BASE 1
#define COMMON_PROPERTIES_UBO_BASE 2

// SSBO
#define MATRICES_BASE 0
#define DRAW_CANDIDATES_BASE 1
#define INDIRECT_BASE 2
#define MATERIALS_BASE 3
#define LODS_BASE 4
#define INDIRECT_GLASS_BASE 5
#define INDIRECT_DBL_SIDED_BASE 6
#define LIGHTS_BASE 7
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
    vec4 g_atlasIndexes;      /* 0   .. 16 */
    vec4 g_atlasSizes;        /* 16  .. 32 */
    vec4 g_colorTint;         /* 32  .. 48 */
    vec4 dirtParams;          /* 48  .. 64 */
    vec4 dirtColor;           /* 64  .. 80 */
    vec4 g_tile0Tint;         /* 80  .. 96 */
    vec4 g_tile1Tint;         /* 96  .. 112 */
    vec4 g_tile2Tint;         /* 112 .. 128 */
    vec4 g_tileUVScale;       /* 128 .. 144 */
    vec4 g_detailInfluences;  /* 144 .. 160 */
    vec4 g_detailRejectTiling;
#ifdef GL_SPIRV
    uvec2 maps[6];            /* 160 .. 208 */
#else
    sampler2D maps[6];        /* 160 .. 208 */
#endif
    uint shader_type;         /* 208 .. 212 */
    uint texAddressMode;      /* 212 .. 216 */
    float alphaReference;     /* 216 .. 220 */
    bool g_useNormalPackDXT1; /* 220 .. 224 */
    bool alphaTestEnable;     /* 224 .. 228 */
    bool g_enableAO;          /* 228 .. 232 */
    bool double_sided;        /* 232 .. 236 */
};

#ifdef USE_PERVIEW_UBO
layout(binding = PER_VIEW_UBO_BASE, std140) uniform PerView {
    mat4 view;
    mat4 projection;
    mat4 viewProj;
    mat4 invViewProj;
    vec3 cameraPos;
    uint pad;
    vec2 resolution;
};
#endif

#ifdef USE_COMMON_PROPERTIES_UBO
layout(binding = COMMON_PROPERTIES_UBO_BASE) uniform CommonProperties {
    vec2 map_size;
    vec2 map_center;
    vec3 waterColor;
    float waterAlpha;
    vec3 fog_tint;
    uint light_count;
    vec3 sunColor;
    float mapMaxHeight;
    vec3 ambientColorForward;
    float mapMinHeight;
    float MEAN;
    float AMBIENT;
    float BRIGHTNESS;
    float SPECULAR;
    float GRAY_LEVEL;
    float GAMMA_LEVEL;
    float fog_level;
    float _start;
    float _end;
} props;
#endif

#ifdef USE_LIGHT_SSBO
struct light {
    vec3 location;
    float level;
    vec3 color;
    float fallOff;
    int inUse;
};

layout(std140, binding = LIGHTS_BASE) buffer Lights {
    light lights[];
};
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
    vec2 me_location;
    uint pad1;
    uint pad2;
};

layout(std430, binding = TERRAIN_CHUNK_INFO_BASE) readonly buffer TerrainChunkInfoBuffer {
    TerrainChunkInfo chunks[];
};
#endif