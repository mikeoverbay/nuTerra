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

layout(binding = 1 ) uniform sampler2DArray at1;
layout(binding = 2 ) uniform sampler2DArray at2;
layout(binding = 3 ) uniform sampler2DArray at3;
layout(binding = 4 ) uniform sampler2DArray at4;

layout(binding = 5 ) uniform sampler2DArray at5;
layout(binding = 6 ) uniform sampler2DArray at6;
layout(binding = 7 ) uniform sampler2DArray at7;
layout(binding = 8 ) uniform sampler2DArray at8;


layout(binding = 17) uniform sampler2D mixtexture1;
layout(binding = 18) uniform sampler2D mixtexture2;
layout(binding = 19) uniform sampler2D mixtexture3;
layout(binding = 20) uniform sampler2D mixtexture4;


layout(binding = 21) uniform sampler2D global_AM;

layout(binding = 22) uniform sampler2DArray textArrayC;
layout(binding = 23) uniform sampler2DArray textArrayN;
layout(binding = 24) uniform sampler2DArray textArrayG;

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
   return  pow(valin.rgb, vec3(1.0 / 1.3));
}

/*===========================================================*/

vec4 crop( sampler2DArray samp, in vec2 uv , in float layer, in out float b)
{
    vec2  dx_vtc        = dFdx(uv*1024.0);
    vec2  dy_vtc        = dFdy(uv*1024.0);
    float delta_max_sqr = max(dot(dx_vtc, dx_vtc), dot(dy_vtc, dy_vtc));
    float mipLevel = 0.5 * log2(delta_max_sqr);
    vec2 cropped = fract(uv) * vec2(0.875, 0.875) + vec2(0.0625, 0.0625);
    b =0.0;
    if (cropped.x < 0.065 ) b = 1.0;
    if (cropped.x > 0.935 ) b = 1.0;
    if (cropped.y < 0.065 ) b = 1.0;
    if (cropped.y > 0.935 ) b = 1.0;
    return textureLod( samp, vec3(cropped, layer), mipLevel);
    }

vec4 crop2( sampler2DArray samp, in vec2 uv , in float layer)
{
    vec2  dx_vtc        = dFdx(uv*1024.0);
    vec2  dy_vtc        = dFdy(uv*1024.0);
    float delta_max_sqr = max(dot(dx_vtc, dx_vtc), dot(dy_vtc, dy_vtc));
    float mipLevel = 0.5 * log2(delta_max_sqr);
    vec2 cropped = fract(uv) * vec2(0.875, 0.875) + vec2(0.0625, 0.0625);
    return textureLod( samp, vec3(cropped, layer), mipLevel);
    }


