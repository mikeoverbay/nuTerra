#version 450 core

// A 2D OVERLAY, IN PIXELS.
//
// Position is screen pixels with the origin top-left, which is the frame every
// other part of this app already thinks in - the scope's maths, ImGui's mouse,
// the window size. Converting to clip space here rather than on the CPU means
// the geometry that gets uploaded is the geometry a person can reason about
// when it lands in the wrong place.
layout(location = 0) in vec2 vertexPixel;
layout(location = 1) in vec4 vertexColour;

uniform vec2 screen;          // width, height in pixels

out vec4 colour;

void main(void)
{
    vec2 ndc = vec2( (vertexPixel.x / screen.x) * 2.0 - 1.0,
                     1.0 - (vertexPixel.y / screen.y) * 2.0 );
    gl_Position = vec4(ndc, 0.0, 1.0);
    colour = vertexColour;
}
