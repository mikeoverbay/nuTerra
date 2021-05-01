#version 450 core

uniform mat4 ProjectionMatrix;
uniform vec4 rect;

out flat int id;
out vec2 texCoord;

void main(void)
{
    id = gl_InstanceID;

    vec2 co;

    vec2 off = vec2(gl_InstanceID % 32, gl_InstanceID / 32);

    if (gl_VertexID == 0) {
        co = rect.xw;
        texCoord = vec2(0.0f, 1.0f);
    }
    else if (gl_VertexID == 1) {
        co = rect.xy;
        texCoord = vec2(0.0f, 0.0f);
    }
    else if (gl_VertexID == 2) {
        co = rect.zw;
        texCoord = vec2(1.0f, 1.0f);
    }
    else {
        co = rect.zy;
        texCoord = vec2(1.0f, 0.0f);
    }

    co += vec2(1.0, -1.0) * off * (rect.zw - rect.xy);

    gl_Position = ProjectionMatrix * vec4(co, 0.0f, 1.0f);
}