vec2 get_transformed_uv(in vec4 U, in vec4 V, in vec4 R1, in vec4 R2, in vec4 S) {
    vec4 vt = vec4(fs_in.UV.x*100, 0.0, -fs_in.UV.y*100.0, 1.0);   
    vec2 out_uv;
    out_uv = vec2(-dot(U,vt), dot(V,vt));
    out_uv.xy += vec2(-S.x, S.y);
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

    vec4 mt1, mt2, mt3, mt4, mt5, mt6, mt7, mt8;
    vec4 mn1, mn2, mn3, mn4, mn5, mn6, mn7, mn8;
    vec4 t1, t2, t3, t4, t5, t6, t7, t8;
    vec4 n1, n2, n3, n4, n5, n6, n7, n8;
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

    t1 = crop(at1, tuv1, 0.0, B1);
    t2 = crop(at2, tuv2, 0.0, B2);
    t3 = crop(at3, tuv3, 0.0, B3);
    t4 = crop(at4, tuv4, 0.0, B4);
    t5 = crop(at5, tuv5, 0.0, B5);
    t6 = crop(at6, tuv6, 0.0, B6);
    t7 = crop(at7, tuv7, 0.0, B7);
    t8 = crop(at8, tuv8, 0.0, B8);
    
    mt1 = crop2(at1, tuv1*0.125, 2.0);
    mt2 = crop2(at2, tuv2*0.125, 2.0);
    mt3 = crop2(at3, tuv3*0.125, 2.0);
    mt4 = crop2(at4, tuv4*0.125, 2.0);
    mt5 = crop2(at5, tuv5*0.125, 2.0);
    mt6 = crop2(at6, tuv6*0.125, 2.0);
    mt7 = crop2(at7, tuv7*0.125, 2.0);
    mt8 = crop2(at8, tuv8*0.125, 2.0);

    // Height is in red channel of the normal maps.
    // Ambient occlusion is in the Blue channel.
    // Green and Alpha are normal values.

    n1 = crop2(at1, tuv1, 1.0);
    n2 = crop2(at2, tuv2, 1.0);
    n3 = crop2(at3, tuv3, 1.0);
    n4 = crop2(at4, tuv4, 1.0);
    n5 = crop2(at5, tuv5, 1.0);
    n6 = crop2(at6, tuv6, 1.0);
    n7 = crop2(at7, tuv7, 1.0);
    n8 = crop2(at8, tuv8, 1.0);

    mn1 = crop2(at1, tuv1*0.125, 3.0);
    mn2 = crop2(at2, tuv2*0.125, 3.0);
    mn3 = crop2(at3, tuv3*0.125, 3.0);
    mn4 = crop2(at4, tuv4*0.125, 3.0);
    mn5 = crop2(at5, tuv5*0.125, 3.0);
    mn6 = crop2(at6, tuv6*0.125, 3.0);
    mn7 = crop2(at7, tuv7*0.125, 3.0);
    mn8 = crop2(at8, tuv8*0.125, 3.0);

    // get the ambient occlusion
    t1.rgb *= n1.b;
    t2.rgb *= n2.b;
    t3.rgb *= n3.b;
    t4.rgb *= n4.b;
    t5.rgb *= n5.b;
    t6.rgb *= n6.b;
    t7.rgb *= n7.b;
    t8.rgb *= n8.b;

    mt1.rgb *= mn1.b;
    mt2.rgb *= mn2.b;
    mt3.rgb *= mn3.b;
    mt4.rgb *= mn4.b;
    mt5.rgb *= mn5.b;
    mt6.rgb *= mn6.b;
    mt7.rgb *= mn7.b;
    mt8.rgb *= mn8.b;

    //mix macro

    t1.rgb = t1.rgb* min(r2_1.x,1.0) + mt1.rgb*(r2_1.y+1.0);
    t2.rgb = t2.rgb* min(r2_2.x,1.0) + mt2.rgb*(r2_2.y+1.0);
    t3.rgb = t3.rgb* min(r2_3.x,1.0) + mt3.rgb*(r2_3.y+1.0);
    t4.rgb = t4.rgb* min(r2_4.x,1.0) + mt4.rgb*(r2_4.y+1.0);
    t5.rgb = t5.rgb* min(r2_5.x,1.0) + mt5.rgb*(r2_5.y+1.0);
    t6.rgb = t6.rgb* min(r2_6.x,1.0) + mt6.rgb*(r2_6.y+1.0);
    t7.rgb = t7.rgb* min(r2_7.x,1.0) + mt7.rgb*(r2_7.y+1.0);
    t8.rgb = t8.rgb* min(r2_8.x,1.0) + mt8.rgb*(r2_8.y+1.0);

    n1.rgb = n1.rgb* min(r2_1.x,1.0) + mn1.rgb*(r2_1.y+1.0);
    n2.rgb = n2.rgb* min(r2_2.x,1.0) + mn2.rgb*(r2_2.y+1.0);
    n3.rgb = n3.rgb* min(r2_3.x,1.0) + mn3.rgb*(r2_3.y+1.0);
    n4.rgb = n4.rgb* min(r2_4.x,1.0) + mn4.rgb*(r2_4.y+1.0);
    n5.rgb = n5.rgb* min(r2_5.x,1.0) + mn5.rgb*(r2_5.y+1.0);
    n6.rgb = n6.rgb* min(r2_6.x,1.0) + mn6.rgb*(r2_6.y+1.0);
    n7.rgb = n7.rgb* min(r2_7.x,1.0) + mn7.rgb*(r2_7.y+1.0);
    n8.rgb = n8.rgb* min(r2_8.x,1.0) + mn8.rgb*(r2_8.y+1.0);

   
    //Get the mix values from the mix textures 1-4 and move to vec2. 
    MixLevel1.rg = texture(mixtexture1, mix_coords.xy).ag;
    MixLevel2.rg = texture(mixtexture2, mix_coords.xy).ag;
    MixLevel3.rg = texture(mixtexture3, mix_coords.xy).ag;
    MixLevel4.rg = texture(mixtexture4, mix_coords.xy).ag;

    // This is experimental code from shader assembly.
    // It is not used yet.
    /*
    vec4 r23,r24;
    r23.rg = texture(mixtexture1, mix_coords.xy).ag;
    r23.ba = texture(mixtexture2, mix_coords.xy).ag;
    r24.rg = texture(mixtexture3, mix_coords.xy).ag;
    r24.ba = texture(mixtexture4, mix_coords.xy).ag;
    
    vec4 r8;
    r8.x = mt1.a;
    r8.y = mt2.a;
    r8.z = mt3.a;
    r8.w = mt4.a;

    vec4 r3;
    r3.x = t1.a;
    r3.y = t2.a;
    r3.z = t3.a;
    r3.w = t4.a;
    vec4 r14;
    r14.x = t5.a;
    r14.y = t6.a;
    r14.z = t7.a;
    r14.w = t8.a;

    vec4 r10 = -r3 + r8;
    vec4 r11 = max(r8, vec4(0.003922) );


    float d1 = dot(r23,vec4(1.0));
    float d2 = dot(r24,vec4(1.0));
    d1 += d2;

    vec4 mx1_ = r23/vec4(d1);
    vec4 mx2_ = r24/vec4(d1);
    
    vec4 r4;
    r4.w = dot(r14,r24);
    vec4 r25 = r23 / r4.w;
    vec4 r26 = r24 / r4.w;

    r4.w = dot(r14,r24);
    r24 = max(r14, vec4(0.03922));


    r3.x = dot(r3,r23);
    r3.x += r4.w;

   // r23 = r21 * r25;

   */
    //months of work to figure this out!
    MixLevel1.r *= t1.a;
    MixLevel1.g *= t2.a;
    MixLevel2.r *= t3.a;
    MixLevel2.g *= t4.a;
    MixLevel3.r *= t5.a;
    MixLevel3.g *= t6.a;
    MixLevel4.r *= t7.a;
    MixLevel4.g *= t8.a;

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
    base = mix(ArrayTextureC, base, fs_in.ln);
    out_n = mix(ArrayTextureN, out_n, fs_in.ln) ;

    //there are no metal values for the terrain so we hard code 0.1;
    // specular is in the red channel of the normal maps;
    vec4 gmm_out = vec4(0.1, specular, 128.0/255.0, 0.0);
    gGMF = mix(ArrayTextureG, gmm_out, fs_in.ln);

    //gColor = gColor* 0.001 + r1_8;
    t1 = texture(at1,vec3(fs_in.UV,1.0));
    gColor.rgb = base.rgb;//*0.01+t1.rgb;
    gColor.a = global.a*0.8;

    gNormal.xyz = normalize(out_n.xyz);

    gPosition = fs_in.worldPosition;
    gPick = 0;
}
