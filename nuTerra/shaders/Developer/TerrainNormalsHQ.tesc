#version 450 core

layout (vertices = 3) out;

in VS_OUT {
    vec3 n;
    vec3 t;
    vec3 b;
    vec2 uv;
    flat uint map_id;
    float fLevel;
} tcs_in[];

out TCS_OUT {
    vec3 n;
    vec3 t;
    vec3 b;
    vec2 uv;
    flat uint map_id;
} tcs_out[];

void main(void)
{
    gl_TessLevelInner[0] = max(max(tcs_in[0].fLevel, tcs_in[1].fLevel), tcs_in[2].fLevel);
    gl_TessLevelOuter[0] = max(tcs_in[1].fLevel, tcs_in[2].fLevel);
    gl_TessLevelOuter[1] = max(tcs_in[0].fLevel, tcs_in[2].fLevel);
    gl_TessLevelOuter[2] = max(tcs_in[0].fLevel, tcs_in[1].fLevel);

    gl_out[gl_InvocationID].gl_Position = gl_in[gl_InvocationID].gl_Position;

    // forward
    tcs_out[gl_InvocationID].n = tcs_in[gl_InvocationID].n;
    tcs_out[gl_InvocationID].t = tcs_in[gl_InvocationID].t;
    tcs_out[gl_InvocationID].b = tcs_in[gl_InvocationID].b;
    tcs_out[gl_InvocationID].uv = tcs_in[gl_InvocationID].uv;
    tcs_out[gl_InvocationID].map_id = tcs_in[gl_InvocationID].map_id;
}
