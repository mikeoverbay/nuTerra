#version 450 core

#extension GL_ARB_bindless_texture : require
#extension GL_ARB_shading_language_include : require

#define USE_MATERIALS_SSBO
#include "common.h" //! #include "../common.h"

layout(early_fragment_tests) in;

// Output
layout (location = 0) out vec4 gColor;
layout (location = 1) out vec3 gNormal;
layout (location = 2) out vec4 gGMF;
layout (location = 3) out vec3 gPosition;
layout (location = 4) out uint gPick;

uniform bool show_Lods;

// Input from vertex shader
in VS_OUT
{
    vec2 TC1;
    vec2 TC2;
    vec3 worldPosition;
    mat3 TBN;
    flat uint material_id;
    flat uint model_id;
    flat uint lod_level;
    vec2 UV1;
    vec2 UV2;
    vec2 UV3;
    vec2 UV4;
    vec2 scale_123;
    vec2 scale_4;
    vec2 offset_123;
    vec2 offset_4;
} fs_in;

// globals
vec3 normalBump;
MaterialProperties thisMaterial = material[fs_in.material_id];

//##################################################################################
// sub functions ###################################################################
//##################################################################################
//color correction
//vec4 correct(in vec4 hdrColor, in float exposure, in float gamma_level){  
//    // Exposure tone mapping
//    vec3 mapped = vec3(1.0) - exp(-hdrColor.rgb * exposure);
//    // Gamma correction 
//    mapped.rgb = pow(mapped.rgb, vec3(1.0 / gamma_level));  
//    return vec4 (mapped, hdrColor.a);
//}
////##################################################################################

//##################################################################################
void get_and_write_no_mips(void){

    float alphaCheck = gColor.a;
    if (thisMaterial.g_useNormalPackDXT1) {
        normalBump = (texture(thisMaterial.maps[1], fs_in.TC1,1.0).rgb * 2.0f) - 1.0f;
    } else {
        vec4 normal = texture(thisMaterial.maps[1], fs_in.TC1,1.0);
        normalBump.xy = normal.ag * 2.0 - 1.0;
        float dp = min(dot(normalBump.xy, normalBump.xy),1.0);
        normalBump.z = clamp(sqrt(-dp+1.0),-1.0,1.0);
        normalBump = normalize(normalBump);
        alphaCheck = normal.r;
    }
    if (thisMaterial.alphaTestEnable && alphaCheck < thisMaterial.alphaReference) {
        //discard;
    }
    gNormal.xyz = normalize(fs_in.TBN * normalBump.xyz);
}
//##################################################################################
vec3 get_detail_normal(vec4 anm){
    vec3 bump;
    bump.xy = anm.ag * 2.0 - 1.0;
    float dp = min(dot(bump.xy, bump.xy),1.0);
    bump.z = clamp(sqrt(-dp+1.0),-1.0,1.0);
    return normalize(fs_in.TBN * bump);
}
//##################################################################################
float get_mip_map_level(sampler2D samp)
{
    ivec2 isize = textureSize(samp,0);
    vec2  dx_vtc        = dFdx(fs_in.TC1 * float(isize.x));
    vec2  dy_vtc        = dFdy(fs_in.TC1 * float(isize.y));
    float d = max(dot(dx_vtc, dx_vtc), dot(dy_vtc, dy_vtc));
    return round(0.3 * log2(d)); 
}
//##################################################################################
int get_dom_mix(in vec3 b){
    int ov = 0;
    float s = b.x;
    if (b.y > b.x) {
        s = b.y;
        ov = 1;
    }
    if (b.z > s) {
        ov = 2;
    }
    return ov;
}
 //##################################################################################
 // Dispacted Functions #############################################################
 //##################################################################################
