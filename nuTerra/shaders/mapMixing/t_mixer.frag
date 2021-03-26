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

//layout(binding = 23) uniform sampler2DArray shadow;

uniform int map_id;
uniform vec3 waterColor;
uniform float waterAlpha;

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

vec4 crop( sampler2DArray samp, in vec2 uv , in float layer)
{
    vec2 cropped = fract(uv) * vec2(0.875, 0.875) + vec2(0.0625, 0.0625);
    return texture(samp,vec3(cropped, layer));
}
/*===========================================================*/

// Converion from AG map to RGB vector.
vec4 convertNormal(vec4 norm){
    vec3 n;
    n.xy = clamp(norm.ag*2.0-1.0, -1.0 ,1.0);;
    float dp = min(dot(n.xy, n.xy),1.0);
    n.z = clamp(sqrt(-dp+1.0),-1.0,1.0);
    n = normalize(n);
    n.x = -n.x;
    return vec4(n,0.0);
}

vec3 ColorCorrect(in vec3 valin){  
    // Gamma correction 
   return  pow(valin.rgb, vec3(1.0 / 1.5));  
}

vec2 get_transformed_uv(in vec4 U, in vec4 V, in vec4 S) {
    vec4 vt = vec4(fs_in.UV.x*100, 0.0, -fs_in.UV.y*100.0, 1.0);
    vec2 out_uv;
    out_uv = vec2(-dot(U,vt), dot(V,vt));
    out_uv.xy += vec2(-S.x, S.y);
    return out_uv;
    }
/*===========================================================*/
/*===========================================================*/
/*===========================================================*/

void main(void)
{
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
    // create UV projections
    tuv1 = get_transformed_uv(U1, V1, s1); 
    tuv2 = get_transformed_uv(U2, V2, s2);
    tuv3 = get_transformed_uv(U3, V3, s3); 
    tuv4 = get_transformed_uv(U4, V4, s4);
    tuv5 = get_transformed_uv(U5, V5, s5); 
    tuv6 = get_transformed_uv(U6, V6, s6);
    tuv7 = get_transformed_uv(U7, V7, s7);
    tuv8 = get_transformed_uv(U8, V8, s8);

    // Get AM maps 
    t1 = crop(at1, tuv1, 0.0);
    t2 = crop(at2, tuv2, 0.0);
    t3 = crop(at3, tuv3, 0.0);
    t4 = crop(at4, tuv4, 0.0);
    t5 = crop(at5, tuv5, 0.0);
    t6 = crop(at6, tuv6, 0.0);
    t7 = crop(at7, tuv7, 0.0);
    t8 = crop(at8, tuv8, 0.0);

    mt1 = crop(at1, tuv1*0.125, 2.0);
    mt2 = crop(at2, tuv2*0.125, 2.0);
    mt3 = crop(at3, tuv3*0.125, 2.0);
    mt4 = crop(at4, tuv4*0.125, 2.0);
    mt5 = crop(at5, tuv5*0.125, 2.0);
    mt6 = crop(at6, tuv6*0.125, 2.0);
    mt7 = crop(at7, tuv7*0.125, 2.0);
    mt8 = crop(at8, tuv8*0.125, 2.0);

    // Height is in red channel of the normal maps.
    // Ambient occlusion is in the Blue channel.
    // Green and Alpha are normal values.

    n1 = crop(at1, tuv1, 1.0);
    n2 = crop(at2, tuv2, 1.0);
    n3 = crop(at3, tuv3, 1.0);
    n4 = crop(at4, tuv4, 1.0);
    n5 = crop(at5, tuv5, 1.0);
    n6 = crop(at6, tuv6, 1.0);
    n7 = crop(at7, tuv7, 1.0);
    n8 = crop(at8, tuv8, 1.0);

    mn1 = crop(at1, tuv1*0.125, 3.0);
    mn2 = crop(at2, tuv2*0.125, 3.0);
    mn3 = crop(at3, tuv3*0.125, 3.0);
    mn4 = crop(at4, tuv4*0.125, 3.0);
    mn5 = crop(at5, tuv5*0.125, 3.0);
    mn6 = crop(at6, tuv6*0.125, 3.0);
    mn7 = crop(at7, tuv7*0.125, 3.0);
    mn8 = crop(at8, tuv8*0.125, 3.0);

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
    base.rgb = mix(base.rgb,waterColor,waterAlpha*global.a);
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
    //gColor.rgb *= shad;
    // global.a is used for wetness on the map.

    gColor.rgb = base.rgb * 0.95;
    gColor.a = global.a*0.8;

}
