#version 450 core

uniform mat4 ProjectionMatrix;
uniform vec4 rect;

void main(void)
{
    vec2 co;

    if (gl_VertexID == 0) {
        co = rect.xw;
    }
    else if (gl_VertexID == 1) {
        co = rect.xy;
    }
    else if (gl_VertexID == 2) {
        co = rect.zw;
    }
    else {
        co = rect.zy;
    }

    gl_Position = ProjectionMatrix * vec4(co, 0.0f, 1.0f);
}
