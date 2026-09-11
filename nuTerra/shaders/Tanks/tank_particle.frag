#version 450 core

layout (binding = 0) uniform sampler2D atlas;

// 0 blends, 1 adds. One equation serves both: the caller sets
// One / OneMinusSrcAlpha, which is "over" when an alpha is written and pure
// addition when it is zero. Smoke has to cover what is behind it; a flame has
// to add to it.
uniform int additive;

in vec2 vOffset;
in vec4 vColour;
in vec4 vUV;

layout (location = 0) out vec4 fragColor;

void main(void)
{
    vec3 rgb;
    float a;

    if (vUV.z > vUV.x) {
        // A FRAME OUT OF THE GAME'S OWN ATLAS. The quad's -1..1 offset maps
        // across the frame's rectangle; nothing was cut out of the atlas, so
        // this is the only place the rectangle is needed.
        vec2 t = vOffset * 0.5 + 0.5;
        vec4 c = texture(atlas, mix(vUV.xy, vUV.zw, t));
        rgb = c.rgb * vColour.rgb;
        a = c.a * vColour.a;
    } else {
        // No artwork: a soft round puff. Squared falloff rather than a hard
        // edge, because a trail is hundreds of these overlapping and a visible
        // rim on each turns the streak into a string of beads.
        float d = length(vOffset);
        if (d > 1.0) discard;
        float s = 1.0 - d;
        rgb = vColour.rgb;
        a = vColour.a * s * s;
    }

    if (a <= 0.002) discard;

    // PREMULTIPLIED, and alpha carried through. The caller picks the blend:
    // One / OneMinusSrcAlpha over a premultiplied source is "over" when alpha
    // is present and pure addition when it is zero, so smoke can cover and a
    // flame can add without two shaders.
    fragColor = vec4(rgb * a, (additive != 0) ? 0.0 : a);
}
