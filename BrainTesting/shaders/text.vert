#version 450 core

// TEXT, IN WHATEVER SPACE THE CALLER IS WORKING IN.
//
// One shader for the HUD and for world labels, because the only difference
// between them is the matrix. 2D passes an ortho built from the screen size and
// positions in pixels; 3D passes the camera's viewProj and positions already
// billboarded into world space. Two shaders would be two places for the vertex
// format to drift.
layout(location = 0) in vec3 vertexPosition;
layout(location = 1) in vec2 vertexUV;
layout(location = 2) in vec4 vertexColour;

uniform mat4 mvp;

out vec2 uv;
out vec4 colour;

void main(void)
{
    gl_Position = mvp * vec4(vertexPosition, 1.0);
    uv = vertexUV;
    colour = vertexColour;
}
