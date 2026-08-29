#version 450 core

#extension GL_ARB_bindless_texture : require
#extension GL_ARB_shading_language_include : require

#define USE_PERVIEW_UBO
#define USE_COMMON_PROPERTIES_UBO
#include "common.h" //! #include "../common.h"

layout (location = 0) out vec4 outColor;

layout(binding = 0) uniform sampler2D gColor;
layout(binding = 1) uniform sampler2D gNormal;
layout(binding = 2) uniform sampler2D gGMF;
layout(binding = 3) uniform sampler2D gPosition;
layout(binding = 4) uniform samplerCube cubeMap;

// Diffuse ambient as L2 spherical harmonics, baked per map into
// environments/<env>/probes/global/rem_sh.xml. Nine RGB coefficients let ambient
// follow the surface normal - sky colour from above, warm bounce from below -
// instead of the single flat value this used to apply everywhere.
uniform vec3 sh_ambient[9];
uniform int  sh_enabled;

// Ramamoorthi & Hanrahan irradiance evaluation. The constants fold the SH basis
// together with the cosine convolution, so this returns irradiance directly and
// needs no further scaling.
vec3 eval_sh_irradiance(vec3 n)
{
    const float c1 = 0.429043, c2 = 0.511664, c3 = 0.743125;
    const float c4 = 0.886227, c5 = 0.247708;

    return c4 * sh_ambient[0]
         + 2.0 * c2 * (sh_ambient[1] * n.y + sh_ambient[2] * n.z + sh_ambient[3] * n.x)
         + 2.0 * c1 * (sh_ambient[4] * n.x * n.y
                     + sh_ambient[5] * n.y * n.z
                     + sh_ambient[7] * n.x * n.z)
         + c3 * sh_ambient[6] * n.z * n.z
         - c5 * sh_ambient[6]
         + c1 * sh_ambient[8] * (n.x * n.x - n.y * n.y);
}

// ---------------------------------------------------------------------------
// The baked probe FIELD - probes/sh_grid, an RGBA16F volume, 8 slices, one
// probe every 5 m. Where sh_ambient above is ONE probe stretched over the
// whole map, this varies with POSITION too, because the bake saw the buildings.
//
// A probe is 32 numbers, and they are stored a CHANNEL per slice:
//   slice 0,1,2 = red, green, blue  -> (constant, L.y, L.z, L.x)
//   slice 3,4,5 = red, green, blue  -> quadratic (xy, yz, zz, xz)
//   slice 6     = the (x^2-y^2) coefficient for r,g,b, and .w = REF HEIGHT (m)
//   slice 7     = not read by the game's resolve
//
// It replaces the global probe's irradiance and nothing else. With
// sh_grid_enabled at 0 not one instruction here runs.
// ---------------------------------------------------------------------------
layout(binding = 11) uniform sampler3D sh_grid;
uniform int   sh_grid_enabled;
uniform vec4  sh_grid_uv;     // xy = offset, zw = 1/size, the game's packing
uniform float sh_grid_fade;   // 1 / fade distance in metres
uniform float sh_grid_offset; // metres to push the lookup along the normal
uniform float sh_grid_edge;   // uv width of the ease-out at the box edge
uniform vec3  sh_grid_sh9[9]; // the FIELD's own companion probe, not the global

// How far to travel from the global probe toward the field. 1 is the field
// exactly; above 1 keeps going, exaggerating how far the field departs from
// the flat global probe. Not physical past 1, but this is a viewer.
uniform float sh_grid_mix;

// Separate from eval_sh_irradiance on purpose: the working path above is left
// byte for byte alone.
vec3 eval_sh_grid_fallback(vec3 n)
{
    const float c1 = 0.429043, c2 = 0.511664, c3 = 0.743125;
    const float c4 = 0.886227, c5 = 0.247708;

    return c4 * sh_grid_sh9[0]
         + 2.0 * c2 * (sh_grid_sh9[1] * n.y + sh_grid_sh9[2] * n.z + sh_grid_sh9[3] * n.x)
         + 2.0 * c1 * (sh_grid_sh9[4] * n.x * n.y
                     + sh_grid_sh9[5] * n.y * n.z
                     + sh_grid_sh9[7] * n.x * n.z)
         + c3 * sh_grid_sh9[6] * n.z * n.z
         - c5 * sh_grid_sh9[6]
         + c1 * sh_grid_sh9[8] * (n.x * n.x - n.y * n.y);
}

