#version 450 core

// Outland albedo bake, resolve pass: normalize the accumulated weighted sum.
// accum.rgb = sum(tile.rgb * w), accum.a = sum(w).

layout(location = 0) out vec4 baked;

layout(binding = 0) uniform sampler2D accum;

in vec2 texCoord;

void main(void)
{
    vec4 a = texture(accum, texCoord);
    vec3 c = a.rgb / max(a.a, 1e-4);
    // .a is the slot the game's combine uses to author where the detail map's
    // rgb (not just its grayscale) shows through. Nothing bakes it yet, so 0.
    baked = vec4(c, 0.0);
}
