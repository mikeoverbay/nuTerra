#version 450 core

layout (binding = 0) uniform sampler2D card;

uniform float opacity;
uniform float aspect;   // card height / width, the same value the vertex uses
uniform float corner;   // corner radius, in units of the card's HEIGHT

in vec2 vUV;
in vec2 vAtlas;
in float vFade;

layout (location = 0) out vec4 fragColor;

void main(void)
{
    // THE CARD ITSELF IS FULLY OPAQUE and the shape is cut here.
    //
    // Everything TankCards draws into the atlas is drawn at alpha 1 over an
    // opaque panel, so a cell never holds a partial alpha. That is on purpose:
    // blending text over a half-transparent panel inside the atlas composites
    // unpremultiplied colour against unpremultiplied colour, which fringes
    // every glyph edge toward whatever the panel happens to sit over. Keeping
    // the cell opaque leaves nothing to get wrong in the bake, and makes the
    // card's transparency one uniform rather than a property of the pixels.
    //
    // So the rounding is a signed-distance box evaluated per pixel instead.
    // Measured in units of card HEIGHT, which is why x is divided by the
    // aspect - in raw UV the corners of a card three times wider than it is
    // tall come out as three different radii.
    vec2 p = (vUV - 0.5) * vec2(1.0 / aspect, 1.0);
    vec2 b = vec2(0.5 / aspect, 0.5) - vec2(corner);
    float d = length(max(abs(p) - b, 0.0)) - corner;

    // One pixel of feather, in that same space, so the corner stays smooth at
    // every distance with no mip chain to authorise it.
    float aa = max(fwidth(d), 1e-4);
    float shape = 1.0 - smoothstep(-aa, aa, d);

    float a = opacity * vFade * shape;
    if (a < 0.004) discard;

    fragColor = vec4(texture(card, vAtlas).rgb, a);
}