// Irradiance from the field, easing out to its companion probe when the point
// leaves the grid in XZ or climbs above the probe's reference height.
vec3 eval_sh_grid(vec3 world_pos, vec3 n)
{
    // The offset moves the LOOKUP only, never the height test below.
    //
    // Probes near geometry are much darker than open ones - measured on Abbey
    // the darkest sit around 0.03 against a grid mean of 0.77 - so a wall's
    // surface, which lands close to that boundary, can pull a dark neighbour
    // into its bilinear tap. Pushing the lookup outward biases the sample
    // toward open air. Only the horizontal part can move a lookup, so flat
    // ground (n.xz ~ 0) is unaffected and a wall takes the whole push.
    vec3 sample_pos = world_pos + n * sh_grid_offset;
    vec2 uv = sample_pos.xz * sh_grid_uv.zw - sh_grid_uv.xy;

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

    // Eased over a couple of probes, not switched: the grid stops well inside
    // the outland and a hard test draws a ring across the terrain there.
    vec2  edge    = min(uv, 1.0 - uv);
    float outside = 1.0 - smoothstep(0.0, sh_grid_edge, min(edge.x, edge.y));

    // c6.w is the probe's stored reference height in metres, not a coefficient.
    float height_fade = clamp((world_pos.y - c6.w) * sh_grid_fade, 0.0, 1.0);
    float blend = outside * (1.0 - height_fade) + height_fade;

    // The game's own pre-convolved packing - one dot per band.
    vec3 lin  = vec3(dot(c0.wyz, n), dot(c1.wyz, n), dot(c2.wyz, n));
    vec4 q    = vec4(n.y * n.x, n.z * n.y, n.z * n.z, n.x * n.z);
    vec3 quad = vec3(dot(c3, q), dot(c4, q), dot(c5, q))
              + c6.xyz * (n.x * n.x - n.y * n.y);
    vec3 local = max(vec3(c0.x, c1.x, c2.x) + lin + quad, vec3(0.0));

    // Both ends of the blend evaluated the same way, or the mix would step.
    return mix(local, max(eval_sh_grid_fallback(n), vec3(0.0)), blend);
}
layout(binding = 5) uniform lowp sampler2D lut;
layout(binding = 6) uniform lowp sampler2D env_brdf_lut;
layout(binding = 7) uniform sampler2DArrayShadow shadowMap;

// Map-wide baked sun shadow. Sampled here rather than folded into terrain albedo
// at VT bake time, for two reasons. Albedo is multiplied into the ambient term
// as well as the direct one, so baking it there darkened the sky fill that
// should still be present in shade. And the VT page only covers terrain, so a
// static model standing in a building's shadow received nothing at all - the
// cascades carry trees only, deliberately, because trees animate.
layout(binding = 8) uniform sampler2DShadow sun_shadow_map;

// Moment Shadow Map variant of the same bake - four power moments instead of a
// comparison sampler. Plain sampler2D, mipmapped and pre-blurred.
layout(binding = 9) uniform sampler2D sun_moment_map;

uniform mat4 sunViewProj;

// 0 = no baked shadow, 1 = PCF over the depth map, 2 = moment shadow map.
uniform int has_sun_shadow;

uniform float msm_moment_bias;

// Penumbra shaping, applied to both the PCF and the moment path so an A/B
// between them stays honest.
uniform float shadow_penumbra_lo;
uniform float shadow_penumbra_hi;

uniform mat4 ProjectionMatrix;
uniform vec3 LightPos;

#define MAXCOLOR 15.0
#define COLORS 16.0
#define WIDTH 256.0
#define HEIGHT 16.0

