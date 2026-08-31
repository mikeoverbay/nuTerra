#version 450 core

// Fullscreen quad straight from gl_VertexID - no vertex buffer, no projection.
// Same 4 vertex TriangleStrip as msm_blur.vert, which the bloom blur reuses.
out vec2 texCoord;

void main(void)
{
    vec2 uv;
    if (gl_VertexID == 0)      uv = vec2(0.0, 1.0);
    else if (gl_VertexID == 1) uv = vec2(0.0, 0.0);
    else if (gl_VertexID == 2) uv = vec2(1.0, 1.0);
    else                       uv = vec2(1.0, 0.0);

    texCoord = uv;
    gl_Position = vec4(uv * 2.0 - 1.0, 0.0, 1.0);
}
