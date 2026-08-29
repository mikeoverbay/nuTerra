#version 450 core

#extension GL_ARB_shading_language_include : require

#define USE_PERVIEW_UBO
#define USE_COMMON_PROPERTIES_UBO
#include "common.h" //! #include "../common.h"

// ===========================================================================
// Probe field inspector.
//
// A COMPLETE REPLACEMENT for the deferred pass, selected by the "show probe
// field" checkbox. It exists so the baked SH grid can be checked for placement
// and orientation WITHOUT any of it living in deferred.frag - the real
// lighting path never references the grid, so this view can never change how
// the scene renders.
//
// What it draws, per pixel of scene geometry:
//   * the field's irradiance evaluated against that surface's normal
//   * the probe lattice, so cells can be counted against known world features
//   * red   where the point falls outside the grid's XZ box
//   * amber where the point sits above the probe's stored reference height,
//     which is where the field fades out to its companion probe
// ===========================================================================

layout (location = 0) out vec4 outColor;

layout(binding = 0) uniform sampler2D gColor;
layout(binding = 1) uniform sampler2D gNormal;
layout(binding = 2) uniform sampler2D gGMF;
layout(binding = 3) uniform sampler2D gPosition;

// The baked field - RGBA16F volume, 8 slices. Seven carry one probe's packed
// SH9; slice 6's alpha is that probe's reference height; slice 7 is padding.
layout(binding = 11) uniform sampler3D sh_grid;

uniform vec4  sh_grid_uv;     // xy = offset, zw = 1/size, the game's packing
uniform float sh_grid_fade;   // 1 / fade distance in metres
uniform float sh_grid_offset; // metres to push the lookup along the normal
uniform vec3  sh_grid_sh9[9]; // the field's OWN companion probe

// How hard to scale the raw irradiance for display. It runs past 1.0, so
// without this the frame clips to white and hides the variation the view
// exists to show.
uniform float probe_exposure;

// Draw the probe lattice over the field.
uniform int probe_show_grid;

// Ramamoorthi & Hanrahan, same constants as the lighting path - used only for
// the companion probe, so the two ends of the fade are the same quantity.
vec3 eval_sh9(vec3 sh[9], vec3 n)
{
    const float c1 = 0.429043, c2 = 0.511664, c3 = 0.743125;
    const float c4 = 0.886227, c5 = 0.247708;

    return c4 * sh[0]
         + 2.0 * c2 * (sh[1] * n.y + sh[2] * n.z + sh[3] * n.x)
         + 2.0 * c1 * (sh[4] * n.x * n.y
                     + sh[5] * n.y * n.z
                     + sh[7] * n.x * n.z)
         + c3 * sh[6] * n.z * n.z
         - c5 * sh[6]
         + c1 * sh[8] * (n.x * n.x - n.y * n.y);
}