/*========================== FUNCTIONS =============================*/
// This helps to even out overall levels of brightness and adjusts gamma.
vec4 correct(in vec4 hdrColor, in float exposure, in float gamma_level){
    // Exposure tone mapping
    vec3 mapped = vec3(1.0) - exp(-hdrColor.rgb * exposure);
    // Gamma correction
    mapped.rgb = pow(mapped.rgb, vec3(1.0 / gamma_level));
    mapped.rgb = pow(mapped.rgb, vec3(1.0 / props.GAMMA_LEVEL*0.5));
    return vec4 (mapped, hdrColor.a);
}

 // https://defold.com/tutorials/grading/
 vec4 lut_color_correction(in vec4 px)
 {
    float cell = px.b * MAXCOLOR;

    float cell_l = floor(cell);
    float cell_h = ceil(cell);

    float half_px_x = 0.5 / WIDTH;
    float half_px_y = 0.5 / HEIGHT;
    float r_offset = half_px_x + px.r / COLORS * (MAXCOLOR / COLORS);
    float g_offset = half_px_y + px.g * (MAXCOLOR / COLORS);

    vec2 lut_pos_l = vec2(cell_l / COLORS + r_offset, g_offset);
    vec2 lut_pos_h = vec2(cell_h / COLORS + r_offset, g_offset);

    vec4 graded_color_l = textureLod(lut, lut_pos_l, 0);
    vec4 graded_color_h = textureLod(lut, lut_pos_h, 0);

    vec4 graded_color = mix(graded_color_l, graded_color_h, fract(cell));

    return graded_color;

 }
/*===================================================================*/
#define MANUAL_SRGB ;
vec4 SRGBtoLINEAR(vec4 srgbIn)
{
    #ifdef MANUAL_SRGB
    #ifdef SRGB_FAST_APPROXIMATION
    vec3 linOut = pow(srgbIn.xyz,vec3(2.2));
    #else //SRGB_FAST_APPROXIMATION
    vec3 bLess = step(vec3(0.04045),srgbIn.xyz);
    vec3 linOut = mix( srgbIn.xyz/vec3(12.92), pow((srgbIn.xyz+vec3(0.055))/vec3(1.055),vec3(2.4)), bLess );
    #endif //SRGB_FAST_APPROXIMATION
    return vec4(linOut,srgbIn.w);
    ;
    #else //MANUAL_SRGB
    return srgbIn;
    #endif //MANUAL_SRGB
}


// Fraction of the sun reaching this point: 1.0 lit, 0.0 fully shadowed.
// This used to sit at the end of main and darken the finished pixel, which also
// darkened the ambient - the one thing that should still be there in shade.
// Shadow belongs on the direct light only, so it is a factor now, not a filter.
float sun_shadow_factor(vec3 view_pos)
{
    if (!props.use_shadow_mapping) {
        return 1.0;
    }

    float depthValue = abs(view_pos.z);

    int layer = -1;
    for (int i = 0; i < cascadeCount; ++i)
    {
        if (depthValue < cascadePlaneDistances[i])
        {
            layer = i;
            break;
        }
    }
    if (layer == -1)
    {
        layer = cascadeCount;
    }

    vec4 coords = lightSpaceMatrices[layer] * (invView * vec4(view_pos, 1.0));
    coords.xy *= vec2(0.5);
    coords.xy += vec2(0.5);
    if (coords.z >= 1.0 || coords.z <= 0.0) {
        return 1.0;
    }
    coords.w = layer;
    coords = coords.xywz;

    // Every cascade shares one map but covers a very different slice of the
    // world, so a fixed one-texel 3x3 gives a hard edge up close and a mush far
    // away. Widening the kernel on the near cascades spends the taps where the
    // texels are small enough to be worth filtering.
    const float kernel[4] = float[4](2.5, 1.5, 1.0, 1.0);
    float spread = kernel[layer] / float(textureSize(shadowMap, 0).x);

    // Rotate the tap pattern per pixel, or the 3x3 leaves a visible square
    // grain along every shadow edge.
    float a = fract(sin(dot(gl_FragCoord.xy, vec2(12.9898, 78.233))) * 43758.5453) * 6.2831853;
    vec2 rc = vec2(cos(a), sin(a));

    float shadowDepth = 0.0;
    for (int oy = -1; oy <= 1; ++oy)
    for (int ox = -1; ox <= 1; ++ox)
    {
        vec2 o = vec2(ox, oy);
        o = vec2(o.x * rc.x - o.y * rc.y, o.x * rc.y + o.y * rc.x);
        shadowDepth += texture(shadowMap, vec4(coords.xy + o * spread, coords.z, coords.w));
    }
    return mix(1.0, shadowDepth / 9.0, props.shadow_strength);
}

