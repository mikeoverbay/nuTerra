﻿#version 450 core

#extension GL_ARB_bindless_texture : require
#extension GL_ARB_shading_language_include : require

#include "common.h" //! #include "../common.h"


layout (location = 0) out vec4 gColor;
layout (location = 1) out vec3 gNormal;
layout (location = 2) out vec4 gGMF;
layout (location = 3) out vec3 gPosition;

layout(binding = 2) uniform sampler2D normal_map;
layout(binding = 3) uniform sampler2D tile_map;


// TODO: use sampler2DArray
layout(binding =  4) uniform sampler2D c_tiles[4];


uniform float tile_scale;

in VS_OUT {
    vec3 vertexPosition;
    mat3 TBN;
    vec2 UV;
    float specular;
} fs_in;

float write_normal(void){
    vec4 n = texture(normal_map, fs_in.UV);
    vec3 norm;
    norm.xz = n.ag;
    norm.y = clamp(sqrt(1.0-(n.x * n.x) +(n.z * n.z)), -1.0, 1.0);
    //norm.x *= -1.0;
    norm.xyz = normalize(fs_in.TBN * norm);
    gNormal = norm.xyz * 0.5 + 0.5;
    return n.r;
}

vec4 get_tile( sampler2D samp, in vec2 uv)
{
    vec2 cropped = fract(uv) * vec2(0.875, 0.875) + vec2(0.0625, 0.0625);
    //set mip bias -2 per games specs
    return texture( samp, cropped,-2);
    }

void main(void)
    {

    float sc = tile_scale/4.0 ;
    vec2 t_uv = fract(fs_in.UV * sc);
    //t_uv += vec2 (0.5,0.5);
    //t_uv = t_uv * vec2(0.875) + vec2(0.0625);
    t_uv *= vec2 (-1.0, 1.0);
    vec4 c1,c2,c3,c4;
    c1 = get_tile(c_tiles[0], t_uv);
    c2 = get_tile(c_tiles[1], t_uv);
    c3 = get_tile(c_tiles[2], t_uv);
    c4 = get_tile(c_tiles[3], t_uv);
   
    vec4 mv = texture(tile_map, fs_in.UV);

    vec3 color = c1.rgb * mv.r;
    color = mix(color, c2.rgb, mv.g);
    color = mix(color, c3.rgb, mv.b);
    color = mix(color, c4.rgb, mv.a);


    float shadow = write_normal();
    
    
    gColor.rgb = color;// * (shadow+0.1);
    gColor.a = 0.0;

    gPosition = fs_in.vertexPosition;
    gGMF = vec4(0.2, 0.3, 128.0/255.0, 0.0);

}
