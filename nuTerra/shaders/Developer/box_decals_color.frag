#version 450 core

#extension GL_ARB_bindless_texture : require
#extension GL_ARB_shading_language_include : require

#define USE_PERVIEW_UBO
#include "common.h" //! #include "../common.h"

layout (location = 0) out vec4 gColor;
layout (location = 1) out vec4 gNormal;
// gGMF is ColorAttachment2 and TerrainHQ.frag:18 / model.frag:14 both bind it
// at location 2. This said 6 - the ATTACHMENT number, not the output location -
// so the write fell off the end of the draw buffer list and vanished.
layout (location = 2) out vec4 gGMF;

layout (binding = 0) uniform sampler2D depthMap;
layout (binding = 1) uniform sampler2D igGMF;

layout (binding = 2) uniform sampler2D normal_tex;
layout (binding = 3) uniform sampler2D color_tex;
layout (binding = 4) uniform sampler2D SurfaceNormal;

uniform vec2 offset;
uniform vec2 scale;
uniform uint influence;
uniform uint v1;
uniform uint v2;
uniform uint vis;
uniform uint wet;

// The decal's projection axis - decal-local +Z, the thickness axis named in the
// vert - rotated into VIEW space and normalized on the CPU. VIEW space because
// gSurfaceNormal is view space in every writer that fills it; TerrainHQ.frag:98
// spells that out. Local Z because the decal face is local XY and the UV below
// is built from XY only, so Z is the direction we project along. Uploaded as
// zero when the decal's transform is degenerate, which disables the gate.
uniform vec3 decal_axis;

// The decal's UV tangent - decal-local +X, the direction tuv.s increases along
// - rotated into VIEW space and normalized on the CPU, exactly like decal_axis.
// This exists because the tangent frame must NOT come from screen-space
// derivatives. get_tbn used to build it from dFdx/dFdy of a UV that is
// reconstructed from the DEPTH BUFFER, so the UV Jacobian it divides by
// collapses toward zero wherever the surface is near grazing - and the driver's
// fine derivatives differ between even and odd pixels, so the exploded tangent
// alternated pixel to pixel and painted a 1-pixel checkerboard over the ground.
// Measured on 101_dday: signed checker energy +0.540, gone (-0.002) once the
// frame stopped being derived.
// A decal's UV mapping is affine in its own box, so this direction is exact,
// constant across the decal, and free. Uploaded as zero when the transform is
// degenerate, which makes get_tbn fall back to an arbitrary perpendicular.
uniform vec3 decal_tangent;

// 1 when this decal's texture runs opaque content all the way to its border, so
// the box cuts it off square and we have to fade it ourselves. Textures painted
// with a transparent margin already fade and must be left alone or they lose
// their outer detail. Derived at load time by DecalEdgeProbe - space.bin has no
// flag for it because the artist encodes it in the pixels.
uniform uint edge_fade;

// How far in from the box edge the fade runs, in the decal's local units. Local
// XY spans -0.5..0.5, so this is 24% of the half extent, or 12% of the full box
// width. It scales with the decal: on Abbey that works out at roughly 0.24 to
// 1.7 metres, median 0.84 on a typical 7 m patch.
const float EDGE_FADE_WIDTH = 0.12;

// Grazing-angle rejection. c = |dot(surface normal, projection axis)| is the
// cosine of the angle between them, so the test is on that angle directly.
// Cutoff is 45 degrees, by the owner's call: a face more than 45 deg away from
// facing the decal does not receive it. 45 is the symmetric point - measured
// from the axis or from the face plane it is the same threshold - so there is
// no convention to get wrong. cos(45) = 0.7071 is the half-alpha point.
// Note this is NOT an anisotropy threshold. The UV stretches by 1/c, so 45 deg
// cuts at only 1.41x stretch, which the aniso sampler would resolve fine. It is
// a stronger, simpler rule than "where does it visibly smear": faces must face
// the decal. That is deliberate - the earlier 87 deg setting cleared the worst
// streaks but left the moderately-angled ones.
// Fade rather than discard: no MainFBO attachment is multisampled, so a hard
// cut is a binary per-pixel decision with no coverage to soften it, and the
// boundary sits exactly where the Rgb8 normal is noisiest. The 40-50 deg band
// is ~25x wider than that quantization jitter (~0.4 deg), so the edge reads
// smooth rather than speckled.
const float ANGLE_FADE_MIN = 0.642788;   // 50 deg off axis - fully rejected
const float ANGLE_FADE_MAX = 0.766044;   // 40 deg off axis - fully kept

