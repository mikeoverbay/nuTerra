#version 450 core

#extension GL_ARB_shading_language_include : require
#include "common.h" //! #include "../common.h"

layout (location = 0) out vec4 gColor;
layout (location = 1) out vec3 gNormal;
layout (location = 2) out vec4 gGMF;

layout (std140, binding = TERRAIN_LAYERS_UBO_BASE) uniform Layers {
    vec4 U1;
    vec4 U2;
    vec4 U3;
    vec4 U4;

    vec4 U5;
    vec4 U6;
    vec4 U7;
    vec4 U8;

    vec4 V1;
    vec4 V2;
    vec4 V3;
    vec4 V4;

    vec4 V5;
    vec4 V6;
    vec4 V7;
    vec4 V8;

    vec4 r1_1;
    vec4 r1_2;
    vec4 r1_3;
    vec4 r1_4;
    vec4 r1_5;
    vec4 r1_6;
    vec4 r1_7;
    vec4 r1_8;

    vec4 r2_1;
    vec4 r2_2;
    vec4 r2_3;
    vec4 r2_4;
    vec4 r2_5;
    vec4 r2_6;
    vec4 r2_7;
    vec4 r2_8;

    vec4 s1;
    vec4 s2;
    vec4 s3;
    vec4 s4;
    vec4 s5;
    vec4 s6;
    vec4 s7;
    vec4 s8;
    };

layout(binding = 1 ) uniform sampler2D layer_1T1;
layout(binding = 2 ) uniform sampler2D layer_2T1;
layout(binding = 3 ) uniform sampler2D layer_3T1;
layout(binding = 4 ) uniform sampler2D layer_4T1;

layout(binding = 5 ) uniform sampler2D layer_1T2;
layout(binding = 6 ) uniform sampler2D layer_2T2;
layout(binding = 7 ) uniform sampler2D layer_3T2;
layout(binding = 8 ) uniform sampler2D layer_4T2;

layout(binding = 9 ) uniform sampler2D n_layer_1T1;
layout(binding = 10) uniform sampler2D n_layer_2T1;
layout(binding = 11) uniform sampler2D n_layer_3T1;
layout(binding = 12) uniform sampler2D n_layer_4T1;

layout(binding = 13) uniform sampler2D n_layer_1T2;
layout(binding = 14) uniform sampler2D n_layer_2T2;
layout(binding = 15) uniform sampler2D n_layer_3T2;
layout(binding = 16) uniform sampler2D n_layer_4T2;

layout(binding = 17) uniform sampler2D mixtexture1;
layout(binding = 18) uniform sampler2D mixtexture2;
layout(binding = 19) uniform sampler2D mixtexture3;
layout(binding = 20) uniform sampler2D mixtexture4;


layout(binding = 21) uniform sampler2D global_AM;
layout(binding = 22) uniform sampler2D NRP_noise;

uniform vec3 waterColor;
uniform float waterAlpha;

in VS_OUT {
    vec2 tuv1, tuv2, tuv3, tuv4, tuv5, tuv6, tuv7, tuv8; 
    vec2 UV;
    vec2 Global_UV;
} fs_in;

/*===========================================================*/
// https://www.gamedev.net/articles/programming/graphics/advanced-terrain-texture-splatting-r3287/
vec4 blend(vec4 texture1, float a1, vec4 texture2, float a2) {
 float depth = 0.5;
 float ma = max(texture1.a + a1, texture2.a + a2) - depth;
 float b1 = max(texture1.a + a1 - ma, 0);
 float b2 = max(texture2.a + a2 - ma, 0);
 return (texture1 * b1 + texture2 * b2) / (b1 + b2);
 }
 //have to do this because we need the alpha in the am textures.
vec4 blend_normal(vec4 n1, vec4 n2, vec4 texture1, float a1, vec4 texture2, float a2) {
 float depth = 0.5;
 float ma = max(texture1.a + a1, texture2.a + a2) - depth;
 float b1 = max(texture1.a + a1 - ma, 0);
 float b2 = max(texture2.a + a2 - ma, 0);
 return (n1 * b1 + n2 * b2) / (b1 + b2);
 }
