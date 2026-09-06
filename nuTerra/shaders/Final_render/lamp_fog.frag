#version 450 core

#extension GL_ARB_shading_language_include : require

#define USE_PERVIEW_UBO
#include "common.h" //! #include "../common.h"

// Fog lit by one lamp - the shafts.
//
// Every other lighting calculation in this renderer runs AT A SURFACE. This one
// runs in the air between the camera and the surface, which is the only place a
// shaft exists: the beam under a street lamp is light scattering off fog in
// empty space, so nothing that shades geometry can ever produce it, however
// much fog is turned up.
//
// Marches the segment of the view ray that lies inside this lamp's sphere,
// testing the SAME baked shadow cube the surface lighting uses at every step.
// That test is what carves the shaft: where the cube says the step is occluded,
// no light is scattered there, and the gap between the lit steps and the
// unlit ones is the beam.
//
// Additive, and it reads nothing but scene depth. There is no material here -
// fog has no albedo, no normal and no BRDF - so the G-buffer is not needed and
// no new render target is either.

layout(binding = 0) uniform sampler2D gPosition;   // VIEW space

// The shadow CUBE, sampled per step - not the baked light volume.
//
// The volume was tried and reverted for god rays. At 64^3 over a lamp's
// bounding cube it is ~0.6 m a voxel, and the occluder that makes a beam from a
// STREET LAMP is the fixture and its post at 0.3-0.5 m: below the volume's
// resolution entirely, so the shafts it wants to cast are averaged away before
// the march ever sees them. The cube resolves ~0.08 m at 10 m, about eight
// times finer, and costs nothing measurable.
//
// The volume is still the right structure for big occluders - buildings - and
// it is still baked; it is simply the wrong tool for this.
layout(binding = 1) uniform samplerCubeArrayShadow lamp_shadow_map;
uniform float lamp_shadow_near;
uniform float lamp_shadow_bias;

uniform vec3  lamp_pos;      // world
uniform float lamp_range;
uniform vec3  lamp_color;    // sRGB as authored
uniform float lamp_level;
uniform int   lamp_index;    // layer in the cube array, -1 if it has no bake

uniform float fog_gain;      // scattering strength
uniform float fog_phase;     // Henyey-Greenstein g

// Extinction per metre - how fast the air swallows light.
//
// Without this the march is a plain sum and grows without bound with the path
// length, so the SAME gain that reads as a faint haze from outside a lamp
// whites the screen out from inside it: measured, ~20x more accumulated light
// across a 20 m sphere. Beer-Lambert makes the sum converge instead, so one
// setting works wherever the camera stands.
uniform float fog_density;
// The fog's OWN falloff, deliberately not the surfaces' light_falloff.
//
// They answer different questions. On a surface the falloff decides how fast
// the pool fades across the ground; in the air it decides how much of the
// sphere is worth seeing at all. At the surface value of 12, scattering at 90%
// of the radius is 1% of what it is at the bulb, so a 20 m lamp shows a glow
// about half that wide and reads as too small - the volume is the right size,
// the light in it just is not.
uniform float fog_falloff;
uniform int   fog_steps;

in vec3 fWorld;

layout(location = 0) out vec4 outColor;

const float PI = 3.14159265;

// Where the scattered light starts being compressed instead of added. Below it
// the strength slider is exactly linear; above it the whole vec3 is scaled by
// one factor, which is what keeps the lamp's colour at any intensity.
const float FOG_KNEE = 0.7;

// Ordered 4x4 Bayer, from the pixel's position and nothing else.
//
// The march has to start at a jittered offset or the steps show as concentric
// shells. The usual jitter is a per-pixel hash, and it is WRONG here: it
// changes every frame as the camera moves, so the shells become crawling
// grain, and these are recorded flights. A screen-space ordered pattern holds
// still relative to the screen and the eye reads it as texture, not motion.
float bayer(ivec2 p)
{
    const float m[16] = float[16](
         0.0,  8.0,  2.0, 10.0,
        12.0,  4.0, 14.0,  6.0,
         3.0, 11.0,  1.0,  9.0,
        15.0,  7.0, 13.0,  5.0);
    return m[(p.y & 3) * 4 + (p.x & 3)] * (1.0 / 16.0);
}

// Forward scattering. Without it fog lit by a lamp is a flat glow with no
// direction; this is what makes looking TOWARD a lamp brighter than looking
// across it, which is most of what reads as a beam.
float henyey_greenstein(float cos_t, float g)
{
    float g2 = g * g;
    float d = 1.0 + g2 - 2.0 * g * cos_t;
    return (1.0 - g2) / (4.0 * PI * pow(max(d, 1e-4), 1.5));
}