in VS_OUT {
    flat mat4 invMVP;
} fs_in;

const vec3 tr = vec3 (0.5 ,0.5 , 0.5);
const vec3 bl = vec3(-0.5, -0.5, -0.5);

void clip(vec3 v) {
    if (v.x > tr.x || v.x < bl.x ) discard;
    if (v.y > tr.y || v.y < bl.y ) discard;
    if (v.z > tr.z || v.z < bl.z ) discard;
}

// Tangent frame for the decal's normal map. The tangent comes in from the CPU
// (see decal_tangent) rather than from screen-space derivatives - deriving it
// from a depth-reconstructed UV is what produced the checkerboard.
mat3 get_tbn (in vec3 v_Normal, in vec3 v_Tangent){
    vec3 ng = normalize(v_Normal);

    // Zero means the CPU found the decal transform degenerate. Any fixed
    // perpendicular will do: the normal map's rotation is then arbitrary, but it
    // is STABLE, which is the whole point - the derived frame was not.
    vec3 t = v_Tangent;
    if (dot(t, t) < 0.5) {
        t = abs(ng.z) < 0.9 ? vec3(0.0, 0.0, 1.0) : vec3(1.0, 0.0, 0.0);
    }

    // Gram-Schmidt against the REAL surface normal, so the frame lies in the
    // surface the decal landed on rather than in the decal's own plane.
    t = t - ng * dot(ng, t);
    float len2 = dot(t, t);
    if (len2 < 1e-8) {
        // Tangent parallel to the normal - the decal is projecting edge-on.
        t = abs(ng.z) < 0.9 ? cross(ng, vec3(0.0, 0.0, 1.0))
                            : cross(ng, vec3(1.0, 0.0, 0.0));
        len2 = max(dot(t, t), 1e-8);
    }
    t *= inversesqrt(len2);

    vec3 b = normalize(cross(ng, t));
    return mat3(t, b, ng);
}
vec3 getNormal( in vec2 UV1)
{
    vec3 normalBump;
    vec4 normal = texture(normal_tex,UV1);
    normalBump.xy = normal.ag * 2.0 - 1.0;
    float dp = min(dot(normalBump.xy, normalBump.xy),1.0);
    normalBump.z = clamp(sqrt(-dp+1.0),-1.0,1.0);
    normalBump = normalize(normalBump);
        //normalBump.x*=-1.0;
    return normalBump;
}

