#version 430 core

out vec4 fragColor;
uniform sampler2D imageMap;
in vec2 texCoord;

void main(void)
{
    vec3 n = normalize(texture(imageMap, texCoord).xyz*0.5+0.5);
    if ( n == vec3(0.0) ) discard;
    fragColor.xyz = normalize(n);
}