/*===========================================================*/

//http://www.iquilezles.org/www/articles/texturerepetition/texturerepetition.htm
float sum( vec4 v ) {
    return v.x+v.y+v.z;
    }
vec4 textureNoTile( sampler2D samp, in vec2 uv ,in float flag)
{

   uv = fract(uv) * vec2(0.875) + vec2(0.0625);
   return texture(samp,uv);

   // disabled for now
   if (flag == 0.0 ){
      }

    // sample variation pattern    
    float k = texture( NRP_noise, fract(uv)*0.005 ).x; // cheap (cache friendly) lookup    
    
    // compute index    
    float index = k*8.0;
    float i = floor( index );
    float f = fract( index );

    // offsets for the different virtual patterns    
    vec2 offa = sin(vec2(3.0,7.0)*(i+0.0)); // can replace with any other hash    
    vec2 offb = sin(vec2(3.0,7.0)*(i+1.0)); // can replace with any other hash    

    // compute derivatives for mip-mapping    
    vec2 dx = dFdx(uv), dy = dFdy(uv);
    
    // sample the two closest virtual patterns    
    vec4 cola = textureGrad( samp, uv + offa, dx, dy );
    vec4 colb = textureGrad( samp, uv + offb, dx, dy );

    // interpolate between the two virtual patterns   
    float s = smoothstep(0.2,0.8,f-0.1* sum(cola-colb) );
    return mix( cola, colb, s);
    }
/*===========================================================*/

// Converion from AG map to RGB vector.
vec4 convertNormal(vec4 norm){
        vec3 n;
        n.xy = clamp(norm.ag*2.0-1.0, -1.0 ,1.0);
        n.z = max(sqrt(1.0 - (n.x*n.x - n.y *n.y)),0.0);
        n.x *= -1.0; // X needs flipped DX to OpenGL
        return vec4(n,0.0);
}

/*===========================================================*/
/*===========================================================*/
/*===========================================================*/

