#version 450 core

// One proxy tree per instance. The mat4 arrives as four vec4 attributes,
// locations 2..5, each advancing once per instance.

layout(location = 0) in vec3 vertexPosition;
layout(location = 1) in vec3 vertexNormal;
layout(location = 2) in vec4 m0;
layout(location = 3) in vec4 m1;
layout(location = 4) in vec4 m2;
layout(location = 5) in vec4 m3;
// 1 on the trunk's own vertices, 0 on the canopy. Per VERTEX.
layout(location = 6) in float vertexIsTrunk;
// 1 if a hull is stopped here, 0 if it drives through. Per INSTANCE.
layout(location = 7) in float instanceBlocks;

uniform mat4 viewProj;

out vec3 worldNormal;
out float heightFrac;
flat out float blocks;

void main(void)
{
    // Columns in the order OpenTK's rows arrive, so this is the same
    // transpose every other shader here takes - and the same M * v.
    mat4 model = mat4(m0, m1, m2, m3);
    blocks = instanceBlocks;

    // A TREE YOU CAN DRIVE THROUGH HAS NO TRUNK. The owner, 2026-09-16:
    // "make them with no trunks and make ones we can drive over forest green".
    // The trunk is what stops a hull, so drawing one on a tree that stops
    // nothing is the picture disagreeing with the grid - and the whole reason
    // this app exists is to see what the grid thinks.
    //
    // Pushed outside the clip volume rather than scaled to zero: a degenerate
    // triangle is still rasterised at the seams on some drivers and leaves a
    // dark speck where the trunk was.
    if (vertexIsTrunk > 0.5 && instanceBlocks < 0.5) {
        gl_Position = vec4(2.0, 2.0, 2.0, 1.0);
        worldNormal = vec3(0.0, 1.0, 0.0);
        heightFrac = 1.0;
        return;
    }

    vec4 world = model * vec4(vertexPosition, 1.0);
    worldNormal = normalize(mat3(model) * vertexNormal);

    // 0 at the foot, 1 at the crown - the fragment darkens the trunk with it,
    // so a trunk reads as a trunk without a second draw or a second colour.
    heightFrac = clamp(vertexPosition.y / 9.5, 0.0, 1.0);

    gl_Position = viewProj * world;
}
