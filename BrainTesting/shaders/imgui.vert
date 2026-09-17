#version 450 core

// ImGui's own vertex layout, which ImGuiController binds by these locations:
//   0  vec2 position, screen pixels
//   1  vec2 uv into the font atlas
//   2  vec4 colour, packed as four unsigned bytes and normalised
//
// Written for Brain Testing 2026-09-16. nuTerra has this shader compiled into
// its own pipeline but not as a file this app can read, so it is here rather
// than linked - the ONE thing about ImGui that is not shared.
layout(location = 0) in vec2 in_position;
layout(location = 1) in vec2 in_texCoord;
layout(location = 2) in vec4 in_color;

uniform mat4 projection_matrix;

out vec4 color;
out vec2 texCoord;

void main()
{
    gl_Position = projection_matrix * vec4(in_position, 0.0, 1.0);
    color = in_color;
    texCoord = in_texCoord;
}