// Fraction of the sun reaching this point according to the map-wide bake.
// Same contract as sun_shadow_factor: 1.0 lit, 0.0 fully shadowed, so the two
// simply multiply.
//
// Takes world space. Note gPosition is view space despite every writer calling
// the varying "worldPosition" - it is view * model * vertex - so callers must
// pass invView * Position, exactly as sun_shadow_factor does above.
// Moment Shadow Maps, Peters & Klein 2015.
//
// Given the first four power moments of the occluder depth distribution in a
// filter footprint, this solves the Hamburger moment problem for the tightest
// bound on how much light reaches frag_z. Returns shadow INTENSITY: 0 lit,
// 1 fully shadowed.
//
// The value of the method is not the reconstruction, it is that moments are
// linear. A depth comparison must be compared before it is averaged, so a depth
// map cannot be blurred or mipmapped and PCF must spend taps every frame. These
// can be, and were - once, at bake time - so sampling is a single trilinear
// fetch and minification finally has a correct answer instead of aliasing.
float msm_intensity(vec4 b_in, float frag_z)
{
    // Bias toward the moments of a uniform distribution on 0..1. The Hankel
    // matrix below is singular wherever depth is constant, which is most of a
    // flat map, so without this the solve blows up across open ground.
    const vec4 UNIFORM_MOMENTS = vec4(0.5, 1.0 / 3.0, 0.25, 0.2);
    vec4 b = mix(b_in, UNIFORM_MOMENTS, msm_moment_bias);

    // B = [[1, b1, b2],
    //      [b1, b2, b3],
    //      [b2, b3, b4]]   solved by LDL^T, then B*c = (1, z, z^2).
    float L21 = b.x;
    float L31 = b.y;
    float D22 = max(b.y - b.x * b.x, 1e-7);
    float L32 = (b.z - b.x * b.y) / D22;
    float D33 = max(b.w - b.y * b.y - L32 * L32 * D22, 1e-7);

    // Forward substitution, L y = (1, z, z^2)
    float y2 = frag_z - L21;
    float y3 = frag_z * frag_z - L31 - L32 * y2;

    // Diagonal, then back substitution L^T c = D^-1 y
    float c3 = y3 / D33;
    float c2 = (y2 / D22) - L32 * c3;
    float c1 = 1.0 - L21 * c2 - L31 * c3;

    // Roots of c1 + c2*z + c3*z^2, the two support points of the distribution.
    float p = c2 / c3;
    float q = c1 / c3;
    float r = sqrt(max(p * p * 0.25 - q, 0.0));
    float z2 = -p * 0.5 - r;
    float z3 = -p * 0.5 + r;

    vec4 sw = (z3 < frag_z) ? vec4(z2, frag_z, 1.0, 1.0)
            : ((z2 < frag_z) ? vec4(frag_z, z2, 0.0, 1.0)
                             : vec4(0.0, 0.0, 0.0, 0.0));

    float denom = (z3 - sw.y) * (frag_z - z2);
    float quotient = (sw.x * z3 - b.x * (sw.x + z3) + b.y) / (abs(denom) < 1e-7 ? 1e-7 : denom);

    return clamp(sw.z + sw.w * quotient, 0.0, 1.0);
}

// Reshapes the transition after filtering and before the light sees it.
//
// Filtering decides how far a penumbra reaches; this decides how much of that
// reach is visible. That separation is the useful part - the footprint can stay
// wide, which is what makes minification and antialiasing behave, while the
// edge presents as tight.
//
// smoothstep, deliberately, not step. A hard cut throws away the sub-pixel
// gradient the blur and the mip chain just produced and puts the aliasing
// straight back. Narrow the band to sharpen; never close it.
//
// It doubles as the light-leak control on the moment path: raising lo crushes
// the grey haze that a filterable shadow map leaves in front of an occluder.
float shape_penumbra(float lit)
{
    float lo = shadow_penumbra_lo;
    float hi = max(shadow_penumbra_hi, lo + 1e-3);
    return smoothstep(lo, hi, lit);
}

