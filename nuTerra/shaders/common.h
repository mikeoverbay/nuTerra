
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
