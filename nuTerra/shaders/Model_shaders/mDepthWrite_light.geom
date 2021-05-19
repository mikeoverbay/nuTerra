#version 450 core

#if defined(GL_NV_geometry_shader_passthrough) && defined(GL_NV_viewport_array2)
#define USE_MULTICAST
#endif

#ifdef USE_MULTICAST
#extension GL_NV_viewport_array2 : require
#extension GL_NV_geometry_shader_passthrough : require
#endif

layout(triangles) in;

#ifdef USE_MULTICAST

layout(passthrough) in gl_PerVertex {
    vec4 gl_Position;
};

layout(viewport_relative) out int gl_Layer;

layout(passthrough) in Block
{
    flat uint material_id;
    vec2 uv;
} gs_in[];

#else

layout(triangle_strip, max_vertices = 3) out;

in Block
{
    flat uint material_id;
    vec2 uv;
} gs_in[];

out Block
{
    flat uint material_id;
    vec2 uv;
} gs_out;

#endif

void main(void)
{

#ifdef USE_MULTICAST

    gl_ViewportMask[0] = 1;
    gl_Layer = 0;

#else

    for (int i = 0; i < gl_in.length(); ++i) {
        gl_Position = gl_in[i].gl_Position;
        gs_out.material_id = gs_in[i].material_id;
        gs_out.uv = gs_in[i].uv;
        gl_ViewportIndex = 0;
        gl_Layer = 0;
        EmitVertex();
    }
    EndPrimitive();

#endif

}