void main(void)
{
    const uint FLAG = uint(texelFetch(gGMF, ivec2(gl_FragCoord), 0).b * 255.0);

    // Sky and unlit pixels have no surface to evaluate against - pass them
    // through so the view still reads as a scene.
    if ((FLAG & GBUF_RENDER_MASK) == 0u ||
        (FLAG & GBUF_RENDER_MASK) == GBUF_RENDER_MASK) {
        outColor = texelFetch(gColor, ivec2(gl_FragCoord), 0);
        return;
    }

    vec3 Position = texelFetch(gPosition, ivec2(gl_FragCoord), 0).xyz;
    vec3 N        = normalize(texelFetch(gNormal, ivec2(gl_FragCoord), 0).xyz * 2.0 - 1.0);

    // gPosition is VIEW space despite every writer calling it worldPosition,
    // and the grid is baked in world space - so both have to be lifted.
    vec3 world_pos = (invView * vec4(Position, 1.0)).xyz;
    vec3 n_world   = normalize(mat3(invView) * N);

    // TWO uv's, and keeping them apart matters.
    //
    // uv_lookup is where the field is SAMPLED - pushed off the surface along
    // its normal, which is the whole job of sh_grid_offset.
    //
    // uv_world is where this pixel actually IS. Everything drawn as an overlay
    // uses that one, because the probe lattice and the box are fixed in the
    // world and must not move with the surface normal. Drawing them from the
    // offset uv made the lines tear apart: the normal changes discontinuously
    // across facets and normal maps, so the uv jumped between neighbouring
    // pixels and fwidth() - which measures exactly that jump - exploded the
    // line width wherever it happened.
    vec3 sample_pos  = world_pos + n_world * sh_grid_offset;
    vec2 uv_lookup   = sample_pos.xz * sh_grid_uv.zw - sh_grid_uv.xy;
    vec2 uv_world    = world_pos.xz  * sh_grid_uv.zw - sh_grid_uv.xy;
    vec2 uv = uv_lookup;

    // Slice centres of an 8 deep texture, so a Linear filter never straddles
    // two coefficient vectors.
    const float S = 1.0 / 8.0;
    vec4 c0 = textureLod(sh_grid, vec3(uv, 0.5 * S), 0.0);
    vec4 c1 = textureLod(sh_grid, vec3(uv, 1.5 * S), 0.0);
    vec4 c2 = textureLod(sh_grid, vec3(uv, 2.5 * S), 0.0);
    vec4 c3 = textureLod(sh_grid, vec3(uv, 3.5 * S), 0.0);
    vec4 c4 = textureLod(sh_grid, vec3(uv, 4.5 * S), 0.0);
    vec4 c5 = textureLod(sh_grid, vec3(uv, 5.5 * S), 0.0);
    vec4 c6 = textureLod(sh_grid, vec3(uv, 6.5 * S), 0.0);

    // The game's pre-convolved packing - one dot per band.
    vec3 lin  = vec3(dot(c0.wyz, n_world), dot(c1.wyz, n_world), dot(c2.wyz, n_world));
    vec4 q    = vec4(n_world.y * n_world.x, n_world.z * n_world.y,
                     n_world.z * n_world.z, n_world.x * n_world.z);
    vec3 quad = vec3(dot(c3, q), dot(c4, q), dot(c5, q))
              + c6.xyz * (n_world.x * n_world.x - n_world.y * n_world.y);
    vec3 local = max(vec3(c0.x, c1.x, c2.x) + lin + quad, vec3(0.0));

    vec3 col = local * probe_exposure;

    // ---- where the field stops being the answer ---------------------------
    // Tested on the WORLD uv, not the lookup: "is this pixel inside the box"
    // is a fact about where the pixel is, and testing the offset uv made the
    // boundary crawl along walls as the normal turned.
    bool outside = any(lessThan(uv_world, vec2(0.0))) || any(greaterThan(uv_world, vec2(1.0)));

    // c6.w is the probe's reference height, not a coefficient. Above it the
    // field fades out to the companion probe.
    float height_fade = clamp((world_pos.y - c6.w) * sh_grid_fade, 0.0, 1.0);

    if (outside) {
        // Beyond the box the companion probe is all there is - show what the
        // fallback actually looks like, tinted red so the edge is unmistakable.
        vec3 glob = max(eval_sh9(sh_grid_sh9, n_world), vec3(0.0)) * probe_exposure;
        col = mix(glob, vec3(1.0, 0.1, 0.1), 0.45);
    } else if (height_fade > 0.0) {
        // Amber where the point has climbed above the bake.
        col = mix(col, vec3(1.0, 0.65, 0.1), height_fade * 0.6);
    }

    // ---- the probe lattice ------------------------------------------------
    // One line per probe, so cells can be counted against a known feature -
    // 5 m apart on most maps, 10 m on Karelia.
    if (probe_show_grid != 0 && !outside) {
        vec2 dim  = vec2(textureSize(sh_grid, 0).xy);
        vec2 cell = uv_world * dim;

        // Take the derivative of the UNWRAPPED cell coordinate. fract() jumps
        // by a whole cell at every boundary, so fwidth() of anything derived
        // from it spikes exactly where the line is meant to be drawn - which
        // is the second reason these lines misbehaved.
        vec2 dcell = max(fwidth(cell), vec2(1e-6));

        // Distance to the nearest cell edge, converted to PIXELS. That makes
        // the line one pixel wide at any camera distance without measuring a
        // wrapped quantity.
        vec2  f  = fract(cell);
        vec2  d  = min(f, 1.0 - f) / dcell;
        float edge_px = min(d.x, d.y);
        float line = 1.0 - smoothstep(0.0, 1.0, edge_px);

        // Once cells are smaller than a pixel the lattice is pure moire, so
        // fade it out rather than drawing noise.
        line *= 1.0 - smoothstep(0.35, 1.0, length(dcell));

        col = mix(col, vec3(0.05, 1.0, 0.35), line * 0.55);
    }

    outColor = vec4(col, 1.0);
}
