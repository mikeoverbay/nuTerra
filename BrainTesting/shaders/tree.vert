#version 450 core

// One proxy tree per instance. The mat4 arrives as four vec4 attributes,
// locations 2..5, each advancing once per instance.

layout(location = 0) in vec3 vertexPosition;
layout(location = 1) in vec3 vertexNormal;
layout(location = 2) in vec4 m0;
layout(location = 3) in vec4 m1;
layout(location = 4) in vec4 m2;
layout(location = 5) in vec4 m3;

uniform mat4 viewProj;

out vec3 worldNormal;
out float heightFrac;

void main(void)
{
    // Columns in the order OpenTK's rows arrive, so this is the same
    // transpose every other shader here takes - and the same M * v.
    mat4 model = mat4(m0, m1, m2, m3);

    vec4 world = model * vec4(vertexPosition, 1.0);
    worldNormal = normalize(mat3(model) * vertexNormal);

    // 0 at the foot, 1 at the crown - the fragment darkens the trunk with it,
    // so a trunk reads as a trunk without a second draw or a second colour.
    heightFrac = clamp(vertexPosition.y / 9.5, 0.0, 1.0);

    gl_Position = viewProj * world;
}
