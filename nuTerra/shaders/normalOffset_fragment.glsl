#version 430 compatibility

layout (location = 0) out vec4 gColor;

uniform sampler2D normalMap;
in vec2 texcoord;

void main(void)
{
	vec3 n = normalize(texture(normalMap, texcoord).xyz)*0.5+0.5;// shift to 0.0 to 1.0
	if ( n == vec3(0.0) ) discard;
	gColor.xyz = n;
	gColor.a = 1.0;
}