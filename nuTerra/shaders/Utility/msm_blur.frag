#version 450 core

// One axis of a separable Gaussian over the moment map.
//
// This is the whole reason for moments. A depth comparison has to be compared
// before it is averaged, so a depth map cannot legally be blurred - which is why
// PCF has to spend taps every frame and can never mipmap. Power moments are
// linear, so averaging them is exact, and the filtering can happen once here at
// bake time instead of per pixel forever.
layout(binding = 0) uniform sampler2D src;

// One texel step along the axis being blurred, zero on the other.
uniform vec2 direction;

in vec2 texCoord;
layout(location = 0) out vec4 fragColor;

void main(void)
{
    // 9 tap, sigma ~2.
    const float w[5] = float[5](0.2270270270, 0.1945945946,
                                0.1216216216, 0.0540540541, 0.0162162162);

    vec4 acc = texture(src, texCoord) * w[0];
    for (int i = 1; i < 5; ++i) {
        vec2 o = direction * float(i);
        acc += texture(src, texCoord + o) * w[i];
        acc += texture(src, texCoord - o) * w[i];
    }
    fragColor = acc;
}
