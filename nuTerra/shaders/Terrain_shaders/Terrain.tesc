#version 450 core

layout (vertices = 3) out;

in VS_OUT {
    vec3 vertexPosition;
    vec3 vertexNormal;
    vec3 vertexTangent;
    vec2 UV;
    flat uint map_id;
} tcs_in[];

out TCS_OUT {
    vec3 vertexPosition;
    vec3 vertexNormal;
    vec3 vertexTangent;
    vec2 UV;
    flat uint map_id;
} tcs_out[];

void main(void)
{
    if (gl_InvocationID == 0) {
        gl_TessLevelInner[0] = 1.0;
        gl_TessLevelOuter[0] = 1.0;
        gl_TessLevelOuter[1] = 1.0;
        gl_TessLevelOuter[2] = 1.0;
    }

    gl_out[gl_InvocationID].gl_Position = gl_in[gl_InvocationID].gl_Position;

    // forward
    tcs_out[gl_InvocationID].vertexPosition = tcs_in[gl_InvocationID].vertexPosition;
    tcs_out[gl_InvocationID].vertexNormal = tcs_in[gl_InvocationID].vertexNormal;
    tcs_out[gl_InvocationID].vertexTangent = tcs_in[gl_InvocationID].vertexTangent;
    tcs_out[gl_InvocationID].UV = tcs_in[gl_InvocationID].UV;
    tcs_out[gl_InvocationID].map_id = tcs_in[gl_InvocationID].map_id;
}
