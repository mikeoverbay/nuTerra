#version 450 core

// Outland albedo bake, resolve pass: normalize the accumulated weighted sum.
// accum.rgb = sum(tile.rgb * w), accum.a = sum(w).

layout(location = 0) out vec4 baked;

layout(binding = 0) uniform sampler2D accum;
layout(binding = 1) uniform sampler2D global_AM;

// Albedo-texel uv -> world XZ (the cascade's mirrored affine, per axis:
// world = A * uv + B). Matches patch_outland_heightmap and outland.vert.
uniform vec4 world_from_uv;   // A.xy, B.xy
// world XZ clamp rect: the terrain footprint, slightly inset. Clamping the
// world sample here makes the field's border tone continue outward across
// the whole cascade instead of wrapping the global map.
uniform vec4 field_rect;      // min.xy, max.xy
// world -> Global_UV affine (same frame t_mixer's Global_UV uses, REPEAT
// convention): guv = G_A * world + G_B.
uniform vec4 global_uv_aff;   // G_A.xy, G_B.xy
uniform int apply_global;
// Residual global influence at full distance (1 at the seam always).
uniform float global_base;

in vec2 texCoord;

void main(void)
{
    vec4 a = texture(accum, texCoord);
    vec3 c = a.rgb / max(a.a, 1e-4);

    // The game's own distant-terrain shader (terrain2_5 low-LOD variant,
    // 15 instructions) writes albedo = g_globalAlbedoMap sampled RAW - at
    // range the field IS the global map, verbatim. So the outland must
    // CONTINUE raw global at the seam: mix straight toward the clamped
    // global sample, full strength at the footprint line (pixel-matching
    // the field), relaxing to global_base at range so the authored tile
    // character survives in the far mountains. A plain mix cannot overshoot
    // brightness - the earlier luminance-weighted blend washed the sheet out.
    if (apply_global != 0) {
        vec2 world = world_from_uv.xy * texCoord + world_from_uv.zw;
        vec2 wc = clamp(world, field_rect.xy, field_rect.zw);
        vec2 guv = global_uv_aff.xy * wc + global_uv_aff.zw;
        vec4 g = texture(global_AM, guv);

        // Beyond the footprint the clamped sample degenerates into edge
        // smears (four flat quadrants at far-cascade scale), so fade it
        // toward the global map's mean (its deepest mip) over one
        // field-width - continuous everywhere, cascades agree.
        vec2 half_span = 0.5 * (field_rect.zw - field_rect.xy);
        vec2 out_d = max(field_rect.xy - world, world - field_rect.zw);
        float outside = length(max(out_d, vec2(0.0)));
        float fade = clamp(outside / max(half_span.x, half_span.y), 0.0, 1.0);
        // The frozen clamp projects the field's border row outward as
        // streaks; blurring lightly leaves soft streaks, and moving the
        // sample point drags features into comets. The stable answer: keep
        // the clamped position but sample a HEAVILY mipped global - an 8 m
        // local average right at the seam (local tone, no streakable
        // structure) rising to a ~250 m regional wash within half the fade,
        // with the deepest-mip mean finishing the far end.
        g = textureLod(global_AM, guv, mix(5.5, 8.0, min(fade * 2.5, 1.0)));
        vec4 g_mean = textureLod(global_AM, vec2(0.5), 12.0);
        vec3 gg = mix(g.rgb, g_mean.rgb, fade);

        float k = mix(1.0, clamp(global_base, 0.0, 1.0), fade);
        c = mix(c, gg, k);
    }

    // The slow mean-fade gradient spans hundreds of texels on the NEAR sheet
    // (one 8-bit step every ~12 texels = visible colour banding; the far
    // sheet compresses the same fade into a few texels and never shows it).
    // Interleaved gradient noise, +/- half an LSB, breaks the steps up
    // before the RGBA8 quantize.
    float ign = fract(52.9829189 * fract(dot(gl_FragCoord.xy, vec2(0.06711056, 0.00583715))));
    c += (ign - 0.5) / 255.0;

    // .a is the slot the game's combine uses to author where the detail map's
    // rgb (not just its grayscale) shows through. Nothing bakes it yet, so 0.
    baked = vec4(c, 0.0);
}
