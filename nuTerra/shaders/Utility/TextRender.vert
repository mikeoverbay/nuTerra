#version 450 core

layout (location = 0) uniform mat4 ProjectionMatrix;
layout (location = 1) uniform vec4 rect;
layout (location = 2) uniform float index;
layout (location = 3) uniform float divisor;
layout (location = 4) uniform int col_row;

layout (location = 0) out VS_OUT {
    vec2 texCoord;
} vs_out;


void main(void)
{
    vec2 uv;
    vec2 co;

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

    vec2 scale = vec2(1.0f/divisor);
    vec2 uvs = uv*scale + vec2(scale * index);
    if (col_row == 0) {
        uvs.x = uv.x;
    } else {
        uvs.y = uv.y;
    }

    vs_out.texCoord = uvs;
}
