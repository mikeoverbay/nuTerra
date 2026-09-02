#version 450 core

#extension GL_ARB_shading_language_include : require

#define USE_PERVIEW_UBO
#define USE_COMMON_PROPERTIES_UBO
#include "common.h" //! #include "../common.h"

// Screen space reflections for wet surfaces.
//
// A cubemap cannot put a building in a puddle - it is the sky, infinitely far
// away, and every pixel of it is the same no matter where the building stands.
// The only thing on hand that knows where the geometry actually is, is the
// frame we just finished drawing. So march the reflected ray through it and
// see what it hits.
//
// This runs AFTER the deferred resolve and samples the LIT colour. Reflecting
// the G-buffer albedo instead would put flat, unlit building faces in the
// water - the reflection has to be of the shaded scene, not of its raw paint.
//
// Deliberately NOT gated on sun_shadow. A puddle in a building's shadow still
// reflects that building; what it must not do is throw a sun glint, and that
// is gated separately in deferred.frag. The two together are the rule: sun
// terms need sun, geometry reflections do not.

layout(binding = 0) uniform sampler2D gLit;        // the resolved frame
layout(binding = 1) uniform sampler2D gNormal;     // view space, 0..1 encoded
layout(binding = 2) uniform sampler2D gGMF;        // .a = wetness mask
layout(binding = 3) uniform sampler2D gPosition;   // VIEW space, despite the name

uniform float ssr_intensity;
uniform int   ssr_steps;
uniform float ssr_thickness;   // metres a hit may be behind the ray and still count
uniform float ssr_stride;      // metres per step

in vec2 texCoord;
layout(location = 0) out vec4 fragColor;

void main(void)
{
    vec4 lit = texture(gLit, texCoord);
    fragColor = lit;

    float wet = texture(gGMF, texCoord).a;
    if (wet <= 0.001 || ssr_intensity <= 0.0) {
        return;
    }

    // gPosition is view space - every writer names the varying worldPosition
    // and every one of them stores view * model * vertex. The camera sits at
    // the origin here, which is the one convenience that buys.
    vec3 P = texture(gPosition, texCoord).xyz;
    if (P.z >= 0.0) {
        return;              // nothing was drawn here - sky
    }

    vec3 N = normalize(texture(gNormal, texCoord).xyz * 2.0 - 1.0);
    vec3 V = normalize(P);               // camera -> surface
    vec3 R = normalize(reflect(V, N));

    // A ray heading back at the camera has nothing in front of it to hit, and
    // marching one only produces noise along grazing surfaces.
    if (R.z > -0.02) {
        return;
    }

    // Fresnel: a wet surface is a mirror at a glancing angle and nearly clear
    // looking straight down. Without this every puddle reflects equally hard
    // from directly above, which is the tell that it is a screen effect.
    // NdotV unclamped, and back faces rejected outright.
    //
    // max(x, 0.0) does not guard a range here - it hides a SIGN. On a surface
    // facing away from the camera dot(-V,N) is negative, max() turns it into 0,
    // and fresnel comes out at 1: FULL strength SSR on a back face. The
    // reflected ray then lands anywhere it likes and smears the lit frame
    // across the surface, which is what showed up as a swirling marbled "wave"
    // pattern when looking up at the underside of a wet area. There is no
    // animation involved and never was.
    float NdotV = dot(-V, N);
    if (NdotV <= 0.0) {
        return;              // back facing - nothing here can reflect
    }
    float fresnel = pow(1.0 - NdotV, 4.0);

    vec3  pos = P;
    float hit = 0.0;
    vec2  hit_uv = vec2(0.0);

    for (int i = 0; i < ssr_steps; ++i) {
        // Step size grows with distance, so near reflections stay accurate and
        // far ones still reach something within the step budget.
        pos += R * (ssr_stride * (1.0 + float(i) * 0.08));

        vec4 clip = projection * vec4(pos, 1.0);
        if (clip.w <= 0.0) break;
        vec2 uv = (clip.xy / clip.w) * 0.5 + 0.5;
        if (any(lessThan(uv, vec2(0.0))) || any(greaterThan(uv, vec2(1.0)))) {
            break;           // left the screen - this is what SSR cannot do
        }

        float scene_z = texture(gPosition, uv).z;
        if (scene_z >= 0.0) continue;        // sky, nothing to hit

        // Both are negative going away from the camera, so the ray is behind
        // the surface when it is the more negative of the two. thickness stops
        // it matching against something far behind the intended occluder.
        float behind = scene_z - pos.z;
        if (behind > 0.0 && behind < ssr_thickness) {
            hit = 1.0;
            hit_uv = uv;
            break;
        }
    }

    if (hit == 0.0) {
        return;              // deferred's cubemap already filled this in
    }

    // Fade out at the screen edge. A reflection that stops dead at the border
    // of the frame is the single most obvious SSR artifact.
    vec2 edge = smoothstep(vec2(0.0), vec2(0.12), hit_uv) *
                (1.0 - smoothstep(vec2(0.88), vec2(1.0), hit_uv));
    float border = edge.x * edge.y;

    vec3 reflected = texture(gLit, hit_uv).rgb;

    fragColor.rgb = lit.rgb + reflected * wet * fresnel * border * ssr_intensity;
}
