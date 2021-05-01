#version 450 core

layout (vertices = 3) out;

layout(location = 0) in VS_OUT {
    vec3 vertexPosition;
    vec3 vertexNormal;
    vec3 vertexTangent;
    vec2 UV;
    flat int map_id;
    float fLevel;
} tcs_in[];

layout(location = 0) out TCS_OUT {
    vec3 vertexNormal;
    vec3 vertexTangent;
    vec2 UV;
    flat int map_id;
} tcs_out[];

void main(void)
{
    gl_TessLevelInner[0] = max(max(tcs_in[0].fLevel, tcs_in[1].fLevel), tcs_in[2].fLevel);
    gl_TessLevelOuter[0] = max(tcs_in[1].fLevel, tcs_in[2].fLevel);
    gl_TessLevelOuter[1] = max(tcs_in[0].fLevel, tcs_in[2].fLevel);
    gl_TessLevelOuter[2] = max(tcs_in[0].fLevel, tcs_in[1].fLevel);

    gl_out[gl_InvocationID].gl_Position = gl_in[gl_InvocationID].gl_Position;

    // forward
    tcs_out[gl_InvocationID].vertexNormal = tcs_in[gl_InvocationID].vertexNormal;
    tcs_out[gl_InvocationID].vertexTangent = tcs_in[gl_InvocationID].vertexTangent;
    tcs_out[gl_InvocationID].UV = tcs_in[gl_InvocationID].UV;
    tcs_out[gl_InvocationID].map_id = tcs_in[gl_InvocationID].map_id;
}
