#version 450 core

layout (location = 0) uniform mat4 ProjectionMatrix;
layout (location = 1) uniform vec4 rect;

layout (location=0) out VS_OUT {
    vec2 TexCoords;
} vs_out;


void main(void)
{
    vec2 co;
    vec2 uv;

    if (gl_VertexID == 0) {
        co = rect.xw;
        uv = vec2(0.0f, 1.0f);
    }
    else if (gl_VertexID == 1) {
        co = rect.xy;
        uv = vec2(0.0f, 0.0f);
    }
    else if (gl_VertexID == 2) {
        co = rect.zw;
        uv = vec2(1.0f, 1.0f);
    }
    else {
        co = rect.zy;
        uv = vec2(1.0f, 0.0f);
    }

    gl_Position = ProjectionMatrix * vec4(co, 0.0f, 1.0f);
    vs_out.TexCoords = uv;
}
