#version 450 core

uniform vec4 rect;
uniform mat4 ProjectionMatrix;

out VS_OUT {
    vec2 UV;
} vs_out;

void main(void)
{
    vec2 co;

    if (gl_VertexID == 0) {
        co = rect.xw;
        vs_out.UV = vec2(0.0f, 1.0f);
    }
    else if (gl_VertexID == 1) {
        co = rect.xy;
        vs_out.UV = vec2(0.0f, 0.0f);
    }
    else if (gl_VertexID == 2) {
        co = rect.zw;
        vs_out.UV = vec2(1.0f, 1.0f);
    }
    else {
        co = rect.zy;
        vs_out.UV = vec2(1.0f, 0.0f);
    }

	gl_Position = ProjectionMatrix * vec4(co, 0.0f, 1.0f);
}