//        ' FLAG INFO
//        ' 0  = No shading
//        ' 64  = model 
//        ' 128 = terrain
//        ' 255 = sky dome. We will want to control brightness
//        ' more as they are added
void main()
{
    // Calculate UVs
    vec2 uv = gl_FragCoord.xy / resolution;

    // Grazing-angle rejection - the fix for decals smearing down faces they
    // only skim. gSurfaceNormal is VIEW space and Rgb8, so decode and then
    // RENORMALIZE: quantization leaves the length off unity, and only a unit
    // vector makes the dot product an actual cosine.
    vec3 normal = texture(SurfaceNormal, uv).xyz * 2.0 - 1.0;
    float n_len2 = dot(normal, normal);
    normal *= inversesqrt(max(n_len2, 1e-8));

    // A pixel no G-buffer writer touched holds the clear, which decodes to
    // (-1,-1,-1) and has length^2 = 3, not 1. Outland ground is the real case:
    // its normal output lands in gAUX_Color rather than here. Those fragments
    // get no gate at all rather than a wrong one, preserving old behaviour.
    float angle_alpha = 1.0;
    if (abs(n_len2 - 1.0) < 0.05 && dot(decal_axis, decal_axis) > 0.5) {
        // abs() because the axis points down INTO the surface on essentially
        // every ground decal while the view-space normal faces the camera, so
        // the raw dot is systematically negative - a signed test rejects
        // everything. The smear depends on |cos|, not on which side we are on.
        float c = abs(dot(normal, decal_axis));
        angle_alpha = smoothstep(ANGLE_FADE_MIN, ANGLE_FADE_MAX, c);
    }
    /*==================================================*/
    // A decal's influenceType is a bitmask over surface kinds, so the rule is
    // just a bit test. The game does the same thing by sampling a 256x8
    // "bitwise LUT" texture, which is only a precomputed bit extraction.
    // Kinds live in the low 3 bits of gGMF.b - see common.h.
    if ((influence & GBUF_KNOWN_KINDS) != 0u) {
        uint kind = GBUF_KIND(texture(igGMF, uv).b);
        if (((influence >> kind) & 1u) == 0u) discard;
    }
    /*==================================================*/
    // sample the Depth from the Depthsampler
    float depth = texture(depthMap, uv).x;

    // Calculate clip space by recreating it out of the coordinates and depth-sample
    vec4 ScreenPosition = vec4(uv*2.0-1.0, depth, 1.0);

    // Transform position from screen space to world space
    vec4 WorldPosition = fs_in.invMVP * ScreenPosition;
    vec4 WP = WorldPosition;
    WorldPosition.xyz /= WorldPosition.w;
    WorldPosition.w = 1.0f;
    // trasform to decal original and size.
    // 1 x 1 x 1
    clip (WorldPosition.xyz);

    // distance to the nearest side of the box, before the UV remap moves it:
    // 0 at the edge, 0.5 at the middle
    vec2 edge_distance = vec2(0.5) - abs(WorldPosition.xy);
    float edge_alpha = 1.0;
    if (edge_fade != 0u) {
        edge_alpha = clamp(min(edge_distance.x, edge_distance.y) / EDGE_FADE_WIDTH, 0.0, 1.0);
    }

    /*==================================================*/
   WorldPosition.xy += 0.5;
   WorldPosition.xy *= -1.0;
   vec2 tuv = WorldPosition.xy * scale + offset;
   vec4 color =  texture(color_tex, tuv);

   //Get texture UVs
   if (wet ==1) {
       gColor.a = color.r*0.8;
       gColor.rgb = vec3(0.0);
       gNormal.a = color.r;

       // A puddle is a flat mirror, so give it a flat mirror's normal.
       //
       // This branch used to leave gNormal.xyz UNWRITTEN while the pass left
       // buffer 1's RGB open, so a wet decal scribbled an undefined normal over
       // the ground it sat on. deferred.frag hid that - it does
       // N = mix(N, blank_n, water_mix), flattening toward up in proportion to
       // wetness, so the resolve never saw the garbage. ssr.frag has no such
       // step: it reads gNormal raw, and a wrong normal there sends the
       // reflected ray somewhere the puddle cannot see, or straight back at the
       // camera where SSR gives up (R.z > -0.02).
       //
       // World up in view space, which is the same vector deferred.frag builds
       // as blank_n. The view matrix is a LookAt and carries no scale, so the
       // plain upper-left 3x3 is the right rotation and the inverse-transpose
       // is unnecessary.
       gNormal.xyz = normalize(mat3(view) * vec3(0.0, 1.0, 0.0)) * 0.5 + 0.5;

       // The SSR wetness mask. ssr.frag samples gGMF.A and scales the whole
       // reflection by it, so a puddle that does not write this channel is
       // invisible to SSR no matter how wet the rest of the G-buffer says it
       // is. This used to write gGMF.r, which is GLOSS, into an attachment
       // the decal pass had not enabled - it could never have worked.
       //
       // Faded here rather than in the shared tail below because the else
       // branch never writes gGMF at all, and the pass masks the channel off
       // for those decals.
       gGMF.a = color.r * edge_alpha * angle_alpha;
       }
   else
   {
   mat3 TBN = get_tbn(normal, decal_tangent);

    gNormal.xyz = TBN * getNormal(tuv) *0.5 + 0.5;   

   gColor = color;

    gNormal.a = color.a;
    }

    gColor.a  *= edge_alpha * angle_alpha;
    gNormal.a *= edge_alpha * angle_alpha;
}

