#version 450 core

out vec4 fragColor;
layout (binding = 0) uniform sampler2D imageMap;

uniform bool reversed;
uniform float near;
uniform float far;

in vec2 texCoord;

float linearDepth()
{
    float depth = texture(imageMap, texCoord).x;
    if (reversed) {
        depth = 1.0 - depth;
    }
    return (2.0 * near) / (far + near - depth * (far - near));
}

void main()
{
    if (reversed) {
        float c = sqrt(sqrt(sqrt(linearDepth())));
        if (c < 1.0){
            fragColor = vec4(c, c, c, 1.0);
        }
    } else {
        fragColor = vec4(vec3(1.0 - linearDepth()), 1.0);
    }
}
