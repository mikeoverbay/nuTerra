// Deferred lighting vertex shader.
#version 450 core

uniform vec4 rect;
uniform mat4 ProjectionMatrix;

out vec2 UV;
out mat4 projMatrixInv;

void main(void)
{
    vec2 co;

    if (gl_VertexID == 0) {
        co = rect.xw;
        UV = vec2(0.0f, 1.0f);
    }
    else if (gl_VertexID == 1) {
        co = rect.xy;
        UV = vec2(0.0f, 0.0f);
    }
    else if (gl_VertexID == 2) {
        co = rect.zw;
        UV = vec2(1.0f, 1.0f);
    }
    else {
        co = rect.zy;
        UV = vec2(1.0f, 0.0f);
    }

	gl_Position = ProjectionMatrix * vec4(co, 0.0f, 1.0f);
	projMatrixInv = inverse(ProjectionMatrix);
}
