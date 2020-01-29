#version 330 compatibility
out vec4 fragColor;
uniform sampler2D normalMap;
in vec2 texcoord;

void main(void){
vec3 n = normalize(texture2D(normalMap, texcoord).xyz)*0.5+0.5;// shift to 0.0 to 1.0
if ( n == 0.0 ) discard;
fragColor.xyz = n;
fragColor.a = 1.0;
}