subroutine void fn_entry();
layout(index = 0) subroutine(fn_entry) void default_entry()
{
    gColor = vec4(1, 0, 0, 0);
}
//##################################################################################
layout(index = 1) subroutine(fn_entry) void FX_PBS_ext_entry()
{
    gColor = texture(thisMaterial.maps[0], fs_in.TC1,1); // color
    gColor *= thisMaterial.g_colorTint;
    vec4 gm = texture(thisMaterial.maps[2], fs_in.TC1,1);
    gGMF.rg = gm.rg; // gloss/metal

    if (thisMaterial.g_enableAO) gColor.xyz += gColor.xyz * gm.b;
    get_and_write_no_mips();
}
//##################################################################################
layout(index = 2) subroutine(fn_entry) void FX_PBS_ext_dual_entry()
{
    gColor = textureLod(thisMaterial.maps[0], fs_in.TC1, get_mip_map_level(thisMaterial.maps[0])); // color
    gColor *= textureLod(thisMaterial.maps[3], fs_in.TC2, get_mip_map_level(thisMaterial.maps[3])); // color2
    gColor *= thisMaterial.g_colorTint;
    gColor.rgb *= 2.0; // this will need tweaking
    gGMF.rg = textureLod(thisMaterial.maps[2], fs_in.TC1, get_mip_map_level(thisMaterial.maps[2])).rg; // gloss/metal
    get_and_write_no_mips();
}
//##################################################################################
layout(index = 3) subroutine(fn_entry) void FX_PBS_ext_detail_entry()
{   
    // detail uv scale is in g_detailRejectTiling.zw;
    vec2 uvc = fract(fs_in.TC1) * thisMaterial.g_detailRejectTiling.zw;
    gColor = texture(thisMaterial.maps[0], fs_in.TC1);
    gColor *= thisMaterial.g_colorTint;
    
    vec4 gm = texture(thisMaterial.maps[2], fs_in.TC1);
    float nm_aoc = texture(thisMaterial.maps[1], fs_in.TC1).b;
    float d_aoc = texture(thisMaterial.maps[3], uvc).b;

    //gColor.rgb *= mix(nm_aoc, d_aoc, thisMaterial.g_detailInfluences.x);

    gGMF.rgb = gm.rgb; // gloss/metal
    vec4 nmap;
    nmap.ag = mix(texture(thisMaterial.maps[1], fs_in.TC1).ag, texture(thisMaterial.maps[3],
                (uvc),1.0).ag, 1.0-thisMaterial.g_detailInfluences.xx);

    gNormal.rgb = get_detail_normal(nmap);
    }
