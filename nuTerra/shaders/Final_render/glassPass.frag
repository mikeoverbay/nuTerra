#version 450 core


layout (binding = 0) uniform sampler2D colorMap;
layout (binding = 1) uniform sampler2D glassMap;

out vec4 blend;

in VS_OUT {
    vec2 UV;
} fs_in;

void main(void){

vec4 color = texture(colorMap, fs_in.UV);
vec4 glass = texture(glassMap, fs_in.UV);
blend.rgb = mix(color.rgb, glass.rgb, glass.a);
blend.a = 1.0;
}