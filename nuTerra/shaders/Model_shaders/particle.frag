#version 450 core

#extension GL_ARB_shading_language_include : require

#define USE_COMMON_PROPERTIES_UBO
#include "common.h" //! #include "../common.h"

// The pass draws premultiplied, blend (ONE, ONE_MINUS_SRC_ALPHA), matching the
// volumetric FX pass so both composite the same way.

layout (binding = 0) uniform sampler2D atlas;
uniform int wireMode;   // debug: draw untextured so motion can be judged alone
layout (binding = 3) uniform sampler2D gPosition;   // view space, for soft edges

layout (location = 0) out vec4 outColor;

in VS_OUT
{
    vec2 uv;
    vec4 colour;
    float viewDist;
} fs_in;

void main(void)
{
    if (wireMode != 0) {
        // Opaque, no texture, no soft fade - just the card outline. The colour
        // carries the particle's age so flow is readable: green new, red old.
        outColor = vec4(fs_in.colour.rgb, 1.0);
        return;
    }

    const vec4 tex = texture(atlas, fs_in.uv);

    // Soft particles, the same rule the volumetric pass uses: fade a card out
    // as it approaches whatever is behind it, so it does not cut a hard line
    // into the ground. Nothing drawn leaves gPosition at zero, which is sky and
    // must read as infinitely far.
    const vec3 scenePos  = texelFetch(gPosition, ivec2(gl_FragCoord.xy), 0).xyz;
    const float sceneDist = (abs(scenePos.z) < 1e-6) ? 1e30 : -scenePos.z;
    const float softFade  = clamp((sceneDist - fs_in.viewDist) / 0.5, 0.0, 1.0);

    float alpha = tex.a * fs_in.colour.a * softFade;
    if (alpha <= 0.002) discard;

    const vec3 rgb = tex.rgb * fs_in.colour.rgb;
    outColor = vec4(rgb * alpha, alpha);
}
