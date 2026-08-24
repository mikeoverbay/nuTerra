#version 450 core

// Same screen-space quad as image2d.vert - kept separate because Shader("name")
// pairs name.vert with name.frag.
uniform mat4 ProjectionMatrix;
uniform vec4 rect;
out vec2 texCoord;

void main(void)
{
    vec2 uv;
    vec2 co;

    if (gl_VertexID == 0)      { co = rect.xw; uv = vec2(0.0, 1.0); }
    else if (gl_VertexID == 1) { co = rect.xy; uv = vec2(0.0, 0.0); }
    else if (gl_VertexID == 2) { co = rect.zw; uv = vec2(1.0, 1.0); }
    else                       { co = rect.zy; uv = vec2(1.0, 0.0); }

    gl_Position = ProjectionMatrix * vec4(co, 0.0, 1.0);
    texCoord = uv;
}
