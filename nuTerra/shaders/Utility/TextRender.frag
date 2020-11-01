#version 450 core

layout (binding = 0) uniform sampler2D imageMap;
layout (location = 5) uniform vec4 color;
layout (location = 6) uniform int mask;

layout (location = 0) in VS_OUT {
    vec2 texCoord;
} fs_in;

layout (location = 0) out vec4 fragColor;


void main(void)
{
    if (mask == 1) {
        fragColor.a = 1.0;
        vec4 co = texture(imageMap, fs_in.texCoord) * color;
        fragColor.rgb = co.rgb * co.a;
        fragColor.rgb += vec3(0.3, 0.3, 0.3) * (1.0 - co.a);
    } else {
        fragColor = texture(imageMap, fs_in.texCoord) * color;
    }
}
