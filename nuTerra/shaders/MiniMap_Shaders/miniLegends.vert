#version 430 core

uniform mat4 ProjectionMatrix;
uniform vec4 rect;
uniform float index;
uniform int col_row;
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

    vec2 scale = vec2(1.0f/16.0f);
    vec2 uvs = uv*scale + vec2(scale * index)-0.001;
    if ( col_row == 0 ){
    uvs.x = uv.x;
    }else{
    uvs.y = uv.y;
    }
    texCoord = uvs;
}
