#version 450 core

// Depth read as plain data, NOT a comparison - the caller has to drop
// TEXTURE_COMPARE_MODE to None first or this is undefined.
layout(binding = 0) uniform sampler2D depthMap;

// The map only occupies a slice of the depth range, so raw depth is a flat grey
// wash. These stretch whatever slice matters over the full 0..1 of the display.
uniform float lo = 0.0;
uniform float hi = 1.0;

in vec2 texCoord;
out vec4 fragColor;

void main(void)
{
    const float d = texture(depthMap, texCoord).r;
    const float v = clamp((d - lo) / max(hi - lo, 1e-6), 0.0, 1.0);

    // Near the sun reads bright. Untouched depth clears to 1.0, so anything the
    // bake never drew to shows as black and the map footprint is obvious - if
    // the whole panel is black, the sun camera is not looking at the map.
    fragColor = vec4(vec3(1.0 - v), 1.0);
}
