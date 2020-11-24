#version 450 core

#extension GL_ARB_shading_language_include : require
#include "common.h" //! #include "../common.h"

layout(early_fragment_tests) in;

layout (location = 0) out vec4 gColor;
layout (location = 1) out vec3 gNormal;
layout (location = 2) out vec4 gGMF;
layout (location = 3) out vec3 gPosition;
layout (location = 4) out uint gPick;
layout (location = 5) out vec4 gAux;

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

layout(binding = 22) uniform sampler2DArray textArrayC;
layout(binding = 23) uniform sampler2DArray textArrayN;
layout(binding = 24) uniform sampler2DArray textArrayG;

layout(binding = 25) uniform sampler2D NRP_noise;


uniform vec3 waterColor;
uniform float waterAlpha;
uniform float map_id;
uniform float test;

in VS_OUT {
    mat3 TBN;
    vec4 Vertex;
    vec3 worldPosition;
    vec2 tuv1, tuv2, tuv3, tuv4, tuv5, tuv6, tuv7, tuv8; 
    vec2 UV;
    vec2 Global_UV;
    float ln;
    flat float is_hole;
} fs_in;


/*===========================================================*/
// https://www.gamedev.net/articles/programming/graphics/advanced-terrain-texture-splatting-r3287/
vec3 blend(vec4 texture1, float a1, vec4 texture2, float a2) {
 float depth = 0.2;
 float ma = max(texture1.a + a1, texture2.a + a2) - depth;
 float b1 = max(texture1.a + a1 - ma, 0);
 float b2 = max(texture2.a + a2 - ma, 0);
 return (texture1.rgb * b1 + texture2.rgb * b2) / (b1 + b2);
 }
/*===========================================================*/

