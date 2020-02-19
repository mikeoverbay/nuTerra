#version 430 core

uniform mat4 ProjectionMatrix;
uniform vec4 rect;
uniform vec4 color;

void main(void)
{
    vec2 co;

    if (gl_VertexID == 0) {
        co = rect.xy;
    }
    else if (gl_VertexID == 1) {
        co = rect.zw;
    }
    gl_Position = ProjectionMatrix * vec4(co, 0.0f, 1.0f);
}
