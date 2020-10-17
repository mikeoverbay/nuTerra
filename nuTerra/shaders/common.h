#define PARAMETERS_BASE 0
#define PER_FRAME_DATA_BASE 1

#define MATRICES_BASE 0
#define DRAW_CANDIDATES_BASE 1
#define INDIRECT_BASE 2
#define MATERIALS_BASE 3

struct CandidateDraw
{
    uint model_id;
    uint material_id;
    uint count;
    uint firstIndex;
    uint baseVertex;
    uint baseInstance;
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
    uint offset;
    vec3 bmax;
    uint count;
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
};