// Used to add normals together. Could be better.
vec4 add_norms (in vec4 n1, in vec4 n2) {
    n1.xyz += n2.xyz;
    return n1;
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
//http://www.iquilezles.org/www/articles/texturerepetition/texturerepetition.htm
float sum( vec4 v ) {
    return v.x+v.y+v.z;
    }

vec4 textureNoTile( sampler2D samp, in vec2 uv ,in float flag, in out float b)
{

   uv = fract(uv) * vec2(0.875) + vec2(0.0625);
   
   b =0.0;
   if (uv.x < 0.065 ) b = 1.0;
   if (uv.x > 0.935 ) b = 1.0;
   if (uv.y < 0.065 ) b = 1.0;
   if (uv.y > 0.935 ) b = 1.0;


   return texture(samp,uv,0.6);

// Disabled for now.
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
vec4 get_dom_normal(vec4 n1,   vec4 n2,   vec4 n3,   vec4 n4, 
                    vec4 n5,   vec4 n6,   vec4 n7,   vec4 n8,
                    vec2 mix1, vec2 mix2, vec2 mix3, vec2 mix4, out float val){

    vec4 n;
    val = 0.0;
       if (mix1.r > val){ n = n1; val = mix1.r; }
       if (mix1.g > val){ n = n2; val = mix1.g; }
       if (mix2.r > val){ n = n3; val = mix2.r; }
       if (mix2.g > val){ n = n4; val = mix2.g; }
       if (mix3.r > val){ n = n5; val = mix3.r; }
       if (mix3.g > val){ n = n6; val = mix3.g; }
       if (mix4.r > val){ n = n7; val = mix4.r; }
       if (mix4.g > val){ n = n8; val = mix4.g; }

    return n;
}
/*===========================================================*/
/*===========================================================*/
/*===========================================================*/
/*===========================================================*/

void main(void)
{
    vec4 global = texture(global_AM, fs_in.Global_UV);
    // This is needed to light the global_AM.
    //Can we bail early?
//    if (fs_in.is_hole == 1.0)
//    {
//        gColor = global;
//        gColor.a = 1.0;
//        //if (fs_in.ln > 0.0 ) gColor.r = 1.0;
//        gGMF.rgb = vec3(global.a+0.2, 0.0, 128.0/255.0);
//
//        gPosition = fs_in.worldPosition;
//        gPick = 0;
//        return;
//    }
    //==============================================================
    float B1, B2, B3, B4, B5, B6, B7, B8;
    vec4 color_1 = vec4(1.0,  1.0,  0.0,  0.0);
    vec4 color_2 = vec4(0.0,  1.0,  0.0,  0.0);
    vec4 color_3 = vec4(0.0,  0.0,  1.0,  0.0);
    vec4 color_4 = vec4(1.0,  1.0,  0.0,  0.0);
    vec4 color_5 = vec4(1.0,  0.0,  1.0,  0.0);
    vec4 color_6 = vec4(1.0,  0.65, 0.0,  0.0);
    vec4 color_7 = vec4(1.0,  0.49, 0.31, 0.0);
    vec4 color_8 = vec4(0.5,  0.5,  0.5,  0.0);

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
    // Get AM maps,crop, detilize and set Test outline blend flag
    t1 = textureNoTile(layer_1T1, fs_in.tuv1, r1_1.z, B1);
    t2 = textureNoTile(layer_1T2, fs_in.tuv2, r1_2.z, B2);

    t3 = textureNoTile(layer_2T1, fs_in.tuv3, r1_3.z, B3);
    t4 = textureNoTile(layer_2T2, fs_in.tuv4, r1_4.z, B4);

    t5 = textureNoTile(layer_3T1, fs_in.tuv5, r1_5.z, B5);
    t6 = textureNoTile(layer_3T2, fs_in.tuv6, r1_6.z, B6);

    t7 = textureNoTile(layer_4T1, fs_in.tuv7, r1_7.z, B7);
    t8 = textureNoTile(layer_4T2, fs_in.tuv8, r1_8.z, B8);

    // ambient occusion is in blue channel of the normal maps.
    // Specular OR Parallax is in the red channel. Green and Alpha are normal values.
    // We must get the Ambient Occlusion before converting so it isn't lost.

    // Get and convert normal maps. Save ambient occlusion value.

    n1 = textureNoTile(n_layer_1T1, fs_in.tuv1, r1_1.z, B1);
    aoc_0 = n1.b;
    n1 = convertNormal(n1);// + U1;
    n1.a = t1.a; // for blend function

    n2 = textureNoTile(n_layer_1T2, fs_in.tuv2, r1_2.z, B2);
    aoc_1 =  n2.b;
    n2 = convertNormal(n2);// + U2;
    n2.a = t2.a; // for blend function

    n3 = textureNoTile(n_layer_2T1, fs_in.tuv3, r1_3.z, B3);
    aoc_2 = n3.b;
    n3= convertNormal(n3);// + U3;
    n3.a = t3.a; // for blend function

    n4 = textureNoTile(n_layer_2T2, fs_in.tuv4, r1_4.z, B4);
    aoc_3 = n4.b;
    n4 = convertNormal(n4);// + U4;
    n4.a = t4.a; // for blend function

    n5 = textureNoTile(n_layer_3T1, fs_in.tuv5, r1_5.z, B5);
    aoc_4 = n5.b;
    n5 = convertNormal(n5);// + U5;
    n5.a = t5.a; // for blend function

    n6 = textureNoTile(n_layer_3T2, fs_in.tuv6, r1_6.z, B6);
    aoc_5 = n6.b;
    n6 = convertNormal(n6);// + U6;
    n6.a = t6.a; // for blend function

    n7 = textureNoTile(n_layer_4T1, fs_in.tuv7, r1_7.z, B7);
    aoc_6 = n7.b;
    n7 = convertNormal(n7);// + U7;
    n7.a = t7.a; // for blend function

    n8= textureNoTile(n_layer_4T2, fs_in.tuv8, r1_8.z, B8);
    aoc_7 = n8.b;
    n8 = convertNormal(n8);// + U8;
    n8.a = t8.a; // for blend function
    
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
    base = t8 * MixLevel4.g;

    base.rgb = blend(base, aoc_7 * MixLevel4.g, t7, aoc_7);
    base.rgb = blend(base, aoc_7, t7, aoc_6 * MixLevel4.r);
    base.rgb = blend(base, aoc_6, t6, aoc_5 * MixLevel3.g);
    base.rgb = blend(base, aoc_5, t5, aoc_4 * MixLevel3.r);
    base.rgb = blend(base, aoc_4, t4, aoc_3 * MixLevel2.g);
    base.rgb = blend(base, aoc_3, t3, aoc_2 * MixLevel2.r);
    base.rgb = blend(base, aoc_2, t2, aoc_1 * MixLevel1.g);
    base.rgb = blend(base, aoc_1, t1, aoc_0 * MixLevel1.r);


    // Texture outlines if test = 1.0;
    base = mix(base, base + color_1, B1 * test * MixLevel1.r);
    base = mix(base, base + color_2, B2 * test * MixLevel1.g);
    base = mix(base, base + color_3, B3 * test * MixLevel2.r);
    base = mix(base, base + color_4, B4 * test * MixLevel2.g);
    base = mix(base, base + color_5, B5 * test * MixLevel3.r);
    base = mix(base, base + color_6, B6 * test * MixLevel3.g);
    base = mix(base, base + color_7, B7 * test * MixLevel4.r);
    base = mix(base, base + color_8, B8 * test * MixLevel4.g);

    //Get our normal maps. Same mixing and clamping as AM maps above

    //-------------------------------------------------------------

    vec4 out_n = vec4(0.0);
    out_n.rgb = normalize(n7.rgb);
    out_n.rgb = blend(out_n,aoc_7 * MixLevel4.g ,normalize(n7), aoc_7);
    out_n.rgb = blend(out_n,aoc_7, normalize(n7) ,aoc_6 * MixLevel4.r);
    out_n.rgb = blend(out_n,aoc_6, normalize(n6) ,aoc_5 * MixLevel3.g);
    out_n.rgb = blend(out_n,aoc_5, normalize(n5) ,aoc_4 * MixLevel3.r);
    out_n.rgb = blend(out_n,aoc_4, normalize(n4) ,aoc_3 * MixLevel2.g);
    out_n.rgb = blend(out_n,aoc_3, normalize(n3) ,aoc_2 * MixLevel2.r);
    out_n.rgb = blend(out_n,aoc_2, normalize(n2) ,aoc_1 * MixLevel1.g);
    out_n.rgb = blend(out_n,aoc_1, normalize(n1) ,aoc_0 * MixLevel1.r);
    
    //Find dom Normal
    float nBlend;
    vec4 top_n = get_dom_normal(n1, n2, n3, n4, n5, n6, n7, n8,
                           MixLevel1.rg, MixLevel2.rg, MixLevel3.rg,
                           MixLevel4.rg, nBlend);
    out_n = mix(out_n, top_n, nBlend);
    out_n.xyz = fs_in.TBN * out_n.xyz;

    // Mix in the global_AM color using global_AM's alpha channel.

    // I think this is used for wetness on the map.
    base.rgb = mix(base.rgb ,waterColor, global.a * waterAlpha);
    
    // This blends between low and highrez by distance

    // This blends the layered colors/normals and the global_AM/normalMaps over distance.
    // The blend stats at 100 and ends at 400. This has been changed for debug
    // Replace ln with 1.0 to show only layered terrain.
    
    //base = mix(base,vec4(MixLevel1.xy, 0.0 ,1.0), 0.4);
    vec4 ArrayTextureC = texture(textArrayC, vec3(fs_in.UV, map_id) );
    vec4 ArrayTextureN = texture(textArrayN, vec3(fs_in.UV, map_id) );
    vec4 ArrayTextureG = texture(textArrayG, vec3(fs_in.UV, map_id) );

    ArrayTextureN.xyz = fs_in.TBN * ArrayTextureN.xyz;

    base = mix(ArrayTextureC, base, fs_in.ln);
    out_n = mix(ArrayTextureN, out_n, fs_in.ln) ;
    gGMF = mix(ArrayTextureG, vec4(0.2, 0.0, 128.0/255.0, global.a*0.8), fs_in.ln);

    // The obvious
    gColor = base;
    //gColor = gColor* 0.001 + r1_8;
    gColor.a = 1.0;

    gNormal.xyz = normalize(out_n.xyz);

    gAux.rgb = waterColor;
    gAux.a = global.a * waterAlpha;

    gPosition = fs_in.worldPosition;
    gPick = 0;
}
