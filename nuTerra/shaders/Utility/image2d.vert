#version 430 core

uniform mat4 ProjectionMatrix;
uniform vec4 rect;

out vec2 texCoord;

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
    texCoord = uv;
}
