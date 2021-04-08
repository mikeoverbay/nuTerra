#version 450 core

layout (vertices = 3) out;

in VS_OUT {
    vec4 Vertex;
    vec2 UV;
    vec2 Global_UV;
    float ln;
    flat uint map_id;
} tcs_in[];

out TCS_OUT {
    vec4 Vertex;
    vec2 UV;
    vec2 Global_UV;
    float ln;
    flat uint map_id;
} tcs_out[];

void main(void)
{
    if (gl_InvocationID == 0) {
        gl_TessLevelInner[0] = 5.0;
        gl_TessLevelOuter[0] = 5.0;
        gl_TessLevelOuter[1] = 5.0;
        gl_TessLevelOuter[2] = 5.0;
    }

    gl_out[gl_InvocationID].gl_Position = gl_in[gl_InvocationID].gl_Position;

    // forward
    tcs_out[gl_InvocationID].Vertex = tcs_in[gl_InvocationID].Vertex;
    tcs_out[gl_InvocationID].UV = tcs_in[gl_InvocationID].UV;
    tcs_out[gl_InvocationID].Global_UV = tcs_in[gl_InvocationID].Global_UV;
    tcs_out[gl_InvocationID].ln = tcs_in[gl_InvocationID].ln;
    tcs_out[gl_InvocationID].map_id = tcs_in[gl_InvocationID].map_id;
}