void main(void)
{
    //==============================================================
    vec4 t1, t2, t3, t4, t5, t6, t7, t8;
    vec4 n1, n2, n3, n4, n5, n6, n7, n8;
    float  aoc_0, aoc_1, aoc_2, aoc_3;
    float  aoc_4, aoc_5, aoc_6, aoc_7;

    vec2 MixLevel1, MixLevel2, MixLevel3, MixLevel4;
    vec3 PN1, PN2, PN3, PN4;
    float height_0, height_1, height_2, height_3;
    float  height_4, height_5, height_6, height_7;
    vec2 mix_coords;
    //==============================================================

    mix_coords = fs_in.UV;
    mix_coords.x = 1.0 - mix_coords.x;
    vec2 UVs = fs_in.UV;

    vec4 global = texture(global_AM, fs_in.Global_UV);

    // Get AM maps and Test Texture maps
    t1 = textureNoTile(layer_1T1, fs_in.tuv1, r1_1.z);
    t2 = textureNoTile(layer_1T2, fs_in.tuv2, r1_2.z);

    t3 = textureNoTile(layer_2T1, fs_in.tuv3, r1_3.z);
    t4 = textureNoTile(layer_2T2, fs_in.tuv4, r1_4.z);

    t5 = textureNoTile(layer_3T1, fs_in.tuv5, r1_5.z);
    t6 = textureNoTile(layer_3T2, fs_in.tuv6, r1_6.z);

    t7 = textureNoTile(layer_4T1, fs_in.tuv7, r1_7.z);
    t8 = textureNoTile(layer_4T2, fs_in.tuv8, r1_8.z);

    // height is in blue channel of the normal maps.
    // Specular is in the red channel. Green and Alpha are normal values.

    n1 = textureNoTile(n_layer_1T1, fs_in.tuv1, r1_1.z);
    n2 = textureNoTile(n_layer_1T2, fs_in.tuv2, r1_2.z);
    n3 = textureNoTile(n_layer_2T1, fs_in.tuv3, r1_3.z);
    n4 = textureNoTile(n_layer_2T2, fs_in.tuv4, r1_4.z);
    n5 = textureNoTile(n_layer_3T1, fs_in.tuv5, r1_5.z);
    n6 = textureNoTile(n_layer_3T2, fs_in.tuv6, r1_6.z);
    n7 = textureNoTile(n_layer_4T1, fs_in.tuv7, r1_7.z);
    n8 = textureNoTile(n_layer_4T2, fs_in.tuv8, r1_8.z);

    // get the heights
    aoc_0 = n1.b;
    aoc_1 = n2.b;
    aoc_2 = n3.b;
    aoc_3 = n4.b;
    aoc_4 = n5.b;
    aoc_5 = n6.b;
    aoc_6 = n7.b;
    aoc_7 = n8.b;

    
    //Get the mix values from the mix textures 1-4 and move to vec2. 
    MixLevel1.rg = texture(mixtexture1, mix_coords.xy).ag;
    MixLevel2.rg = texture(mixtexture2, mix_coords.xy).ag;
    MixLevel3.rg = texture(mixtexture3, mix_coords.xy).ag;
    MixLevel4.rg = texture(mixtexture4, mix_coords.xy).ag;

    // Mix our textures in to base and

    vec4 base = vec4(0.0);  

    vec4 m4 = blend(t7, aoc_6 * MixLevel4.r, t8 , aoc_7 * MixLevel4.g);

    vec4 m3 = blend(t5, aoc_4 * MixLevel3.r, t6 , aoc_5 * MixLevel3.g);

    vec4 m2 = blend(t3, aoc_2 * MixLevel2.r, t4 , aoc_3 * MixLevel2.g);

    vec4 m1 = blend(t1, aoc_0 * MixLevel1.r, t2 , aoc_1 * MixLevel1.g);


    vec4 m5 = blend(m3, aoc_5 + MixLevel3.r+MixLevel3.g, m4, aoc_6 + MixLevel4.r+MixLevel4.g);

    vec4 m6 = blend(m1 ,aoc_1 + MixLevel1.r+MixLevel1.g, m2, aoc_2 + MixLevel2.r+MixLevel2.g);

    vec4 m7 = blend(m5, aoc_4 + MixLevel3.r+MixLevel3.g+MixLevel4.r+MixLevel4.g, m6, aoc_3 + MixLevel1.r+MixLevel1.g+ MixLevel2.r+MixLevel2.g);

    base = m7;


    //-------------------------------------------------------------
    // normals
    vec4 out_n = vec4(0.0);

     m4 = blend_normal(n7, n8, t7 , aoc_6 * MixLevel4.r, t8 , aoc_7 * MixLevel4.g);

     m3 = blend_normal(n5, n6, t5, aoc_4 * MixLevel3.r, t6 , aoc_5 * MixLevel3.g);

     m2 = blend_normal(n3, n4, t3, aoc_2 * MixLevel2.r, t4 , aoc_3 * MixLevel2.g);

     m1 = blend_normal(n1, n2, t1, aoc_0 * MixLevel1.r, t2 , aoc_1 * MixLevel1.g);


     m5 = blend(m3, aoc_5 + MixLevel3.r+MixLevel3.g, m4, aoc_6 + MixLevel4.r+MixLevel4.g);

     m6 = blend(m1, aoc_1 + MixLevel1.r+MixLevel1.g, m2, aoc_2 + MixLevel2.r+MixLevel2.g);

     m7 = blend(m5, aoc_4 + MixLevel3.r+MixLevel3.g+MixLevel4.r+MixLevel4.g, m6, aoc_3 + MixLevel1.r+MixLevel1.g+ MixLevel2.r+MixLevel2.g);

    out_n = m7;
     float specular = out_n.r;

     out_n = convertNormal(out_n);

     gNormal.xyz = normalize(out_n.xyz);

    // global.a is used for wetness on the map.
    gGMF = vec4(specular, 0.1, 128.0/255.0, global.a*0.8);
    
    gColor = base;
    gColor.a = 1.0;


}
