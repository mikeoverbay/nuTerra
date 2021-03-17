#version 450 core

#extension GL_ARB_shading_language_include : require
#include "common.h" //! #include "../common.h"

layout(early_fragment_tests) in;

layout (location = 0) out vec4 gColor;
layout (location = 1) out vec3 gNormal;
layout (location = 2) out vec4 gGMF;
layout (location = 3) out vec3 gPosition;
layout (location = 4) out uint gPick;

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

layout(binding = 1 ) uniform sampler2D samp_t1;
layout(binding = 2 ) uniform sampler2D samp_t2;
layout(binding = 3 ) uniform sampler2D samp_t3;
layout(binding = 4 ) uniform sampler2D samp_t4;

layout(binding = 5 ) uniform sampler2D samp_t5;
layout(binding = 6 ) uniform sampler2D samp_t6;
layout(binding = 7 ) uniform sampler2D samp_t7;
layout(binding = 8 ) uniform sampler2D samp_t8;


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
    vec2 UV;
    vec2 Global_UV;
    float ln;
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
   return  pow(valin.rgb, vec3(1.0 / 1.5));  
    
}

/*===========================================================*/
//http://www.iquilezles.org/www/articles/texturerepetition/texturerepetition.htm
float sum( vec4 v ) {
    return v.x+v.y+v.z;
    }

vec4 textureNoTile( sampler2D samp, in vec2 uv ,in float flag, in out float b)
{

   
   //if (flag > 0.0 ){
   if (true){

        vec2  dx_vtc        = dFdx(uv*1024.0);
        vec2  dy_vtc        = dFdy(uv*1024.0);
        float delta_max_sqr = max(dot(dx_vtc, dx_vtc), dot(dy_vtc, dy_vtc));

        float mipLevel = 0.5 * log2(delta_max_sqr);

        vec2 cropped = (uv);// * vec2(0.875, 0.875) + vec2(0.0625, 0.0625);

        b =0.0;
        if (cropped.x < 0.065 ) b = 1.0;
        if (cropped.x > 0.935 ) b = 1.0;
        if (cropped.y < 0.065 ) b = 1.0;
        if (cropped.y > 0.935 ) b = 1.0;

        return textureLod( samp, cropped, max(0.0, mipLevel) );

        }

    vec2 cropped = (uv);// * vec2(0.875, 0.875) + vec2(0.0625, 0.0625);

    b =0.0;
    if (cropped.x < 0.065 ) b = 1.0;
    if (cropped.x > 0.935 ) b = 1.0;
    if (cropped.y < 0.065 ) b = 1.0;
    if (cropped.y > 0.935 ) b = 1.0;

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
    float s = smoothstep(0.2,0.8,f-0.1 * sum(cola-colb) );
    return mix( cola, colb, s);
    }

vec2 get_transformed_uv(in vec4 U, in vec4 V, in vec4 R1, in vec4 R2, in vec4 S) {

    float pad = 0.0625/3.0;
    vec2 out_uv;

    float xloc = 0.0;
    float scale = 1.0/3.0;

    vec4 vt = vec4(fs_in.UV.x, 0.0, fs_in.UV.y, 1.0);   
    out_uv = vec2(dot(U,vt*-100.0), dot(V,vt*100.0));
    
    vec2 vtx = fract(out_uv);
    vtx.x = xloc * scale + (vtx.x/3.0);

    vtx = vtx * vec2(0.875, 0.875) + vec2(pad, 0.0625);

    
    out_uv = vtx * vec2(1.0,1.0);// + vec2(0.5,0.5);
    //out_uv.xy += vec2(-S.x, -S.y);

    return out_uv;

    }

/*===========================================================*/
/*===========================================================*/
/*===========================================================*/
/*===========================================================*/
/*===========================================================*/

