#version 450 core

in vec3 worldNormal;
in float heightFrac;
flat in float blocks;
out vec4 fragColour;

uniform vec3 kindColour;
// Darker than kindColour on purpose: these are the ones a tank goes through,
// and they should read as the quiet background to the ones that stop it.
uniform vec3 driveColour;

void main(void)
{
    vec3 n = normalize(worldNormal);
    const vec3 keyDir = normalize(vec3(0.45, 0.80, 0.35));
    float key = max(dot(n, keyDir), 0.0);

    // A tree that stops a hull gets its trunk brown at the foot and the
    // kind's green at the crown. A tree you drive through has no trunk to
    // shade, so it is one flat forest green all the way down.
    vec3 bark = vec3(0.32, 0.24, 0.17);
    vec3 c = (blocks > 0.5)
           ? mix(bark, kindColour, smoothstep(0.30, 0.45, heightFrac))
           : driveColour;

    fragColour = vec4(c * (0.35 + key * 0.75), 1.0);
}
