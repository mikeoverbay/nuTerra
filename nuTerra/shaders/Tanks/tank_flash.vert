#version 450 core

#extension GL_ARB_shading_language_include : require

#define USE_PERVIEW_UBO
#include "common.h" //! #include "../common.h"

// The flame at the muzzle: three quads standing in the world, NOT billboards.
//
// THE ARTWORK IS A SIDE VIEW. gun_flash is a flame photographed side on -
// 256 wide by 128 tall, the plume running along the long axis - so it can only
// be hung on a surface that lies ALONG the barrel. Pointed at the camera it is
// a picture of a flame seen from the side pasted over a flame seen head on,
// which is a smear.
//
// So: three rectangles, each anchored at the muzzle, each running `length`
// along the gun and `thickness` across it, and each rolled 120 degrees about
// the barrel from the last. From any viewing angle at least one of the three
// is close to broadside, and the other two foreshorten into it - which is what
// gives a flat picture volume. Fixed in world space at the moment of firing;
// they do not turn to follow the camera.
//
//   a_uv.x  ->  along the barrel   (0 = muzzle, 1 = tip of the plume)
//   a_uv.y  ->  across it          (0 and 1 the edges, 0.5 the axis)
//
//   world = muzzle + fwd * (u.x * length) + up * ((u.y - 0.5) * thickness)
//
// which puts the pivot at the bottom-centre of the card, on the muzzle itself.

layout(location = 0) in vec4 a_pos_len;    // xyz muzzle, w length
layout(location = 1) in vec4 a_fwd_thick;  // xyz along barrel, w thickness
layout(location = 2) in vec4 a_up_alpha;   // xyz across, w alpha
layout(location = 3) in vec4 a_uv;         // frame rect: u0, v_bottom, u1, v_top

out vec2 vUV;
out float vAlpha;

void main(void)
{
    // (0,0) (1,0) (0,1) (1,1) - the strip's four corners.
    vec2 c = vec2(float(gl_VertexID & 1), float(gl_VertexID >> 1));

    vec3 world = a_pos_len.xyz
               + a_fwd_thick.xyz * (c.x * a_pos_len.w)
               + a_up_alpha.xyz  * ((c.y - 0.5) * a_fwd_thick.w);

    gl_Position = projection * view * vec4(world, 1.0);
    vUV = mix(a_uv.xy, a_uv.zw, c);
    vAlpha = a_up_alpha.w;
}
