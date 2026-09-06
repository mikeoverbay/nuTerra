#version 450 core

// One model, one matrix, no scene.
//
// Deliberately NOT the map's model.vert: that one needs the candidate-draw
// SSBO, the per-instance matrix buffer and a material id, none of which exist
// when the point is to look at a single mesh on its own. The vertex layout is
// the same, so it reads the same buffer - it just ignores everything past the
// normal.

layout(location = 0) in vec3 vertexPosition;
layout(location = 1) in vec4 vertexNormal;

uniform mat4 mvp;

out vec3 fNormal;
out vec3 fPos;

void main(void)
{
    fPos = vertexPosition;
    fNormal = normalize(vertexNormal.xyz);
    gl_Position = mvp * vec4(vertexPosition, 1.0);
}
