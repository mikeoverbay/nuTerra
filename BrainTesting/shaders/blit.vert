#version 450 core

// A TEXTURED QUAD IN SCREEN PIXELS. Used to put an off-screen target back on
// the display: the scope renders into its own framebuffer at its own
// resolution, and this is what hangs the result in a corner.
layout(location = 0) in vec2 vertexPixel;
layout(location = 1) in vec2 vertexUV;

uniform vec2 screen;          // width, height in pixels

out vec2 uv;

void main(void)
{
    vec2 ndc = vec2( (vertexPixel.x / screen.x) * 2.0 - 1.0,
                     1.0 - (vertexPixel.y / screen.y) * 2.0 );
    gl_Position = vec4(ndc, 0.0, 1.0);
    uv = vertexUV;
}
