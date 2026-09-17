#version 450 core

// A RENDERED PANEL, STANDING IN THE WORLD.
//
// The quad's corners arrive already built from the camera's right and up, so
// the billboarding is done on the CPU where the camera basis already exists.
// Doing it here would mean passing that basis in as two more uniforms to save
// four vector adds a frame.
layout(location = 0) in vec3 vertexPosition;
layout(location = 1) in vec2 vertexUV;

uniform mat4 mvp;

out vec2 uv;

void main(void)
{
    gl_Position = mvp * vec4(vertexPosition, 1.0);
    uv = vertexUV;
}
