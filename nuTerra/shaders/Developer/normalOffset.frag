#version 450 core

out vec4 fragColor;
uniform sampler2D imageMap;
in vec2 texCoord;

void main(void)
{
    vec3 n = normalize(texture(imageMap, texCoord).xyz);
    if ( length(n) < .01 ) discard;
    fragColor.xyz = normalize(n*0.5+0.5);
}
