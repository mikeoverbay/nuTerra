#version 450 core

#extension GL_ARB_bindless_texture : require
#extension GL_ARB_shading_language_include : require

#include "common.h" //! #include "../common.h"


layout (location = 0) out vec4 gColor;
layout (location = 1) out vec3 gNormal;
layout (location = 2) out vec4 gGMF;
layout (location = 3) out vec3 gPosition;

layout(binding = 2) uniform sampler2D normal_map;
layout(binding = 3) uniform sampler2D tile_map;


layout(binding =  4) uniform sampler2D c_tile_1;
layout(binding =  5) uniform sampler2D c_tile_2;
layout(binding =  6) uniform sampler2D c_tile_3;
layout(binding =  7) uniform sampler2D c_tile_4;


uniform float tile_scale;

in VS_OUT {
    vec3 vertexPosition;
    mat3 TBN;
    vec2 UV;
    float specular;
} fs_in;

vec3 convert_normal(in vec4 n){
vec3 norm;
norm.xz = n.ag;
norm.y = clamp(sqrt(1.0-(n.x * n.x) +(n.z * n.z)), -1.0, 1.0);
return normalize(norm);
}

void main(void)
    {

    float sc = tile_scale/2.0 ;
    vec2 t_uv = fract(fs_in.UV * sc);
    //t_uv += vec2 (0.5,0.5);
    //t_uv = t_uv * vec2(0.875) + vec2(0.0625);
    t_uv *= vec2 (1.0, 1.0);
    vec4 c1,c2,c3,c4;
    c1 = texture(c_tile_1, t_uv);
    c2 = texture(c_tile_2, t_uv);
    c3 = texture(c_tile_3, t_uv);
    c4 = texture(c_tile_4, t_uv);

    float mv = texture(tile_map, fs_in.UV).r;
    int m = int(255 * mv);
    int m1 = m & 0x1;
    int m2 = m & 0x4 >> 2;
    int m3 = m & 0x16 >> 4;
    int m4 = m & 0x64 >> 6;

    float ml = 0.95;
    float mx1 = float(m1 * ml);
    float mx2 = float(m2 * ml);
    float mx3 = float(m3 * ml);
    float mx4 = float(m4 * ml);

    vec3 color = c1.rgb;
    color.rgb = color.rgb + c2.rgb * c2.w * mx2;
    color.rgb = color.rgb + c3.rgb * c3.w * mx3;
    color.rgb = color.rgb + c4.rgb * c4.w * mx4;

    gPosition = fs_in.vertexPosition;
    vec4 n = texture(normal_map, fs_in.UV);
    float shadow = n.r;
    n.xyz = fs_in.TBN * convert_normal(n);

    gNormal = n.xyz * 0.5 + 0.5;
    
    gColor.rgb = color;// * shadow;
    gColor.a = 0.0;

    gGMF = vec4(0.2, 0.3, 128.0/255.0, 0.0);

}
