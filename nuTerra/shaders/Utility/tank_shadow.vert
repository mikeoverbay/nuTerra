#version 450 core

// Depth-only, one tank's hull box into one layer of the tank shadow array.
//
// A box today and the mesh tomorrow: the skinned draw lives in MapTanks, which
// is another session's file, and one owner per file is the rule. Everything
// around this - the layers, the matrices, the range test, the fade - is proved
// by a box, and swapping in a real draw changes nothing here but the geometry.
layout(location = 0) in vec3 vPos;

uniform mat4 mvp;

void main(void)
{
    gl_Position = mvp * vec4(vPos, 1.0);
}
