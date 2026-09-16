#version 450 core

// SHADED BY TYPE, on the owner's ask. The colour is the kind's, from
// ModelKind.KIND_RGB - the same legend the flight bake writes and Flight Studio
// reads, so a building is the same ochre in all three.

in vec3 worldNormal;
out vec4 fragColour;

uniform vec3 kindColour;

void main(void)
{
    vec3 n = normalize(worldNormal);

    // The same key light the terrain uses, so a wall and the ground it stands
    // on are lit by one sun and a roof does not read as a different material.
    const vec3 keyDir = normalize(vec3(0.45, 0.80, 0.35));
    float key = max(dot(n, keyDir), 0.0);
    float fill = 0.35;

    fragColour = vec4(kindColour * (fill + key * 0.75), 1.0);
}
