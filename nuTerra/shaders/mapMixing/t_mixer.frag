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

    float used_1;
    float used_2;
    float used_3;
    float used_4;
    float used_5;
    float used_6;
    float used_7;
    float used_8;
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
    mat3 TBN;
    vec2 tuv1, tuv2, tuv3, tuv4, tuv5, tuv6, tuv7, tuv8; 
    vec2 UV;
    vec2 Global_UV;
    flat float is_hole;
} fs_in;

/*===========================================================*/
//http://www.iquilezles.org/www/articles/texturerepetition/texturerepetition.htm
float sum( vec4 v ) {
    return v.x+v.y+v.z;
    }
vec4 textureNoTile( sampler2D samp, in vec2 uv ,in float flag)
{
    if (flag == 0.0 ) return texture(samp,uv);

    // sample variation pattern    
    float k = texture( NRP_noise, 0.005*uv ).x; // cheap (cache friendly) lookup    
    
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

// Used to add normals together. Could be better.
vec4 add_norms (in vec4 n1, in vec4 n2) {
    n1.xyz += n2.xyz;
    return n1;
}
//-------------------------------------------------------------

// Converion from AG map to RGB vector.
vec4 convertNormal(vec4 norm){
        vec3 n;
        n.xy = clamp(norm.ag*2.0-1.0, -1.0 ,1.0);
        n.z = max(sqrt(1.0 - (n.x*n.x - n.y *n.y)),0.0);
        n.x *= -1.0; // X needs flipped DX to OpenGL
        return vec4(n,0.0);
}

/*===========================================================*/
vec2 crop(in vec2 tc){
        return fract(tc) * vec2(0.875) + vec2(0.0625); 
    }
/*===========================================================*/



void main(void)
{

    //==============================================================
    vec4 global = texture(global_AM, fs_in.Global_UV);
    // This is needed to light the global_AM.
    //Can we bail early?
//    if (fs_in.is_hole == 1.0)
//    {
//        gColor = global;
//        gColor.a = 1.0;
//        gGMF.rgb = vec3(global.a+0.2, 0.0, 128.0/255.0);
//        return;
//    }
    //==============================================================

    vec4 t1, t2, t3, t4, t5, t6, t7, t8;
    vec4 n1, n2, n3, n4, n5, n6, n7, n8;

    vec2 MixLevel1, MixLevel2, MixLevel3, MixLevel4;
    vec3 PN1, PN2, PN3, PN4;
    float aoc_0, aoc_1, aoc_2, aoc_3;
    float  aoc_4, aoc_5, aoc_6, aoc_7;
    vec2 mix_coords;

    mix_coords = fs_in.UV;
    mix_coords.x = 1.0 - mix_coords.x;
    vec2 UVs = fs_in.UV;

    // create UV projections

    // Get AM maps and Test Texture maps
    t1 = textureNoTile(layer_1T1, crop(fs_in.tuv1), r1_1.z);
    t2= textureNoTile(layer_1T2, crop(fs_in.tuv2), r1_2.z);

    t3 = textureNoTile(layer_2T1, crop(fs_in.tuv3), r1_3.z);
    t4 = textureNoTile(layer_2T2, crop(fs_in.tuv4), r1_4.z);

    t5 = textureNoTile(layer_3T1, crop(fs_in.tuv5), r1_5.z);
    t6 = textureNoTile(layer_3T2, crop(fs_in.tuv6), r1_6.z);

    t7 = textureNoTile(layer_4T1, crop(fs_in.tuv7), r1_7.z);
    t8 = textureNoTile(layer_4T2, crop(fs_in.tuv8), r1_8.z);



    // ambient occusion is in blue channel of the normal maps.
    // Specular OR Parallax is in the red channel. Green and Alpha are normal values.
    // We must get the Ambient Occlusion before converting so it isn't lost.

    // Get and convert normal maps. Save ambient occlusion value.

    n1 = textureNoTile(n_layer_1T1, crop(fs_in.tuv1), r1_1.z);
    aoc_0 = n1.b;
    n1 = convertNormal(n1) + U1;

    n2 = textureNoTile(n_layer_1T2, crop(fs_in.tuv2), r1_2.z);
    aoc_1 =  n2.b;
    n2 = convertNormal(n2) + U2;

    n3 = textureNoTile(n_layer_2T1, crop(fs_in.tuv3), r1_3.z);
    aoc_2 = n3.b;
    n3= convertNormal(n3) + U3;

    n4 = textureNoTile(n_layer_2T2, crop(fs_in.tuv4), r1_4.z);
    aoc_3 = n4.b;
    n4 = convertNormal(n4) + U4;

    n5 = textureNoTile(n_layer_3T1, crop(fs_in.tuv5), r1_5.z);
    aoc_4 = n5.b;
    n5 = convertNormal(n5) + U5;

    n6 = textureNoTile(n_layer_3T2, crop(fs_in.tuv6), r1_6.z);
    aoc_5 = n6.b;
    n6 = convertNormal(n6) + U6;

    n7 = textureNoTile(n_layer_4T1, crop(fs_in.tuv7), r1_7.z);
    aoc_6 = n7.b;
    n7 = convertNormal(n7) + U7;

    n8= textureNoTile(n_layer_4T2, crop(fs_in.tuv8), r1_8.z);
    aoc_7 = n8.b;
    n8 = convertNormal(n8) + U8;

    
    //Get the mix values from the mix textures 1-4 and move to vec2. 
    MixLevel1.rg = texture(mixtexture1, mix_coords.xy).ag;
    MixLevel2.rg = texture(mixtexture2, mix_coords.xy).ag;
    MixLevel3.rg = texture(mixtexture3, mix_coords.xy).ag;
    MixLevel4.rg = texture(mixtexture4, mix_coords.xy).ag;

    // Uniforms used_1 thru used_8 are either 0 or 1
    // depending on if the slot is used.
    // It is used to clamp unused values to 0 so
    // they have no affect on shading.

    vec4 base = vec4(0.0);  

    // Mix our textures in to base and
    // apply Ambient Occlusion.
    // Mix group 4
    base += t7 * aoc_6 * MixLevel4.r * used_7;
    base += t8 * aoc_7 * MixLevel4.g * used_8;

    // Mix group 3
    base += t5 * aoc_4 * MixLevel3.r * used_5;
    base += t6 * aoc_5 * MixLevel3.g * used_6;

    // Mix group 2
    base += t3 * aoc_2 * MixLevel2.r * used_3;
    base += t4 * aoc_3 * MixLevel2.g * used_4;

    // Mix group 1
    base += t1 * aoc_0 * MixLevel1.r * used_1;
    base += t2 * aoc_1 * MixLevel1.g * used_2;
    
    //Get our normal maps. Same mixing and clamping as AM maps above

    // Mix group 4
    n7.rgb = normalize(n7.rgb) * MixLevel4.r * used_7;
    n8.rgb = normalize(n8.rgb) * MixLevel4.g * used_8;

    // Mix group 3
    n5.rgb =  normalize(n5.rgb) * MixLevel3.r * used_5;
    n6.rgb = normalize(n6.rgb) * MixLevel3.g * used_6;

    // Mix group 2
    n3.rgb = normalize(n3.rgb) * MixLevel2.r * used_3;
    n4.rgb = normalize(n4.rgb) * MixLevel2.g * used_4;

    // Mix group 1
    n1.rgb = normalize(n1.rgb) * MixLevel1.r * used_1;
    n2.rgb =  normalize(n2.rgb) * MixLevel1.g * used_2;


    //flip X axis. Everything is flipped on X including texture rotations.

    //n1.x *= -1.0;
    //n2.x *= -1.0;
    //n3.x *= -1.0;
    //n4.x *= -1.0;

    //n1_2.x *= -1.0;
    //n2_2.x *= -1.0;
    //n3_2.x *= -1.0;
    //n4_2.x *= -1.0;

    //-------------------------------------------------------------

    //n_tex.x*=-1.0;
    vec4 out_n = vec4(0.0);
    // Add up our normal values.
    out_n = add_norms(out_n, n1);
    out_n = add_norms(out_n, n2);
    out_n = add_norms(out_n, n3);
    out_n = add_norms(out_n, n4);
    out_n = add_norms(out_n, n5);
    out_n = add_norms(out_n, n6);
    out_n = add_norms(out_n, n7);
    out_n = add_norms(out_n, n8);

    
    // Mix in the global_AM color using global_AM's alpha channel.

    // I think this is used for wetness on the map.
    base.rgb = mix(base.rgb ,waterColor, global.a * waterAlpha);
    
    gNormal.xyz = normalize(out_n.xyz);
    gColor = base;
    gGMF = vec4(0.2, 0.0, 128.0/255.0, global.a*0.8);
    gColor.rgb = mix (gColor.rgb, gColor.rgb * waterColor,global.a*0.8);
    gColor.a = global.a * waterAlpha;

}
