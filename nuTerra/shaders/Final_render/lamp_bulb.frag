#version 450 core

#extension GL_ARB_shading_language_include : require

#define USE_PERVIEW_UBO
#include "common.h" //! #include "../common.h"

// Emits premultiplied colour with ZERO coverage. Under the FX buffer's
// One / OneMinusSrcAlpha that reduces to dst + src, so the bulb ADDS light and
// attenuates nothing behind it - the same contract the volumetric meshes use
// (docs/FX_PIPELINE.md, "Cards first, meshes second"). The alpha channel there
// is accumulated coverage, and a light source has none: it is not a surface.

// The scene depth, for the occlusion test below. The pass itself runs with the
// depth test OFF - see the test for why.
layout(binding = 0) uniform sampler2D depthMap;

// The game's own lens glare - particles/content_deferred/PFX_textures/
// glow_star.dds, 256x256 BC7. Greyscale: a hot core, six radial spikes and
// a long ANAMORPHIC horizontal streak, with a soft halo behind. Because the
// streak is horizontal the sprite must stay screen-axis aligned and must
// never be rotated per lamp, or it stops reading as a lens.
layout(binding = 1) uniform sampler2D starMap;

in vec2 vOffset;
in float vDim;      // 1 at true size, lower where the pixel floor inflated it
in vec4 vCentre;    // clip-space centre of the bulb
in float vDepth;    // that centre's own 0..1 depth, for the core's z test
in float vThresh;   // window depth of a point see_thru metres in front of it

uniform vec3 bulb_color;   // sRGB, as the picker holds it
uniform float bulb_gain;
uniform float glare_ext;    // quad radius / core radius
uniform float halo_gain;    // the wide soft surround, relative to the core
uniform float spike_gain;   // the cross, relative to the core
uniform float spike_sharp;  // higher is thinner arms

out vec4 fragColor;

// How much of the bulb the scene lets through, 0..1.
//
// This is done HERE, per sprite, rather than by letting the hardware depth test
// do it per pixel. Per pixel is what produced donuts: the bulb sits inside its
// fixture, the housing in front of it is nearer, so the test cut the middle out
// of the disc and left the rim that overhangs the silhouette. Testing one point
// against a threshold set slightly in FRONT of the bulb lets it shine through
// its own glass and hood while a wall still stops it.
//
// Five taps rather than one so a bulb passing behind a pole fades over a few
// pixels instead of snapping off.
float visibility()
{
    if (vCentre.w <= 0.0) return 0.0;

    vec3 ndc = vCentre.xyz / vCentre.w;
    if (any(greaterThan(abs(ndc.xy), vec2(1.0)))) return 0.0;

    vec2 uv = ndc.xy * 0.5 + 0.5;
    vec2 px = 3.0 / resolution;

    // REVERSED Z: the engine clears depth to 0 and tests Greater, so NEARER is
    // a LARGER value. The bulb is visible when nothing is nearer than the
    // threshold - that is scene <= vThresh, which is step(scene, vThresh).
    // Written the other way round it meant "visible only when something is in
    // front of it", so the bulb showed through its own glass and vanished the
    // moment that glass was cut away.
    float vis = 0.0;
    vis += step(texture(depthMap, uv).r, vThresh);
    vis += step(texture(depthMap, uv + vec2( px.x, 0.0)).r, vThresh);
    vis += step(texture(depthMap, uv - vec2( px.x, 0.0)).r, vThresh);
    vis += step(texture(depthMap, uv + vec2( 0.0, px.y)).r, vThresh);
    vis += step(texture(depthMap, uv - vec2( 0.0, px.y)).r, vThresh);
    return vis * 0.2;
}

void main(void)
{
    float d = length(vOffset);
    if (d >= 1.0) discard;

    float vis = visibility();
    if (vis <= 0.0) discard;

    // The core, in its own small disc at the middle of the quad. Cubed falloff
    // reads as a bulb rather than a sticker: nearly all the energy sits in the
    // middle few pixels, which is both what a filament looks like and what the
    // bright pass wants to find - a flat disc of the same total energy blooms
    // as a ring.
    float dc = min(d * max(glare_ext, 1.0), 1.0);
    float fc = 1.0 - dc;
    float core = fc * fc * fc;

    // The core is a BALL sitting at the bulb, so it is depth tested per pixel
    // against its own depth - a wall in front of it hides it exactly, edge and
    // all. The glare is deliberately NOT: a lens flare is made in the lens, not
    // at the bulb, so it keeps the soft five-tap sprite test and survives being
    // partly clipped. vDepth is reversed Z like the buffer, so "nothing nearer"
    // is scene <= mine.
    float scene_here = texelFetch(depthMap, ivec2(gl_FragCoord.xy), 0).r;
    core *= step(scene_here, vDepth);

    // The glare, from the authored texture rather than built from cosines. It
    // already carries the halo, the spikes and the streak in one greyscale
    // image, so the two procedural terms it replaces are gone.
    //
    // RGB * A is how it composites: the file keeps the shape in BOTH, and this
    // pass is additive with alpha 0, so the alpha has to be folded into the
    // colour here rather than left to the blender.
    vec2 suv = vOffset * 0.5 + 0.5;
    vec4 st = texture(starMap, suv);
    float glare = st.r * st.a * spike_gain;

    // Fades with DISTANCE, not just with size. vDim is 1 at true size and drops
    // once the pixel floor starts inflating a far bulb, so the lamps down the
    // street stay plain blobs and only the near ones flare - which is what the
    // reference does. Squared, so the glare is the first thing to go.
    glare *= vDim * vDim;

    // Tinted by the lamp's own colour, like every other term that comes off a
    // light here. It was briefly left white while the glare sprite was being
    // looked at on its own, and white is what a distant lamp then became: the
    // min_px floor holds the core at ten pixels however far away it is, so once
    // the pane has shrunk past it the untinted ball is the whole lamp.
    //
    // sRGB as the picker holds it, linearised - this buffer is linear float.
    vec3 linear = pow(bulb_color, vec3(2.2));

    fragColor = vec4(linear * ((core * vDim + glare) * bulb_gain * vis), 0.0);
}
