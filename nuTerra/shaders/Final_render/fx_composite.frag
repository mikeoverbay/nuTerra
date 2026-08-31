#version 450 core

// Composite the accumulated FX buffer over the lit frame.
//
// The FX passes used to blend straight into gColor, which is Rgba8. Every
// blend there clamps to 1.0, so overlapping additive cards saturated channel
// by channel: fire is roughly (1.0, 0.6, 0.2), so red hit the ceiling first
// and green then climbed to meet it, turning orange into yellow and then into
// white. Measured against the game's own frame, a third of our fire pixels sat
// pinned at R=G=255 where the game had one such pixel in 29191.
//
// They now accumulate into an Rgba16f target instead, and this pass brings the
// sum back into range ONCE, here. Compositing them among themselves first and
// then over the scene is not an approximation: premultiplied "over" is
// associative, so the arrangement is equivalent - only the intermediate
// clamping is gone.
//
// The buffer holds PREMULTIPLIED colour in rgb and accumulated coverage in a,
// which is exactly what this pass's One / OneMinusSrcAlpha blend consumes, so
// alpha smoke still attenuates the scene and additive fire (alpha 0) still
// only adds.

layout(binding = 0) uniform sampler2D fxBuffer;

layout(location = 0) out vec4 outColor;

void main(void)
{
    vec4 fx = texelFetch(fxBuffer, ivec2(gl_FragCoord.xy), 0);

    // Divide by the largest CHANNEL, not by luminance.
    //
    // Luminance is the obvious choice and it is wrong here, because it does not
    // bound the channels: a sum of (2.0, 1.2, 0.4) has luminance 1.31, so
    // dividing by it gives (1.52, 0.91, 0.30) - red is still over 1 and clips
    // on write, which is the very thing this pass exists to prevent. Measured,
    // that left 15.9% of fire pixels still pinned. Dividing by max(r,g,b)
    // guarantees every channel lands at or under 1, so nothing can clip.
    //
    // It is a pure scale, so the hue is preserved EXACTLY: (2.0, 1.2, 0.4)
    // becomes (1.0, 0.6, 0.2) - full brightness, still orange. A genuinely
    // white-hot sum like (5, 5, 4) becomes (1, 1, 0.8) and stays a white core,
    // which is what the game's own frame shows: orange everywhere, white only
    // in the middle.
    //
    // max(1.0, ...) so anything already inside range passes through UNCHANGED -
    // smoke, which sums well below 1, is not touched at all.
    //
    // rgb is premultiplied, so scaling it alone is correct - coverage must not
    // be scaled with it or the smoke would stop attenuating the scene.
    const float peak = max(fx.r, max(fx.g, fx.b));
    fx.rgb /= max(1.0, peak);

    outColor = fx;
}
