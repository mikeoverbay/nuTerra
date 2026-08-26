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
    // --- albedo + detail: the game's exact combine --------------------------
    vec4 alb = texture(baked_albedo, fs_in.UV);
    vec4 det = texture(detail_albedo, fs_in.UV * detail_tiles);

    vec3 color = alb.rgb + det.a - 0.5;  // grayscale detail rides in det.a
    color = mix(color, det.rgb, alb.a);  // alb.a authors where detail rgb shows

    // --- normal: world-space AG decode (DXT5 pack: X in alpha, Z in green) --
    // The game's r channel here is an alpha-test mask (alphaReference defaults
    // to 0, so it never cuts) - available if edge cutouts are ever wanted.
    vec4 nm = texture(normal_map, fs_in.UV);
    vec3 N;
    N.xz = (nm.ag * 2.0 - 1.0) * NORM_SIGN;
    N.y  = sqrt(1.0 - min(dot(N.xz, N.xz), 1.0));
    N    = normalize(N);

    vec3 vN = normalize(mat3(view) * N);

    gColor   = vec4(color, 0.0);
    gNormal  = vN * 0.5 + 0.5;
    // The game's G-buffer write is exactly (59, 80, 0)/255 + material id.
    gGMF     = vec4(0.2314, 0.3137, GFLAG_TERRAIN, 0.0);
    gPosition = fs_in.viewPosition;
    gSurfaceNormals = vN * 0.5 + 0.5;
}