void main(void)
{
    // fWorld is a point on this pixel's view ray - the sphere's far surface -
    // so it gives the ray direction without reconstructing from the depth
    // buffer and the inverse projection.
    vec3 ro = cameraPos;
    vec3 rd = normalize(fWorld - ro);

    // Ray against the lamp's sphere. The near root can be behind the camera
    // when the camera is INSIDE the light, which is exactly when a shaft is
    // most worth having, so it is clamped rather than rejected.
    vec3 oc = ro - lamp_pos;
    float b = dot(oc, rd);
    float c = dot(oc, oc) - lamp_range * lamp_range;
    float disc = b * b - c;
    if (disc <= 0.0) discard;

    float sq = sqrt(disc);
    float t0 = max(-b - sq, 0.0);
    float t1 = -b + sq;
    if (t1 <= t0) discard;

    // Stop at the surface. Without this the march carries on through walls and
    // the fog inside a building lights up as brightly as the street.
    // gPosition is view space and the eye is the origin there, so its length is
    // the distance along the ray to whatever this pixel is looking at.
    vec3 vp = texelFetch(gPosition, ivec2(gl_FragCoord.xy), 0).xyz;
    float scene_t = length(vp);
    // A zero here is a pixel with no geometry - sky - not a surface at the
    // camera. Treating it as zero would cut every shaft against the sky to
    // nothing, which is where they are most visible.
    if (scene_t > 0.001) t1 = min(t1, scene_t);
    if (t1 <= t0) discard;

    int steps = max(fog_steps, 1);
    float dt = (t1 - t0) / float(steps);

    // Offset the first step by a fraction of dt, so the shells land at a
    // different depth on neighbouring pixels and average out.
    float jitter = bayer(ivec2(gl_FragCoord.xy));

    vec3 base = pow(lamp_color, vec3(2.2)) * lamp_level;

    vec3 acc = vec3(0.0);

    for (int i = 0; i < steps; ++i)
    {
        float t = t0 + (float(i) + jitter) * dt;
        vec3 p = ro + rd * t;

        // How much of what is scattered at THIS point still reaches the eye.
        //
        // From the sample's own position, not accumulated step by step. The
        // accumulated form was advanced after the accumulate, so every
        // `continue` below - a step in shadow, a step outside the range -
        // skipped it, and the eye-ward path only lost light across the LIT
        // steps. Air in shadow extinguishes just the same: lit fog seen
        // through 10 m of a wall's shadow came out twice as bright as the same
        // fog seen in the clear. Evaluating at t also puts the transmittance
        // at the jittered sample rather than at the step start, which removes
        // a fixed 4x4 brightness pattern the Bayer offset was leaving behind.
        float trans = exp(-fog_density * (t - t0));
        // Nothing past here can reach the eye, so stop walking. Checked before
        // the continues so a shadowed tail cannot keep the march alive.
        if (trans < 0.002) break;

        vec3 d = lamp_pos - p;
        float dist = length(d);
        if (dist >= lamp_range) continue;

        // The same falloff shape the surfaces get, so a shaft and the pool it
        // lands in agree about where the light stops.
        float s = dist / lamp_range;
        float s4 = s * s * s * s;
        float win = clamp(1.0 - s4, 0.0, 1.0);
        float atten = (win * win) / (1.0 + fog_falloff * s * s);

        float vis = 1.0;
        if (lamp_index >= 0)
        {
            // No normal offset - there is no surface here to bias off, and fog
            // does not self-shadow, so acne cannot happen.
            vec3 sd = -d;
            float mt = max(max(abs(sd.x), abs(sd.y)), abs(sd.z));
            float nz = lamp_shadow_near;
            float ref = (lamp_range - lamp_range * nz / max(mt, nz))
                      / max(lamp_range - nz, 1e-4);
            vis = texture(lamp_shadow_map, vec4(sd, float(lamp_index)),
                          ref - lamp_shadow_bias);
        }
        if (vis <= 0.0) continue;

        float cos_t = dot(normalize(d), rd);

        // Two extinctions, and they are different journeys: `trans` is the trip
        // from this step back to the EYE, and the exp below is the trip from
        // the LAMP out to this step. Only having the first makes the far side
        // of a pool as bright as the near side.
        float to_lamp = exp(-fog_density * dist);

        acc += base * atten * vis
             * henyey_greenstein(cos_t, fog_phase) * to_lamp * trans * dt;
    }

    vec3 lit = acc * fog_gain;

    // Rolled off on the PEAK CHANNEL, exactly as the surface lamps are.
    //
    // Without it the fog clips per channel and a bright shaft arrives WHITE:
    // measured on a capture, 3.2% of the frame was pure white, 18.4% was over
    // 240, and the hottest 1% had drifted to 1.00/0.96/0.81 against an authored
    // 1.00/0.85/0.63. That is the same failure the surfaces had before the
    // roll-off, in a different pass - scattering is the lamp's colour or it is
    // nothing, and clipping is what takes the colour away first.
    //
    // Scaling all three by one factor preserves the ratio at any intensity, so
    // over-driving the strength slider now saturates toward the LAMP's colour
    // instead of toward white.
    float pk = max(lit.r, max(lit.g, lit.b));
    if (pk > FOG_KNEE)
    {
        float over = pk - FOG_KNEE;
        float head = 1.0 - FOG_KNEE;
        lit *= (FOG_KNEE + head * over / (over + head)) / pk;
    }

    // Alpha 0: this pass is ADDITIVE and must never attenuate what is already
    // there. Scattering adds light to the air, it does not cover the scene.
    outColor = vec4(lit, 0.0);
}