//##################################################################################
layout(index = 4) subroutine(fn_entry) void FX_PBS_tiled_atlas_entry()
{
    vec2 UVs;
    vec2 uv1,uv2,uv3,uv4;

    vec2 zeroONE = vec2(fract(fs_in.TC1.x), fract(fs_in.TC1.y)); // 0.0 to 1.0 uv

    UVs = zeroONE*fs_in.scale_123 + fs_in.offset_123;
    uv1 = UVs + fs_in.UV1;
    uv2 = UVs + fs_in.UV2;
    uv3 = UVs + fs_in.UV3;

    zeroONE = vec2(fract(fs_in.TC2.x), fract(fs_in.TC2.y)); // 0.0 to 1.0 uv
    UVs = zeroONE*fs_in.scale_4 + fs_in.offset_4;
    uv4 = UVs + fs_in.UV4;

    vec4 blend = textureLod(thisMaterial.maps[3], uv4,0.0);

    vec4 colorAM_x = textureLod(thisMaterial.maps[0],uv1,get_mip_map_level(thisMaterial.maps[0])) * thisMaterial.g_tile0Tint;
    vec4 colorAM_y = textureLod(thisMaterial.maps[0],uv2,get_mip_map_level(thisMaterial.maps[0])) * thisMaterial.g_tile1Tint;
    vec4 colorAM_z = textureLod(thisMaterial.maps[0],uv3,get_mip_map_level(thisMaterial.maps[0])) * thisMaterial.g_tile2Tint;

    float dirtLevel = blend.z;

    float b = -blend.y + 1.0;
    blend.z = clamp(-blend.x + b, 0.0, 1.0);

    blend.z += 0.01;

    blend.x *= colorAM_x.a;
    blend.y *= colorAM_y.a;
    blend.z *= colorAM_z.a;

    blend.xyz *= blend.xyz;
    blend.xyz *= blend.xyz;
    blend.xyz *= blend.xyz;
//    blend.xyz *= blend.xyz;


    float d = dot(blend.xyz, vec3(1.0));
    blend.xyz = blend.xyz/d;


    vec4 GBMT, MAO;
    vec2 DOM_UV;
    switch (get_dom_mix(blend.xyz)){
        case 0:
            DOM_UV = uv1;
            break;

        case 1:
            DOM_UV = uv2;
            break;

        case 2:
            DOM_UV = uv3;
        }

    GBMT = textureLod(thisMaterial.maps[1],DOM_UV,get_mip_map_level(thisMaterial.maps[1]));
    MAO  = textureLod(thisMaterial.maps[2],DOM_UV,get_mip_map_level(thisMaterial.maps[2]));


    //need to sort this out!
    vec2 dirt_scale = vec2(thisMaterial.dirtParams.y,thisMaterial.dirtParams.z);
    float dirt_blend = thisMaterial.dirtParams.x;

    vec4 DIRT = textureLod(thisMaterial.maps[4],fs_in.TC1,get_mip_map_level(thisMaterial.maps[4]));
    //DIRT.rgb *= thisMaterial.dirtColor.rgb;
    DIRT.rgb *= DIRT.a;
    //============================================
    vec4 colorAM, r0;

    colorAM.xyz =  colorAM_y.xyz * blend.yyy;
    colorAM.xyz += colorAM_z.xyz * blend.zzz;
    colorAM.xyz += colorAM_x.xyz * blend.xxx;
    
    

    //colorAM.rgb = mix(colorAM.rgb,colorAM.rgb * DIRT.rgb, dirtLevel *0.5);
 

    colorAM.rgb *= MAO.ggg;
    colorAM *= blend.a;
    gColor = colorAM;

    //save Gloss.Metal
    gGMF.r = GBMT.r;
    gGMF.g = MAO.r;
        
    vec3 bump;
    vec2 tb = vec2(GBMT.ag * 2.0 - 1.0);

    bump.xy = tb.xy;
    float dp = min(dot(bump.xy, bump.xy),1.0);
    bump.z = clamp(sqrt(-dp+1.0),-1.0,1.0);
    bump = normalize(bump);

    gNormal = normalize(fs_in.TBN * bump);
}
//##################################################################################
layout(index = 5) subroutine(fn_entry) void FX_PBS_tiled_atlas_global_entry()
{
    vec2 UVs;
    vec2 uv1,uv2,uv3,uv4;

    vec4 globalTex = texture(thisMaterial.maps[5],fs_in.TC2);

    vec2 zeroONE = vec2(fract(fs_in.TC1.x), fract(fs_in.TC1.y));

    UVs = zeroONE*fs_in.scale_123 + fs_in.offset_123;
    uv1 = UVs + fs_in.UV1;
    uv2 = UVs + fs_in.UV2;
    uv3 = UVs + fs_in.UV3;

    zeroONE = vec2(fract(fs_in.TC2.x), fract(fs_in.TC2.y));
    UVs = zeroONE*fs_in.scale_4 + fs_in.offset_4;
    uv4 = UVs + fs_in.UV4;

    vec4 blend = textureLod(thisMaterial.maps[3], uv4,0.0);

    vec4 colorAM_x = textureLod(thisMaterial.maps[0],uv1,get_mip_map_level(thisMaterial.maps[0])) * thisMaterial.g_tile0Tint;
    vec4 colorAM_y = textureLod(thisMaterial.maps[0],uv2,get_mip_map_level(thisMaterial.maps[0])) * thisMaterial.g_tile1Tint;
    vec4 colorAM_z = textureLod(thisMaterial.maps[0],uv3,get_mip_map_level(thisMaterial.maps[0])) * thisMaterial.g_tile2Tint;

    float dirtLevel = blend.z;

    float b = -blend.y + 1.0;
    blend.z = clamp(-blend.x + b, 0.0, 1.0);

    blend.z += 0.01;

    blend.x *= colorAM_x.a;
    blend.y *= colorAM_y.a;
    blend.z *= colorAM_z.a;

    blend.xyz *= blend.xyz;
    blend.xyz *= blend.xyz;
    blend.xyz *= blend.xyz;


    float d = dot(blend.xyz, vec3(1.0));
    blend.xyz = blend.xyz/d;

    vec4 GBMT, MAO;
    vec2 DOM_UV;
    switch (get_dom_mix(blend.xyz)){
        case 0:
            DOM_UV = uv1;
            break;

        case 1:
            DOM_UV = uv2;
            break;

        case 2:
            DOM_UV = uv3;
        }

    GBMT = textureLod(thisMaterial.maps[1],DOM_UV,get_mip_map_level(thisMaterial.maps[1]));
    MAO  = textureLod(thisMaterial.maps[2],DOM_UV,get_mip_map_level(thisMaterial.maps[2]));


    //need to sort this out!
    vec2 dirt_scale = vec2(thisMaterial.dirtParams.y,thisMaterial.dirtParams.z);
    float dirt_blend = thisMaterial.dirtParams.x;

    vec4 DIRT = textureLod(thisMaterial.maps[4],fs_in.TC1,get_mip_map_level(thisMaterial.maps[4]));
    DIRT.rgb *= thisMaterial.dirtColor.rgb;
    DIRT.rgb *= DIRT.a;
    //============================================

    vec4 colorAM;
    colorAM.xyz =  colorAM_y.xyz * blend.yyy;
    colorAM.xyz += colorAM_z.xyz * blend.zzz;
    colorAM.xyz += colorAM_x.xyz * blend.xxx;

    colorAM.rgb = mix(colorAM.rgb, DIRT.rgb, dirtLevel *0.35);
 

    colorAM.rgb *= MAO.ggg;
    colorAM *= blend.a;
    gColor = colorAM;

    //save Gloss.Metal
    gGMF.r = GBMT.r;
    gGMF.g = MAO.r;
        
    vec3 bump;

    GBMT = mix(GBMT, globalTex, 0.5); // mix in the global NormalMap

    vec2 tb = vec2(GBMT.ga * 2.0 - 1.0);
    bump.xy = tb.xy;
    float dp = min(dot(bump.xy, bump.xy),1.0);
    bump.z = clamp(sqrt(-dp+1.0),-1.0,1.0);
    bump = normalize(bump);

    gNormal = normalize(fs_in.TBN * bump);
}
//##################################################################################
layout(index = 6) subroutine(fn_entry) void FX_PBS_glass()
{
    discard;
}
//##################################################################################
layout(index = 7) subroutine(fn_entry) void FX_PBS_ext_repaint()
{
    // g_tile0Tint = g_baseCOlor
    // g_tile1Tint = g_repaintColor

   vec4 diffuse = texture(thisMaterial.maps[0], fs_in.TC1);
   diffuse.rgb = mix(diffuse.rgb, thisMaterial.g_tile0Tint.rgb , diffuse.a);
   diffuse.rgb = mix(diffuse.rgb, thisMaterial.g_tile1Tint.rgb , diffuse.a);
   //diffuse.rgb += diffuse.rgb * (thisMaterial.g_tile1Tint.rgb * diffuse.a);
   gGMF.rga = texture(thisMaterial.maps[2], fs_in.TC1).rgb; // gloss/metal

   gColor = diffuse;
   get_and_write_no_mips();
}
//##################################################################################
layout(index = 8) subroutine(fn_entry) void FX_lightonly_alpha_entry()
{
    // gColor = texture(thisMaterial.maps[0], fs_in.TC1);
    gColor = vec4(0.0,0.0,1.0,0.0); // debug
}
//##################################################################################
layout(index = 9) subroutine(fn_entry) void FX_unsupported_entry()
{
    gColor = vec4(1.0, 1.0, 1.0, 0.0);
}
//##################################################################################
subroutine uniform fn_entry entries[10];

// ================================================================================
// Main start
// ================================================================================
void main(void)
{
    float renderType = 64.0/255.0; // 64 = PBS, 63 = light/bump

    entries[thisMaterial.shader_type]();
    gColor.rgb = pow(gColor.rgb, vec3(1.0 / 1.3));
    gColor.a = 0.0;

    gPick.r = fs_in.model_id + 1;

    gPosition = fs_in.worldPosition;
    gGMF.b = renderType;
    gGMF.a - 0.0;

    // Just for debugging
    if (show_Lods) {
        if (fs_in.lod_level == 1)      { gColor.r += 0.4; }
        else if (fs_in.lod_level == 2) { gColor.g += 0.4; }
        else if (fs_in.lod_level == 3) { gColor.b += 0.4; }
    }

}
// ================================================================================
