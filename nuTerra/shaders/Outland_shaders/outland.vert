#version 450 core

#extension GL_ARB_shading_language_include : require

#define USE_PERVIEW_UBO
#include "common.h" //! #include "../common.h"

// The game's PBS_ext_outland VS samples nothing - it draws prebuilt verts and
// its PS ignores the mesh TBN entirely (the cascade normal map is decoded
// straight to world space). So this stays a heightmap-displaced grid and the
// old TBN plumbing is gone; attributes 2/3 in the VAO are simply unused now.

layout(location = 0) in vec2 vertexPosition;
layout(location = 1) in vec2 UVs;

layout(binding = 1) uniform sampler2D height_map;

uniform float y_range;
uniform float y_offset;

uniform vec2 scale;
uniform vec2 center_offset;

layout(location = 0) out VS_OUT {
    vec3 viewPosition;
    vec2 UV;
} vs_out;

void main(void)
{
    // Both axes mirrored (REPEAT wrap makes -uv equal 1-uv): this is the
    // orientation that places the authored maps in nuTerra's X-mirrored world.
    vec2 UV = -UVs;
    vs_out.UV = UV;

    vec3 pos;
    pos.xz = vertexPosition.xy * scale;
    pos.xz += center_offset;
    // The mirror written directly as 1-u, NOT as fract(-u): fract collapses
    // u=0 to 0 instead of 1, so the sheet's -X/-Z edge rows sampled the
    // OPPOSITE side of the heightmap (REPEAT even blended the two borders
    // 50/50) - a one-row vertical wall on exactly two edges, hundreds of
    // metres tall on alpine maps. 1-u is continuous; the half-texel clamp
    // keeps bilinear off the border texels.
    vec2 hts = vec2(textureSize(height_map, 0));
    vec2 huv = clamp(1.0 - UVs, 0.5 / hts, 1.0 - 0.5 / hts);
    // -1.5: sink the sheet slightly so playfield terrain always wins the depth
    // fight along the seam.
    pos.y = texture(height_map, huv).x * y_range + y_offset - 1.5;

    vs_out.viewPosition = vec3(view * vec4(pos, 1.0));
    gl_Position = viewProj * vec4(pos, 1.0);
}