float baked_sun_shadow(vec3 world_pos)
{
    if (has_sun_shadow == 0) {
        return 1.0;
    }

    vec4 sp = sunViewProj * vec4(world_pos, 1.0);
    sp.xyz /= sp.w;

    // Only xy need the -1..1 -> 0..1 remap. ClipDepthMode.ZeroToOne means z
    // already arrives as 0..1 and sun_view_proj is built to match; remapping it
    // again would halve it and shadow everything.
    sp.xy = sp.xy * 0.5 + 0.5;

    // Outside the baked depth range: unshadowed rather than clamped, or the far
    // side of the map would go black.
    if (sp.z > 1.0 || sp.z < 0.0) {
        return 1.0;
    }

    // Outside the baked footprint entirely - the ortho box is fitted to the
    // terrain, so anything past it is lit. The depth path gets this from
    // CLAMP_TO_BORDER with a white border; the moment path has no equivalent,
    // so it is checked here.
    if (any(lessThan(sp.xy, vec2(0.0))) || any(greaterThan(sp.xy, vec2(1.0)))) {
        return 1.0;
    }

    if (has_sun_shadow == 2) {
        // One trilinear fetch. The mip chain does the minification filtering
        // that PCF cannot do at any tap count.
        vec4 b = texture(sun_moment_map, sp.xy);
        float lit = shape_penumbra(1.0 - msm_intensity(b, sp.z));
        return mix(1.0, lit, props.horizon_strength);
    }

    // 4 taps a texel apart. The bake is coarse relative to a screen pixel, so a
    // little filtering here is most of the softness available.
    float texel = 1.0 / float(textureSize(sun_shadow_map, 0).x);
    float s = 0.0;
    s += texture(sun_shadow_map, vec3(sp.xy + vec2(-texel, -texel), sp.z));
    s += texture(sun_shadow_map, vec3(sp.xy + vec2( texel, -texel), sp.z));
    s += texture(sun_shadow_map, vec3(sp.xy + vec2(-texel,  texel), sp.z));
    s += texture(sun_shadow_map, vec3(sp.xy + vec2( texel,  texel), sp.z));

    // Same shaping as the moment path, so switching between the two compares
    // the filtering and nothing else.
    return mix(1.0, shape_penumbra(s * 0.25), props.horizon_strength);
}

