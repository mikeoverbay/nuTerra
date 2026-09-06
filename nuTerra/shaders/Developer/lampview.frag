#version 450 core

// Plain Phong on a flat grey. No textures, no material, no G-buffer.
//
// The job is reading SHAPE - where the bulb sits on a lamp post - and a
// diffuse map is actively in the way of that: a dark painted fixture against a
// dark painted pole hides the very silhouette being measured. Untextured grey
// with one light shows the form and nothing else.

in vec3 fNormal;
in vec3 fPos;

uniform vec3 light_dir;    // WORLD space, pointing at the light
uniform vec3 view_pos;
uniform vec3 base_color;

out vec4 outColor;

void main(void)
{
    vec3 N = normalize(fNormal);

    // Two-sided. These meshes are hollow shells with no bottom faces, so a
    // view from underneath sees backfaces; lighting them by the raw normal
    // makes whole panels read as black holes rather than as surfaces.
    vec3 V = normalize(view_pos - fPos);
    if (dot(N, V) < 0.0) N = -N;

    vec3 L = normalize(light_dir);

    float ambient = 0.28;
    float diff = max(dot(N, L), 0.0) * 0.75;

    vec3 H = normalize(L + V);
    float spec = pow(max(dot(N, H), 0.0), 24.0) * 0.35;

    outColor = vec4(base_color * (ambient + diff) + vec3(spec), 1.0);
}
