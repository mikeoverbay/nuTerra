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
        uv = rect.xw;
    }
    else if (gl_VertexID == 1) {
        co = rect.xy;
        uv = rect.xy;
    }
    else if (gl_VertexID == 2) {
        co = rect.zw;
        uv = rect.zw;
    }
    else {
        co = rect.zy;
        uv = rect.zy;
    }

    gl_Position = ProjectionMatrix * vec4(co, 0.0f, 1.0f);
	texCoord = uv;

}

