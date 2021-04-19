#version 450 core

#extension GL_ARB_shading_language_include : require

#define USE_COMMON_PROPERTIES_UBO
#include "common.h" //! #include "../common.h"

layout (location = 0) out vec4 gColor;
layout (location = 1) out vec3 gNormal;
layout (location = 2) out vec4 gGMF;

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

in VS_OUT {
    vec4 Vertex;
    vec3 worldPosition;
    vec2 UV;
    vec2 Global_UV;
    flat uint map_id;
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

vec4 blend_normal(vec4 n1, vec4 n2, vec4 texture1, float a1, vec4 texture2, float a2) {
 float depth = 0.5;
 float ma = max(texture1.a + a1, texture2.a + a2) - depth;
 float b1 = max(texture1.a + a1 - ma, 0);
 float b2 = max(texture2.a + a2 - ma, 0);
 return (n1 * b1 + n2 * b2) / (b1 + b2);
 }


vec2 get_transformed_uv(in vec4 U, in vec4 V) {

    vec4 vt = vec4(-fs_in.UV.x*100.0+50.0, 0.0, fs_in.UV.y*100.0, 1.0);
    vt *= vec4(1.0, -1.0, 1.0,  1.0);
    vec2 out_uv = vec2(dot(U,vt), dot(-V,vt));
    out_uv += vec2(0.50,0.50);
    return out_uv;
    }
    
vec4 crop( sampler2DArray samp, in vec2 uv , in float layer)
{
    vec2 cropped = fract(uv) * vec2(0.875, 0.875) + vec2(0.0625, 0.0625);
    return texture(samp,vec3(cropped, layer));
}

vec4 crop2( sampler2DArray samp, in vec2 uv , in float layer)
{
    uv *= vec2(0.125, 0.125);
    //uv += vec2(offset.x , offset.y);
    vec2 cropped = fract(uv)* vec2(0.875, 0.875) + vec2(0.0625, 0.0625);
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

/*===========================================================*/
/*===========================================================*/
/*===========================================================*/

void main(void)
{
    //==============================================================
    vec4 mt[8];
    vec4 mn[8];
    vec4 t[8];
    vec4 n[8];
    vec2 tuv[8]; 

    vec2 MixLevel1, MixLevel2, MixLevel3, MixLevel4;
    vec2 mix_coords;
    //==============================================================

    mix_coords = fs_in.UV;
    mix_coords.x = 1.0 - mix_coords.x;

    vec4 global = texture(global_AM, fs_in.Global_UV);
    // create UV projections
    tuv[0] = get_transformed_uv(L.U[0], L.V[0]); 
    tuv[1] = get_transformed_uv(L.U[1], L.V[1]);
    tuv[2] = get_transformed_uv(L.U[2], L.V[2]); 
    tuv[3] = get_transformed_uv(L.U[3], L.V[3]);
    tuv[4] = get_transformed_uv(L.U[4], L.V[4]); 
    tuv[5] = get_transformed_uv(L.U[5], L.V[5]);
    tuv[6] = get_transformed_uv(L.U[6], L.V[6]);
    tuv[7] = get_transformed_uv(L.U[7], L.V[7]);

    // Get AM maps 
    // Get AM maps,crop and set Test outline blend flag

    t[0] = crop(at1, tuv[0], 0.0);
    t[1] = crop(at2, tuv[1], 0.0);
    t[2] = crop(at3, tuv[2], 0.0);
    t[3] = crop(at4, tuv[3], 0.0);
    t[4] = crop(at5, tuv[4], 0.0);
    t[5] = crop(at6, tuv[5], 0.0);
    t[6] = crop(at7, tuv[6], 0.0);
    t[7] = crop(at8, tuv[7], 0.0);
    
    mt[0] = crop2(at1, tuv[0], 2.0);
    mt[1] = crop2(at2, tuv[1], 2.0);
    mt[2] = crop2(at3, tuv[2], 2.0);
    mt[3] = crop2(at4, tuv[3], 2.0);
    mt[4] = crop2(at5, tuv[4], 2.0);
    mt[5] = crop2(at6, tuv[5], 2.0);
    mt[6] = crop2(at7, tuv[6], 2.0);
    mt[7] = crop2(at8, tuv[7], 2.0);

    // Height is in red channel of the normal maps.
    // Ambient occlusion is in the Blue channel.
    // Green and Alpha are normal values.

    n[0] = crop(at1, tuv[0], 1.0);
    n[1] = crop(at2, tuv[1], 1.0);
    n[2] = crop(at3, tuv[2], 1.0);
    n[3] = crop(at4, tuv[3], 1.0);
    n[4] = crop(at5, tuv[4], 1.0);
    n[5] = crop(at6, tuv[5], 1.0);
    n[6] = crop(at7, tuv[6], 1.0);
    n[7] = crop(at8, tuv[7], 1.0);

    mn[0] = crop2(at1, tuv[0], 3.0);
    mn[1] = crop2(at2, tuv[1], 3.0);
    mn[2] = crop2(at3, tuv[2], 3.0);
    mn[3] = crop2(at4, tuv[3], 3.0);
    mn[4] = crop2(at5, tuv[4], 3.0);
    mn[5] = crop2(at6, tuv[5], 3.0);
    mn[6] = crop2(at7, tuv[6], 3.0);
    mn[7] = crop2(at8, tuv[7], 3.0);

    // get the ambient occlusion
    t[0].rgb *= n[0].b;
    t[1].rgb *= n[1].b;
    t[2].rgb *= n[2].b;
    t[3].rgb *= n[3].b;
    t[4].rgb *= n[4].b;
    t[5].rgb *= n[5].b;
    t[6].rgb *= n[6].b;
    t[7].rgb *= n[7].b;
   
    mt[0].rgb *= mn[0].b;
    mt[1].rgb *= mn[1].b;
    mt[2].rgb *= mn[2].b;
    mt[3].rgb *= mn[3].b;
    mt[4].rgb *= mn[4].b;
    mt[5].rgb *= mn[5].b;
    mt[6].rgb *= mn[6].b;
    mt[7].rgb *= mn[7].b;

    t[0].rgb = t[0].rgb* min(L.r2[0].x,1.0) + mt[0].rgb*(L.r2[0].y+1.0);
    t[1].rgb = t[1].rgb* min(L.r2[1].x,1.0) + mt[1].rgb*(L.r2[1].y+1.0);
    t[2].rgb = t[2].rgb* min(L.r2[2].x,1.0) + mt[2].rgb*(L.r2[2].y+1.0);
    t[3].rgb = t[3].rgb* min(L.r2[3].x,1.0) + mt[3].rgb*(L.r2[3].y+1.0);
    t[4].rgb = t[4].rgb* min(L.r2[4].x,1.0) + mt[4].rgb*(L.r2[4].y+1.0);
    t[5].rgb = t[5].rgb* min(L.r2[5].x,1.0) + mt[5].rgb*(L.r2[5].y+1.0);
    t[6].rgb = t[6].rgb* min(L.r2[6].x,1.0) + mt[6].rgb*(L.r2[6].y+1.0);
    t[7].rgb = t[7].rgb* min(L.r2[7].x,1.0) + mt[7].rgb*(L.r2[7].y+1.0);

    n[0].rgb = n[0].rgb* min(L.r2[0].x,1.0) + mn[0].rgb*(L.r2[0].y+1.0);
    n[1].rgb = n[1].rgb* min(L.r2[1].x,1.0) + mn[1].rgb*(L.r2[1].y+1.0);
    n[2].rgb = n[2].rgb* min(L.r2[2].x,1.0) + mn[2].rgb*(L.r2[2].y+1.0);
    n[3].rgb = n[3].rgb* min(L.r2[3].x,1.0) + mn[3].rgb*(L.r2[3].y+1.0);
    n[4].rgb = n[4].rgb* min(L.r2[4].x,1.0) + mn[4].rgb*(L.r2[4].y+1.0);
    n[5].rgb = n[5].rgb* min(L.r2[5].x,1.0) + mn[5].rgb*(L.r2[5].y+1.0);
    n[6].rgb = n[6].rgb* min(L.r2[6].x,1.0) + mn[6].rgb*(L.r2[6].y+1.0);
    n[7].rgb = n[7].rgb* min(L.r2[7].x,1.0) + mn[7].rgb*(L.r2[7].y+1.0);

    //Get the mix values from the mix textures 1-4 and move to vec2. 
    MixLevel1.rg = texture(mixtexture1, mix_coords.xy).ag;
    MixLevel2.rg = texture(mixtexture2, mix_coords.xy).ag;
    MixLevel3.rg = texture(mixtexture3, mix_coords.xy).ag;
    MixLevel4.rg = texture(mixtexture4, mix_coords.xy).ag;


    MixLevel1.r *= t[0].a+L.r1[0].x;
    MixLevel1.g *= t[1].a+L.r1[1].x;
    MixLevel2.r *= t[2].a+L.r1[2].x;
    MixLevel2.g *= t[3].a+L.r1[3].x;
    MixLevel3.r *= t[4].a+L.r1[4].x;
    MixLevel3.g *= t[5].a+L.r1[5].x;
    MixLevel4.r *= t[6].a+L.r1[6].x;
    MixLevel4.g *= t[7].a+L.r1[7].x;

    float power = 0.7;
    MixLevel1.r = pow(MixLevel1.r,1.0/power);
    MixLevel1.g = pow(MixLevel1.g,1.0/power);
    MixLevel2.r = pow(MixLevel2.r,1.0/power);
    MixLevel2.g = pow(MixLevel2.g,1.0/power);
    MixLevel3.r = pow(MixLevel3.r,1.0/power);
    MixLevel3.g = pow(MixLevel3.g,1.0/power);
    MixLevel4.r = pow(MixLevel4.r,1.0/power);
    MixLevel4.g = pow(MixLevel4.g,1.0/power);

     float f =0.0;
    f += dot(MixLevel1.rg,vec2(1.0,1.0));
    f += dot(MixLevel2.rg,vec2(1.0,1.0));
    f += dot(MixLevel3.rg,vec2(1.0,1.0));
    f += dot(MixLevel4.rg,vec2(1.0,1.0));

    MixLevel1.rg/= f;
    MixLevel2.rg/= f;
    MixLevel3.rg/= f;
    MixLevel4.rg/= f;   //months of work to figure this out!
   
//    MixLevel1 = max(MixLevel1,vec2(0.0139));
//    MixLevel2 = max(MixLevel2,vec2(0.0139));
//    MixLevel3 = max(MixLevel3,vec2(0.0139));
//    MixLevel4 = max(MixLevel4,vec2(0.0139));
//   //retuned to old mixing. Its better
       vec4 base;

    base =  t[0] * MixLevel1.r;
    base += t[1] * MixLevel1.g;
    base += t[2] * MixLevel2.r;
    base += t[3] * MixLevel2.g;
    base += t[4] * MixLevel3.r;
    base += t[5] * MixLevel3.g;
    base += t[6] * MixLevel4.r;
    base += t[7] * MixLevel4.g;

    //global
     vec4 gc = global;
    float c_l = length(base.rgb) + base.a + global.a+0.25;
    float g_l = length(global.rgb) - global.a-base.a;
    gc.rgb = global.rgb;
    base.rgb = (base.rgb * c_l + global.rgb * g_l) / 1.8;
    //wetness
    base = blend(base, base.a+0.75, vec4(props.waterColor, props.waterAlpha), global.a);

    //-------------------------------------------------------------
    // normals

    vec4 out_n;
    out_n =  n[0] * MixLevel1.r;
    out_n += n[1] * MixLevel1.g;
    out_n += n[2] * MixLevel2.r;
    out_n += n[3] * MixLevel2.g;
    out_n += n[4] * MixLevel3.r;
    out_n += n[5] * MixLevel3.g;
    out_n += n[6] * MixLevel4.r;
    out_n += n[7] * MixLevel4.g;
    float specular = out_n.r;

    gNormal.xyz = normalize(convertNormal(out_n).xyz);

    gGMF = vec4(0.1, specular, 128.0/255.0, 0.0);
    //vec3 shad = vec3( texture( shadow, vec3(fs_in.UV, float(map_id)) ).r );
    //gColor.rgb *= shad;
    // global.a is used for wetness on the map.

    gColor.rgb = base.rgb;
    gColor.a = global.a*0.8;

}