void main (void)
{
    const uint FLAG = uint(texelFetch(gGMF, ivec2(gl_FragCoord), 0).b * 255.0);

    // Writen as a float in shaders as f = Flag_value/255.0
    // or just 0.0 to mask any shading.
    //
    // If the render bits are 0 we want NO shading done to the color.
    // Models = 64, terrain = 128, dome = 255. The low 3 bits are the decal
    // surface kind and must be masked off before comparing - see common.h.
    // Just output gColor to outColor;
    if ((FLAG & GBUF_RENDER_MASK) != 0u) {
        // FLAG VALUES WILL BE DECIDED AS WE NEED THEM BUT..
        // ZERO = JUST PASS THE COLOR TO OUTPUT
        if ((FLAG & GBUF_RENDER_MASK) != GBUF_RENDER_MASK) {
            vec3 Position = texelFetch(gPosition, ivec2(gl_FragCoord), 0).xyz;

            vec4 color_in = texelFetch(gColor, ivec2(gl_FragCoord), 0);

            //Mix in our water color
            //color_in.rgb = mix(color_in.rgb, waterColor, color_in.a);

            //fog level... this should be on the controller
            float fog_alpha = 0.5;

            vec3 GM_in = texelFetch(gGMF, ivec2(gl_FragCoord), 0).xya;

            //water overides GM values
            GM_in.rg = mix(GM_in.rg,vec2(0.4,0.8), color_in.a);

            vec3 LightPosModelView = LightPos.xyz;

            vec3 L = normalize(LightPosModelView-Position.xyz); // light direction

            vec3 N = normalize(texelFetch(gNormal, ivec2(gl_FragCoord), 0).xyz * 2.0 - 1.0); // convert to -1.0 to 1.0

            // Assigned inside the (FLAG & 192u) block below, which the glow
            // flag does not enter - and multiplying an undefined value by
            // zero still leaves NaN. Safe defaults, overwritten for every
            // flag that does enter.
            float POWER = 3.0;
            float INTENSITY = 0.0;

            // glow.fx: an emissive card. It carries its own light, so neither
            // the sun nor the ambient applies - but fog and the tonemapper
            // still do, which is why it is not the GFLAG_UNLIT passthrough.
            const bool is_glow = (FLAG & GBUF_RENDER_MASK) == GBUF_RENDER_GLOW;

            float metal = GM_in.r;

            if ((FLAG & 192u) != 0u) {
                //---------------------------------------------
                // Poor mans PBR :)
                // how shinny this is
                POWER = max(GM_in.r * 30.0, 3.0);
                INTENSITY = GM_in.g;

                // Wet is smooth, and smooth means a tight highlight.
                //
                // Terrain writes gGMF.r = 0.2, so dry ground runs POWER = 6 -
                // an extremely broad lobe, which is right for rough dirt and
                // completely wrong for standing water. Left at 6 a wet patch
                // reads as a large soft wash instead of a sharp glint, because
                // the lobe is wider than the puddle.
                //
                // GM_in.z is the wetness mask the terrain shaders now write.
                // WET_POWER is the tuning knob: higher is sharper and smaller.
                const float WET_POWER = 96.0;
                POWER = mix(POWER, WET_POWER, clamp(GM_in.z, 0.0, 1.0));
                // How metalic his is
                color_in.rgb = mix(color_in.rgb,
                                   color_in.rgb * vec3(0.04), max( metal * 0.25 , 0.00) );
                //---------------------------------------------

            }
            // Ambient. The flat form applies one value to every surface no matter
            // which way it faces, so nothing in shade has any form. With a probe
            // loaded, evaluate the SH against the normal instead and keep
            // props.AMBIENT as the overall strength control.
            vec4 Ambient_level;
            if (sh_enabled != 0) {
                // The probe is baked in world space but N is in view space -
                // normalMatrix is built from modelView, not model. Evaluating
                // one against the other rotates the whole ambient environment
                // with the camera, so the sky's blue lands on whatever happens
                // to face up on screen and a wall never keeps a stable colour.
                vec3 N_world = normalize(mat3(invView) * N);
                vec3 irradiance = max(eval_sh_irradiance(N_world), vec3(0.0));

                // The field answers the same question with position as well as
                // normal, so where it exists it replaces the single global
                // probe. This is the ONLY place it touches the lighting -
                // everything below is unchanged, and with the grid off the
                // branch never runs.
                if (sh_grid_enabled != 0) {
                    vec3 wp = (invView * vec4(Position, 1.0)).xyz;
                    vec3 grid_irr = max(eval_sh_grid(wp, N_world), vec3(0.0));
                    irradiance = max(mix(irradiance, grid_irr, sh_grid_mix), vec3(0.0));
                }

                // Desaturate toward the probe's own luminance. The bake is
                // genuinely this blue - sky fill is what lights a shadow - but
                // it reads stronger here than in the reference, so this pulls
                // the hue out without touching the level or the direction.
                float amb_lum = dot(irradiance, vec3(0.299, 0.587, 0.114));
                irradiance = mix(vec3(amb_lum), irradiance, props.ambient_sat);
                Ambient_level = vec4(color_in.rgb * irradiance * props.AMBIENT, color_in.a);
            } else {
                // the probe already carries the environment's colour, so applying
                // the forward tint on top of it would double up the warmth
                Ambient_level = color_in * vec4(props.AMBIENT * 3.0);
                Ambient_level.rgb *= props.ambientColorForward;
            }

            // Ambient fills in wherever direct light does not arrive, and there
            // are two independent ways for it not to arrive: the face is turned
            // away from the sun, or something is standing between it and the
            // sun. Both have to be known before ambient is weighted. Weighting
            // on facing alone left a sunward wall inside a building's shadow
            // with no ambient and no sun - black.
            //
            //   direct  = how much sun actually lands here
            //   ambient = the rest
            //
            // N and L are both view space, so the dot is consistent. This uses
            // the raw N.L, not the pow'd lambertTerm - the exponent is material
            // shaping and has no business deciding how much sky fill lands.
            // Live cascades (trees) and the map-wide bake (terrain and static
            // models) are two halves of one answer, so they multiply into a
            // single factor. Everything downstream then sees one number and the
            // ambient/direct split below stays consistent for both.
            float sun_shadow = sun_shadow_factor(Position)
                             * baked_sun_shadow((invView * vec4(Position, 1.0)).xyz);
            float direct_light = max(dot(N, L), 0.0) * sun_shadow;

            if (is_glow) {
                // The card IS the light. sun_shadow 0 also mutes every sun
                // term downstream - diffuse, specular and the wet reflections.
                Ambient_level = vec4(color_in.rgb, color_in.a);
                sun_shadow = 0.0;
                direct_light = 0.0;
            } else {
                Ambient_level.rgb *= (1.0 - direct_light);
            }

            // Ambient is the base the sun adds on top of. This used to start from
            // a hardcoded 0.25 grey with Ambient_level only reaching the output
            // through the distance mix below - at 200 m that is a 2% blend, which
            // is why the ambient slider appeared to do nothing.
            vec4 final_color = (sh_enabled != 0) ? Ambient_level
                                                 : vec4(0.25, 0.25, 0.25, 1.0) * color_in;

            float dist = length(LightPosModelView - Position);
            float cutoff = 10000.0;
            // sunLightColor tints the direct light only - ambient gets its
            // colour from the SH probe instead. Sun Tint blends between a white
            // sun and the map's full-chroma value; Sun Strength is the level.
            vec3 sun_rgb = mix(vec3(1.0), props.sunColor, props.sun_tint);
            vec4 color = vec4(sun_rgb * props.sun_strength, 0.0);

            vec4 t_cam = view * vec4(cameraPos,1.0);
            vec3 V = normalize(t_cam.xyz-Position);

            float perceptualRoughness = 0.2;

            //create a up facing normal that translates properly.
            vec3 blank_n = mat3(inverse(transpose(view))) * normalize(vec3(0.0, 1.0, 0.0));

            float water_mix = color_in.a;

            // Only light whats in range
            if (dist < cutoff) {
                // kill the terrian normals where there is water
                N = mix(N, blank_n, water_mix);

                // Two reflect vectors, because they answer different questions.
                //
                // R is the mirror of the LIGHT, which is what a Phong lobe
                // wants: pow(dot(V,R), n) is the highlight of the sun in this
                // surface. R_env is the mirror of the VIEW, which is what an
                // environment lookup wants: what the surface shows you depends
                // on where you are standing.
                //
                // Both cubemap lookups used R. That pinned the reflection to
                // the sun - near enough the same direction for every pixel, so
                // it returned the same patch of sky everywhere and did not
                // move with the camera. It read as a sheen rather than a
                // reflection, and no amount of tuning was going to fix it.
                vec3 R = reflect(-L,N);
                vec3 R_env = reflect(-V,N);

                // Plain Lambert. GM_in.g used to be the exponent here, but it is
                // gGMF.y - metal for models, the specular sample for terrain -
                // and never a diffuse term. Trees write metal 0, so pow(NdotL, 0)
                // was 1.0 at every angle facing the light and 0.0 the instant it
                // did not: a step function, which is the hard edge round surfaces
                // like trunks were showing. N.L falls off smoothly across the
                // curve and still reaches zero exactly at the terminator.
                float NdotL = max(dot(N, L), 0.0);
                float lambertTerm = NdotL;

                float water_spec = max(pow(dot(V,R), 120.0 ),0.0001) * props.SPECULAR;

                // sun_shadow was computed above, before ambient was weighted -
                // the two have to agree on how much sun arrives here.
                final_color.xyz += max(lambertTerm * color_in.xyz * color.xyz ,0.0) * sun_shadow;



                vec3 halfwayDir = normalize(L + V);

                float spec = max(pow(dot(V,R), POWER ),0.0000) * props.SPECULAR * INTENSITY;

                // Cubemap handedness - the world is mirrored in x for display.
                R_env.xz *= -1.0;

                vec4 brdf = SRGBtoLINEAR( texture2D( env_brdf_lut,
                            vec2(1.0-lambertTerm * 0.25, 1.0-metal) ));
                vec3 specular =  (vec3(spec) * brdf.x + brdf.y);


                vec4 prefilteredColor = SRGBtoLINEAR(textureLod(cubeMap, R_env,
                                        max(8.0-GM_in.g *4.0, 0.0)));
                // GM_in.b is the alpha channel.
                prefilteredColor.rgb = mix(vec3(specular), prefilteredColor.rgb +
                                       specular, GM_in.b*0.2*(1.0-color_in.a));

                vec4 W_prefilteredColor = SRGBtoLINEAR(textureLod(cubeMap, R_env,
                                          max(8.0-water_mix *5.0, 0.0)));

                // Wetness reflection. The mip drops toward 0 as wetness rises,
                // so a wet surface samples the sharp end of the cubemap.
                //
                // WET_REFLECT was 4.0, tuned back when terrain wrote GM_in.z as
                // a hard zero and this term could never fire at all. With a real
                // mask behind it that was too hot.
                const float WET_REFLECT = 3.0;
                vec4 G_prefilteredColor = SRGBtoLINEAR(textureLod(cubeMap, R_env,
                                          max(3.0-GM_in.z *3.0, 0.0)))*GM_in.z*spec*WET_REFLECT;

                vec3 water_reflect = vec3(water_mix*props.ambientColorForward) * vec3(water_spec)*1.5 * W_prefilteredColor.rgb;

                // Nothing reflects the sun out of the sun's reach.
                //
                // All three of these are sun terms, whatever their names say.
                // R is reflect(-L, N) - the mirror of the LIGHT - so spec and
                // water_spec are both the sun's own highlight, and the cubemap
                // lookups they scale are only standing in for the sky around
                // it. A puddle inside a building's shadow was still throwing a
                // hard glint, because only `specular` was ever gated.
                //
                // Gate all three on the same factor the diffuse uses, so a wet
                // surface in shade goes quiet instead of picking out a sun that
                // cannot see it. What SHOULD survive in shadow is a reflection
                // of the geometry around it - that needs real reflected colour,
                // not a sun lobe, and there is none to sample yet.
                // Soft knee instead of a hard clamp. At the mirror angle the
                // wet terms peak together - specular plus the cube reflection
                // that is scaled BY specular - and their sum sails past 1.0.
                // clamp() turned that overshoot into a flat white patch, which
                // is exactly the saturation wet ground and track decals were
                // showing. 1-exp(-x) is linear where the response was already
                // sane and rolls off the peaks, so a hot glint stays bright
                // without ever clipping to paper.
                vec3 sun_add = (water_reflect + specular + G_prefilteredColor.xyz) * sun_shadow;
                final_color.xyz += 1.0 - exp(-sun_add);
                //final_color.xyz += spec;
                // Fade to ambient over distance

                // Ambient is already the base final_color started from, so the
                // old mix toward it here just re-applied 2% of the same value at
                // any normal view distance. BRIGHTNESS stays: it belongs before
                // tone mapping, where it acts as exposure gain.
                final_color = final_color * props.BRIGHTNESS;
                final_color = lut_color_correction( final_color );

            } else {
                final_color = Ambient_level * props.BRIGHTNESS;
            }
            //final_color.r = color_in.a;
            /*===================================================================*/
            /*===================================================================*/
            // Gray level
            vec3 luma = vec3(0.299, 0.587, 0.114);
            vec3 co = vec3(dot(luma, final_color.rgb));
            vec3 c = mix(co, final_color.rgb, props.GRAY_LEVEL);
            final_color.rgb = c;
            /*===================================================================*/

            // FOG calculation... using distance from camera and height on map.
            // It's a more natural height based fog than plastering the screen with it.
            vec4 ts_cam = view * vec4(cameraPos,1.0);
            vec4 p = invView * vec4(Position.xyz,1.0);
            float viewDistance = length(ts_cam.xyz - Position);
            float z = viewDistance*0.75 ;

            float height = 0.0;

            if( p.y <= props.MEAN ){

            height = 1.0-(p.y + -props.mapMinHeight) / (-props.mapMinHeight + props.MEAN);
            height = sin(1.5708*height); // change to a curve to improve depth.
            }

            const float LOG2 = 1.442695;


            //if (flag ==160) {z*=0.75;}//cut fog level down if this is water.
            float fog_density = 0.005;

            float density = (fog_density * height ) * 0.75;
            float fogFactor = exp2(-density * density * z * z * LOG2);
            fogFactor = clamp(fogFactor, 0.0, 1.0);

            vec4 f_color =  vec4(props.fog_tint,0.0) * 1.5 * fog_alpha;


            final_color = mix(final_color, f_color,(1.0- fogFactor)*props.fog_level);
            //final_color.r = outColor.a;
            /*===================================================================*/

            /*===================================================================*/
            // Final Output
            // correct() is a saturating curve - 1 - exp(-x * exposure) can never
            // exceed 1 - so multiplying its result by 1.6 afterwards clipped
            // everything above about 0.7 input to flat white. On a lit surface
            // the sun term alone lands there, which is why the ambient and
            // brightness sliders looked dead. Folding that gain into the
            // exposure instead keeps the midtones where they were and lets the
            // highlights roll off with detail still in them.
            outColor = correct(final_color, props.tonemap_exposure, 1.2);
            // Shadowing happens up in the lighting, on the sun term only -
            // see sun_shadow_factor(). It used to darken the finished pixel here,
            // which dimmed the ambient along with it.

            //outColor.a = fogFactor;
            /*===================================================================*/
        //if flag != 128
        }else{
            outColor = texelFetch(gColor, ivec2(gl_FragCoord), 0) * props.BRIGHTNESS;
        }
    // if flag != 0
    } else {
        outColor = texelFetch(gColor, ivec2(gl_FragCoord), 0) * props.BRIGHTNESS;
    }

    //outColor.a = 1.0;
}