void main(void)
{
    //==============================================================
    // texture outline stuff
    float B1, B2, B3, B4, B5, B6, B7, B8;
    vec4 color_1 = vec4(1.0,  1.0,  0.0,  0.0);
    vec4 color_2 = vec4(0.0,  1.0,  0.0,  0.0);
    vec4 color_3 = vec4(0.0,  0.0,  1.0,  0.0);
    vec4 color_4 = vec4(1.0,  1.0,  0.0,  0.0);
    vec4 color_5 = vec4(1.0,  0.0,  1.0,  0.0);
    vec4 color_6 = vec4(1.0,  0.65, 0.0,  0.0);
    vec4 color_7 = vec4(1.0,  0.49, 0.31, 0.0);
    vec4 color_8 = vec4(0.5,  0.5,  0.5,  0.0);
    //==============================================================

    vec4 t1, t2, t3, t4, t5, t6, t7, t8;
    vec4 n1, n2, n3, n4, n5, n6, n7, n8;
    vec4 c1, c2, c3, c4, c5, c6, c7, c8;
    vec2 tuv1, tuv2, tuv3, tuv4, tuv5, tuv6, tuv7, tuv8; 

    vec2 MixLevel1, MixLevel2, MixLevel3, MixLevel4;
    vec2 mix_coords;
    //==============================================================

    mix_coords = fs_in.UV;
    mix_coords.x = 1.0 - mix_coords.x;

    vec4 global = texture(global_AM, fs_in.Global_UV);
    //-------------------------------------------------------

    // create UV projections
    tuv1 = get_transformed_uv(U1, V1, r1_1, r2_1, s1); 
    tuv2 = get_transformed_uv(U2, V2, r1_2, r2_2, s2);

    tuv3 = get_transformed_uv(U3, V3, r1_3, r2_3, s3); 
    tuv4 = get_transformed_uv(U4, V4, r1_4, r2_4, s4);

    tuv5 = get_transformed_uv(U5, V5, r1_5, r2_5, s5); 
    tuv6 = get_transformed_uv(U6, V6, r1_6, r2_6, s6);

    tuv7 = get_transformed_uv(U7, V7, r1_7, r2_7, s7);
    tuv8 = get_transformed_uv(U8, V8, r1_8, r2_8, s8);


    // Get AM maps,crop, detilize and set Test outline blend flag
    t1 = textureNoTile(samp_t1, tuv1, r2_1.z, B1);
    t2 = textureNoTile(samp_t2, tuv2, r2_2.z, B2);

    t3 = textureNoTile(samp_t3, tuv3, r2_3.z, B3);
    t4 = textureNoTile(samp_t4, tuv4, r2_4.z, B4);

    t5 = textureNoTile(samp_t5, tuv5, r2_5.z, B5);
    t6 = textureNoTile(samp_t6, tuv6, r2_6.z, B6);

    t7 = textureNoTile(samp_t7, tuv7, r2_7.z, B7);
    t8 = textureNoTile(samp_t8, tuv8, r2_8.z, B8);
    
    //t6= vec4(0.0);

    // Height is in red channel of the normal maps.
    // Ambient occlusion is in the Blue channel.
    // Green and Alpha are normal values.
    tuv1.x +=0.3333333333;
    tuv2.x +=0.3333333333;
    tuv3.x +=0.3333333333;
    tuv4.x +=0.3333333333;
    tuv5.x +=0.3333333333;
    tuv6.x +=0.3333333333;
    tuv7.x +=0.3333333333;
    tuv8.x +=0.3333333333;

    n1 = textureNoTile(samp_t1, tuv1, r1_1.z, B1);
    n2 = textureNoTile(samp_t2, tuv2, r1_2.z, B2);
    n3 = textureNoTile(samp_t3, tuv3, r1_3.z, B3);
    n4 = textureNoTile(samp_t4, tuv4, r1_4.z, B4);
    n5 = textureNoTile(samp_t5, tuv5, r1_5.z, B5);
    n6 = textureNoTile(samp_t6, tuv6, r1_6.z, B6);
    n7 = textureNoTile(samp_t7, tuv7, r1_7.z, B7);
    n8 = textureNoTile(samp_t8, tuv8, r1_8.z, B8);

    tuv1.x +=0.3333333333;
    tuv2.x +=0.3333333333;
    tuv3.x +=0.3333333333;
    tuv4.x +=0.3333333333;
    tuv5.x +=0.3333333333;
    tuv6.x +=0.3333333333;
    tuv7.x +=0.3333333333;
    tuv8.x +=0.3333333333;

    c1 = textureNoTile(samp_t1, tuv1, r1_1.z, B1);
    c2 = textureNoTile(samp_t2, tuv2, r1_2.z, B2);
    c3 = textureNoTile(samp_t3, tuv3, r1_3.z, B3);
    c4 = textureNoTile(samp_t4, tuv4, r1_4.z, B4);
    c5 = textureNoTile(samp_t5, tuv5, r1_5.z, B5);
    c6 = textureNoTile(samp_t6, tuv6, r1_6.z, B6);
    c7 = textureNoTile(samp_t7, tuv7, r1_7.z, B7);
    c8 = textureNoTile(samp_t8, tuv8, r1_8.z, B8);

    // get the ambient occlusion

    t1.rgb *= n1.b;
    t2.rgb *= n2.b;
    t3.rgb *= n3.b;
    t4.rgb *= n4.b;
    t5.rgb *= n5.b;
    t6.rgb *= n6.b;
    t7.rgb *= n7.b;
    t8.rgb *= n8.b;
 
    t1 = mix(t1,c1,s1);
    t2 = mix(t2,c2,s2);
    t3 = mix(t3,c3,s3);
    t3 = mix(t4,c4,s4);
    t4 = mix(t5,c5,s5);
    t5 = mix(t6,c6,s6);
    t7 = mix(t7,c7,s7);
    t8 = mix(t8,c8,s8);
 
    t1.rgb = mix(t1.rgb,c1.rgb,1.0);
    t2.rgb = mix(t2.rgb,c2.rgb,1.0);
    t3.rgb = mix(t3.rgb,c3.rgb,1.0);
    t3.rgb = mix(t4.rgb,c4.rgb,1.0);
    t4.rgb = mix(t5.rgb,c5.rgb,1.0);
    t5.rgb = mix(t6.rgb,c6.rgb,1.0);
    t7.rgb = mix(t7.rgb,c7.rgb,1.0);
    t8.rgb = mix(t8.rgb,c8.rgb,1.0);
    //Get the mix values from the mix textures 1-4 and move to vec2. 
    MixLevel1.rg = texture(mixtexture1, mix_coords.xy).ag;
    MixLevel2.rg = texture(mixtexture2, mix_coords.xy).ag;
    MixLevel3.rg = texture(mixtexture3, mix_coords.xy).ag;
    MixLevel4.rg = texture(mixtexture4, mix_coords.xy).ag;

    //months of work to figure this out!

    MixLevel1.r *= t1.a+r1_1.x;
    MixLevel1.g *= t2.a+r1_2.x;
    MixLevel2.r *= t3.a+r1_3.x;
    MixLevel2.g *= t4.a+r1_4.x;
    MixLevel3.r *= t5.a+r1_5.x;
    MixLevel3.g *= t6.a+r1_6.x;
    MixLevel4.r *= t7.a+r1_7.x;
    MixLevel4.g *= t8.a+r1_8.x;

    t1.a += n1.r+r1_1.y;
    t2.a += n2.r+r1_2.y;
    t3.a += n3.r+r1_3.y;
    t4.a += n4.r+r1_4.y;
    t5.a += n5.r+r1_5.y;
    t6.a += n6.r+r1_6.y;
    t7.a += n7.r+r1_7.y;
    t8.a += n8.r+r1_8.y;
   
    MixLevel1 += MixLevel1;
    MixLevel1 += MixLevel1;
    MixLevel1 += MixLevel1;
 
    MixLevel2 += MixLevel2;
    MixLevel2 += MixLevel2;
    MixLevel2 += MixLevel2;

    MixLevel3 += MixLevel3;
    MixLevel3 += MixLevel3;
    MixLevel3 += MixLevel3;

    MixLevel4 += MixLevel4;
    MixLevel4 += MixLevel4;
    MixLevel4 += MixLevel4;

    float pow_s = 4.0;
    MixLevel1 = pow(MixLevel1,vec2(pow_s));
    MixLevel2 = pow(MixLevel2,vec2(pow_s));
    MixLevel3 = pow(MixLevel3,vec2(pow_s));
    MixLevel4 = pow(MixLevel4,vec2(pow_s));


    vec4 m4 = blend(t7, MixLevel4.r, t8 , MixLevel4.g);

    vec4 m3 = blend(t5, MixLevel3.r, t6 , MixLevel3.g);

    vec4 m2 = blend(t3, MixLevel2.r, t4 , MixLevel2.g);

    vec4 m1 = blend(t1, MixLevel1.r, t2 , MixLevel1.g);


    vec4 m5 = blend(m3, MixLevel3.r+MixLevel3.g, m4, MixLevel4.r+MixLevel4.g);

    vec4 m6 = blend(m1 ,MixLevel1.r+MixLevel1.g, m2, MixLevel2.r+MixLevel2.g);

    vec4 m7 = blend(m5, MixLevel3.r+MixLevel3.g+MixLevel4.r+MixLevel4.g, m6, MixLevel1.r+MixLevel1.g+ MixLevel2.r+MixLevel2.g);

    vec4 base = m7;
    base.rgb = ColorCorrect(base.rgb);
   
    // Texture outlines if test = 1.0;

    base = mix(base, base + color_1, B1 * test * MixLevel1.r);
    base = mix(base, base + color_2, B2 * test * MixLevel1.g);
    base = mix(base, base + color_3, B3 * test * MixLevel2.r);
    base = mix(base, base + color_4, B4 * test * MixLevel2.g);
    base = mix(base, base + color_5, B5 * test * MixLevel3.r);
    base = mix(base, base + color_6, B6 * test * MixLevel3.g);
    base = mix(base, base + color_7, B7 * test * MixLevel4.r);
    base = mix(base, base + color_8, B8 * test * MixLevel4.g);

    //-------------------------------------------------------------
    // normals

    m4 = blend_normal(n7, n8, t7, MixLevel4.r, t8 , MixLevel4.g);

    m3 = blend_normal(n5, n6, t5, MixLevel3.r, t6 , MixLevel3.g);

    m2 = blend_normal(n3, n4, t3, MixLevel2.r, t4 , MixLevel2.g);

    m1 = blend_normal(n1, n2, t1, MixLevel1.r, t2 , MixLevel1.g);


    m5 = blend(m3, MixLevel3.r+MixLevel3.g, m4, MixLevel4.r+MixLevel4.g);

    m6 = blend(m1, MixLevel1.r+MixLevel1.g, m2, MixLevel2.r+MixLevel2.g);

    m7 = blend(m5, MixLevel3.r+MixLevel3.g+MixLevel4.r+MixLevel4.g, m6, MixLevel1.r+MixLevel1.g+ MixLevel2.r+MixLevel2.g);

    vec4 out_n = m7;
    float specular = out_n.r;

    out_n = convertNormal(out_n);
    out_n.xyz = fs_in.TBN * out_n.xyz;

    // global.a is used for wetness on the map.
    // I am not sure this should be applied to the AM color.
    //base.rgb = mix(base.rgb ,waterColor, global.a * waterAlpha);
    
    // Get pre=mixed map textures
    vec4 ArrayTextureC = texture(textArrayC, vec3(fs_in.UV, map_id) );
    vec4 ArrayTextureN = texture(textArrayN, vec3(fs_in.UV, map_id) );
    vec4 ArrayTextureG = texture(textArrayG, vec3(fs_in.UV, map_id) );

    ArrayTextureN.xyz = fs_in.TBN * ArrayTextureN.xyz;

    // This blends the pre-mixed maps over distance.
    //base = mix(ArrayTextureC, base, fs_in.ln);
    //out_n = mix(ArrayTextureN, out_n, fs_in.ln) ;

    //there are no metal values for the terrain so we hard code 0.1;
    // specular is in the red channel of the normal maps;
    vec4 gmm_out = vec4(0.1, specular, 128.0/255.0, 0.0);
    gGMF = mix(ArrayTextureG, gmm_out, fs_in.ln);

    //gColor = gColor* 0.001 + r1_8;

    gColor.rgb = base.rgb;//*vec3(0.001) + normalize(out_n.xyz);
    gColor.a = global.a*0.8;

    gNormal.xyz = normalize(out_n.xyz);

    gPosition = fs_in.worldPosition;
    gPick = 0;
}
