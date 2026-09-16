#version 450 core

// One building placement. No instancing: the model matrix is a uniform and
// each visible placement is its own call, which is enough at a few thousand
// and keeps the view-space clip on the CPU where it can be read.

layout(location = 0) in vec3 vertexPosition;
layout(location = 1) in vec4 vertexNormal;   // half floats, normalized

uniform mat4 viewProj;
uniform mat4 model;

out vec3 worldNormal;

void main(void)
{
    // M * v, NOT v * M. OpenTK stores Matrix4 row-major and uploads it
    // untransposed, so GLSL - which reads a mat4 column-major - receives the
    // transpose; multiplying on the LEFT there is what reproduces OpenTK's
    // own row-vector maths. Getting it backwards put one building across the
    // whole frame and hid the terrain behind it.
    vec4 world = model * vec4(vertexPosition, 1.0);

    // No non-uniform scale on a WoT placement, so the upper 3x3 rotates the
    // normal correctly and an inverse-transpose would be the same matrix for
    // more work.
    worldNormal = normalize(mat3(model) * vertexNormal.xyz);

    gl_Position = viewProj * world;
}
