#version 450 core

in vec3 worldNormal;
in float heightFrac;
out vec4 fragColour;

uniform vec3 kindColour;

void main(void)
{
    vec3 n = normalize(worldNormal);
    const vec3 keyDir = normalize(vec3(0.45, 0.80, 0.35));
    float key = max(dot(n, keyDir), 0.0);

    // Trunk brown at the foot, the kind's green at the crown. One mix rather
    // than two draws: the trunk is four faces and not worth a state change.
    vec3 bark = vec3(0.32, 0.24, 0.17);
    vec3 c = mix(bark, kindColour, smoothstep(0.30, 0.45, heightFrac));

    fragColour = vec4(c * (0.35 + key * 0.75), 1.0);
}
