#version 430 core

out vec4 fragColor;

uniform sampler2D imageMap;

in vec2 texcoord;

void main(void)
{
	vec3 n = normalize(texture(imageMap, texcoord).xyz)*0.5+0.5;// shift to 0.0 to 1.0
	if ( n == vec3(0.0) ) discard;
	fragColor.xyz = n;
	fragColor.a = 1.0;
}