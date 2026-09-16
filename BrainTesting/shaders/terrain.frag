#version 450 core

// One flat colour and a cheap directional term. NO TEXTURES - that is the
// requirement, not a shortcut. The lambert is here so the ground reads as
// SHAPE: an unlit mesh of one colour is a silhouette, and an AI harness whose
// operator cannot see a ridge is not showing him the thing he is testing on.

in vec3 worldNormal;
in vec3 worldPos;

out vec4 fragColour;

uniform vec3 baseColour = vec3(0.42, 0.45, 0.38);

void main(void)
{
    vec3 n = normalize(worldNormal);

    // A fixed key light from up and to one side. Not the map's sun: this app
    // does not read the environment's lighting, and a light that moved with
    // the map would make two maps uncomparable at a glance.
    const vec3 keyDir = normalize(vec3(0.45, 0.80, 0.35));
    float key = max(dot(n, keyDir), 0.0);

    // A dim fill from below so downward faces are not pure black - a cliff
    // undercut reads as a hole otherwise.
    float fill = 0.25 + 0.15 * max(dot(n, vec3(0.0, -1.0, 0.0)), 0.0);

    // Every 100 m, one faint line. The chunk grid is 100 m, and having it
    // visible turns "roughly over there" into a countable distance when
    // someone is looking at where a tank stopped.
    vec2 g = abs(fract(worldPos.xz / 100.0 - 0.5) - 0.5) / fwidth(worldPos.xz / 100.0);
    float grid = 1.0 - min(min(g.x, g.y), 1.0);

    vec3 c = baseColour * (fill + key * 0.85);
    c = mix(c, vec3(0.55, 0.58, 0.52), grid * 0.35);

    fragColour = vec4(c, 1.0);
}
