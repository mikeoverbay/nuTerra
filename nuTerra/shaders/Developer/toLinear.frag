#version 450 core

out vec4 fragColor;
uniform sampler2D imageMap;

uniform float near;
uniform float far;

in vec2 texCoord;

float linearDepth()
{
    float zFar = far;
    float zNear = near;
    
    float depth = texture(imageMap, texCoord).x;
    return (2.0 * zNear) / (zFar + zNear - depth * (zFar - zNear));
}

void main()
{
    float c = linearDepth();
    fragColor = vec4(c, c, c, 1.0);
}
