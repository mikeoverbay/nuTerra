// toLinear_fragment.gsls
// used to linearize depth textures to linear colors.
#version 430 compatibility

uniform sampler2D depthMap;

uniform float near;
uniform float far;

in vec2 texCoord;

float linearDepth(float depthSample)
{
    float f = far;
    float n = near;
    
    return  (2.0 * n) / (f + n - depthSample * (f - n));
}

void main()
{
    float r = linearDepth(texture2D(depthMap, texCoord).r);
    gl_FragColor = vec4(vec3(1.0 - r), 1.0);
}
