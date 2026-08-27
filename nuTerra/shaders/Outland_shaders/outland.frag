#version 450 core

#extension GL_ARB_shading_language_include : require

#define USE_PERVIEW_UBO
#include "common.h" //! #include "../common.h"

// Transcribed from the game's PBS_ext_outland.10.dx11.fxo deferred pixel
// shader. At runtime the game samples ONLY three textures here - a baked
// albedo, the cascade normal map, and a detail albedo - the tile set and
// tilemap exist solely to bake the albedo at load (outland_bake_*).

layout (location = 0) out vec4 gColor;
layout (location = 1) out vec3 gNormal;
layout (location = 2) out vec4 gGMF;
layout (location = 3) out vec3 gPosition;
layout (location = 4) out vec3 gSurfaceNormals; // CA5 under attach_CNGPA, like model.frag

layout(binding = 0) uniform sampler2D baked_albedo;
layout(binding = 2) uniform sampler2D normal_map;
layout(binding = 3) uniform sampler2D detail_albedo;

// The game hardcodes detailUV = TC * 64 for both cascades.
uniform float detail_tiles;

// Per-pixel PBR from the cascade normal map: R as shine (gGMF.y), B as
// metal (gGMF.x). The game's compiled shader writes the (59,80)/255
// constants instead and only alpha-tests R - this is the owner's call to
// use the channels, A/B'd against the faithful constants via Settings.
uniform int pbr_from_nm;
uniform float outland_spec;

// Map-space -> nuTerra-world sign for the decoded normal XZ. Forced by the
// placement math: world X = -k*U and world Z = -k*V (UV = -UVs above), so the
// map's normal axes land negated on both. If sunlit outland slopes shade on
// the wrong side, this is the knob - flip one sign at a time.
const vec2 NORM_SIGN = vec2(-1.0, -1.0);

layout(location = 0) in VS_OUT {
    vec3 viewPosition;
    vec2 UV;
} fs_in;

void main(void)
{
    // The mirror as 1-u (fs_in.UV is the negated uv, so 1+UV), half-texel
    // clamped - fract(-u) collapses the u=0 edge rows onto the opposite side
    // of the map. The detail sample keeps its wrap - it is meant to tile.
    vec2 base_uv = 1.0 + fs_in.UV;
    vec2 a_ts = vec2(textureSize(baked_albedo, 0));
    vec2 n_ts = vec2(textureSize(normal_map, 0));
    vec2 auv = clamp(base_uv, 0.5 / a_ts, 1.0 - 0.5 / a_ts);
    vec2 nuv = clamp(base_uv, 0.5 / n_ts, 1.0 - 0.5 / n_ts);

    // --- albedo + detail: the game's exact combine --------------------------
    vec4 alb = texture(baked_albedo, auv);
    vec4 det = texture(detail_albedo, fs_in.UV * detail_tiles);

    vec3 color = alb.rgb + det.a - 0.5;  // grayscale detail rides in det.a
    color = mix(color, det.rgb, alb.a);  // alb.a authors where detail rgb shows

    // --- normal: world-space AG decode (DXT5 pack: X in alpha, Z in green) --
    vec4 nm = texture(normal_map, nuv);

    // The compiled PBS_ext_outland variants alpha-test on the normal map's R
    // channel, but the material's alphaReference defaults to 0 - never cuts -
    // and lakeville's authored value is unknown. A guessed 0.5 threshold
    // punched sky holes through its outland (the walls it was meant to hide
    // were the fract(-u) mirror bug all along), so no cut until a real
    // reference value is recovered from the map data.
    vec3 N;
    N.xz = (nm.ag * 2.0 - 1.0) * NORM_SIGN;
    N.y  = sqrt(1.0 - min(dot(N.xz, N.xz), 1.0));
    N    = normalize(N);

    vec3 vN = normalize(mat3(view) * N);

    gColor   = vec4(color, 0.0);
    gNormal  = vN * 0.5 + 0.5;
    // In THIS deferred, gGMF.x shapes the specular lobe (terrain writes 0.2)
    // and .y is the specular intensity (field sand pages run ~0.1-0.2). The
    // game's raw (59,80)/255 through our BRDF reads shinier than the field,
    // so the lobe matches the terrain and the intensity is a live slider.
    // pbr_from_nm (R as shine, B as metal) stays as an experiment lever -
    // on Sand River R is the cutout mask (~0.91), which runs hot.
    gGMF     = (pbr_from_nm != 0)
             ? vec4(nm.b, nm.r, GFLAG_TERRAIN, 0.0)
             : vec4(0.2, outland_spec, GFLAG_TERRAIN, 0.0);
    gPosition = fs_in.viewPosition;
    gSurfaceNormals = vN * 0.5 + 0.5;
}
