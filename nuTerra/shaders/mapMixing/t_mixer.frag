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

layout(binding = 23) uniform sampler2DArray shadow;

uniform int map_id;


in VS_OUT {

    vec4 Vertex;
    vec3 worldPosition;
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

vec3 ColorCorrect(in vec3 valin){  
    // Gamma correction 
   return  pow(valin.rgb, vec3(1.0 / 1.3));  
    
}
vec2 get_transformed_uv(in vec4 U, in vec4 V, in vec4 R1, in vec4 R2, in vec4 S) {

    mat4 rs;
    rs[0] = vec4(U.x, U.y, U.z, 0.0);
    rs[1] = vec4(0.0, 1.0, 0.0, 0.0);
    rs[2] = vec4(V.x, V.y, V.z, 0.0);
    rs[3] = vec4(0.0, 0.0, 0.0, 1.0);

    vec4 vt = rs * vec4(fs_in.UV.x*100.0, 0.0, fs_in.UV.y*100.0, 1.0);   

    vec2 out_uv = vec2(-vt.x, -vt.z+0.5);
    out_uv += vec2(R1.x, R1.y);
    return out_uv;
    }
/*===========================================================*/
/*===========================================================*/
/*===========================================================*/

void main(void)
{
    //==============================================================
    vec4 t1, t2, t3, t4, t5, t6, t7, t8;
    vec4 n1, n2, n3, n4, n5, n6, n7, n8;
    vec2 tuv1, tuv2, tuv3, tuv4, tuv5, tuv6, tuv7, tuv8; 

    vec2 MixLevel1, MixLevel2, MixLevel3, MixLevel4;
    vec2 mix_coords;
    //==============================================================

    mix_coords = fs_in.UV;
    mix_coords.x = 1.0 - mix_coords.x;

    vec4 global = texture(global_AM, fs_in.Global_UV);
    // create UV projections
    tuv1 = get_transformed_uv(U1, V1, r1_1, r1_1, s1); 
    tuv2 = get_transformed_uv(U2, V2, r1_2, r1_2, s2);

    tuv3 = get_transformed_uv(U3, V3, r1_3, r1_3, s3); 
    tuv4 = get_transformed_uv(U4, V4, r1_4, r1_4, s4);

    tuv5 = get_transformed_uv(U5, V5, r1_5, r1_5, s5); 
    tuv6 = get_transformed_uv(U6, V6, r1_6, r1_6, s6);

    tuv7 = get_transformed_uv(U7, V7, r1_7, r1_7, s7);
    tuv8 = get_transformed_uv(U8, V8, r1_8, r1_8, s8);

    // Get AM maps 
    t1 = textureNoTile(layer_1T1, tuv1, r1_1.z);
    t2 = textureNoTile(layer_1T2, tuv2, r1_2.z);

    t3 = textureNoTile(layer_2T1, tuv3, r1_3.z);
    t4 = textureNoTile(layer_2T2, tuv4, r1_4.z);

    t5 = textureNoTile(layer_3T1, tuv5, r1_5.z);
    t6 = textureNoTile(layer_3T2, tuv6, r1_6.z);

    t7 = textureNoTile(layer_4T1, tuv7, r1_7.z);
    t8 = textureNoTile(layer_4T2, tuv8, r1_8.z);

    // Height is in red channel of the normal maps.
    // Ambient occlusion is in the Blue channel.
    // Green and Alpha are normal values.

    n1 = textureNoTile(n_layer_1T1, tuv1, r1_1.z);
    n2 = textureNoTile(n_layer_1T2, tuv2, r1_2.z);
    n3 = textureNoTile(n_layer_2T1, tuv3, r1_3.z);
    n4 = textureNoTile(n_layer_2T2, tuv4, r1_4.z);
    n5 = textureNoTile(n_layer_3T1, tuv5, r1_5.z);
    n6 = textureNoTile(n_layer_3T2, tuv6, r1_6.z);
    n7 = textureNoTile(n_layer_4T1, tuv7, r1_7.z);
    n8 = textureNoTile(n_layer_4T2, tuv8, r1_8.z);

    // get the ambient occlusion
    t1.rgb *= n1.b;
    t2.rgb *= n2.b;
    t3.rgb *= n3.b;
    t4.rgb *= n4.b;
    t5.rgb *= n5.b;
    t6.rgb *= n6.b;
    t7.rgb *= n7.b;
    t8.rgb *= n8.b;
   
    //Get the mix values from the mix textures 1-4 and move to vec2. 
    MixLevel1.rg = texture(mixtexture1, mix_coords.xy).ag;
    MixLevel2.rg = texture(mixtexture2, mix_coords.xy).ag;
    MixLevel3.rg = texture(mixtexture3, mix_coords.xy).ag;
    MixLevel4.rg = texture(mixtexture4, mix_coords.xy).ag;

    //months of work to figure this out!
    MixLevel1.r *= n1.a + t1.a;
    MixLevel1.g *= n2.a + t2.a;
    MixLevel2.r *= n3.a + t3.a;
    MixLevel2.g *= n4.a + t4.a;
    MixLevel3.r *= n5.a + t5.a;
    MixLevel3.g *= n6.a + t6.a;
    MixLevel4.r *= n7.a + t7.a;
    MixLevel4.g *= n8.a + t8.a;
    
    MixLevel1 *= MixLevel1;
 
    MixLevel2 *= MixLevel2;

    MixLevel3 *= MixLevel3;

    MixLevel4 *= MixLevel4;

// Height mix clamp

    float offs = 0.0;
    float offe = 0.2;

    MixLevel1.r = smoothstep(offs, offe, MixLevel1.r);
    MixLevel1.g = smoothstep(offs, offe, MixLevel1.g);
    MixLevel2.r = smoothstep(offs, offe, MixLevel2.r);
    MixLevel2.g = smoothstep(offs, offe, MixLevel2.g);
    MixLevel3.r = smoothstep(offs, offe, MixLevel3.r);
    MixLevel3.g = smoothstep(offs, offe, MixLevel3.g);
    MixLevel4.r = smoothstep(offs, offe, MixLevel4.r);
    MixLevel4.g = smoothstep(offs, offe, MixLevel4.g);

    vec4 m4 = blend(t7, MixLevel4.r, t8 , MixLevel4.g);

    vec4 m3 = blend(t5, MixLevel3.r, t6 , MixLevel3.g);

    vec4 m2 = blend(t3, MixLevel2.r, t4 , MixLevel2.g);

    vec4 m1 = blend(t1, MixLevel1.r, t2 , MixLevel1.g);


    vec4 m5 = blend(m3, MixLevel3.r+MixLevel3.g, m4, MixLevel4.r+MixLevel4.g);

    vec4 m6 = blend(m1 ,MixLevel1.r+MixLevel1.g, m2, MixLevel2.r+MixLevel2.g);

    vec4 m7 = blend(m5, MixLevel3.r+MixLevel3.g+MixLevel4.r+MixLevel4.g, m6, MixLevel1.r+MixLevel1.g+ MixLevel2.r+MixLevel2.g);

    vec4 base = m7;

    base.rgb = ColorCorrect(base.rgb);
    //-------------------------------------------------------------
    // normals

    m4 = blend_normal(n7, n8, t7, MixLevel4.r, t8 , MixLevel4.g);

    m3 = blend_normal(n5, n6, t5, MixLevel3.r, t6 , MixLevel3.g);

    m2 = blend_normal(n3, n4, t3, MixLevel2.r, t4 , MixLevel2.g);

    m1 = blend_normal(n1, n2, t1, MixLevel1.r, t2 , MixLevel1.g);


    m5 = blend(m3, MixLevel3.r+MixLevel3.g, m4, MixLevel4.r+MixLevel4.g);

    m6 = blend(m1, MixLevel1.r+MixLevel1.g, m2, MixLevel2.r+MixLevel2.g);

    m7 = blend(m5, MixLevel3.r+MixLevel3.g+MixLevel4.r+MixLevel4.g, m6, MixLevel1.r+MixLevel1.g+ MixLevel2.r+MixLevel2.g);


     float specular = m7.r;

     gNormal.xyz = normalize(convertNormal(m7).xyz);

    gGMF = vec4(0.1, specular, 128.0/255.0, 0.0);
    //vec3 shad = vec3( texture( shadow, vec3(fs_in.UV, float(map_id)) ).r );
    gColor.rgb = base.rgb;
    //gColor.rgb *= shad;
    // global.a is used for wetness on the map.
    gColor.a = global.a*0.8;

}
