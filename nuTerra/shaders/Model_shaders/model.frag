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
layout (location = 4) out vec3 gSurfaceNormals;

#ifdef PICK_MODELS
layout (location = 5) out uint gPick;
#endif

// Input from vertex shader
in VS_OUT
{
    vec2 TC1;
    vec2 TC2;
    vec3 worldPosition;
    mat3 TBN;
    flat uint material_id;
    flat vec3 surfaceNormal;
#ifdef PICK_MODELS
    flat uint model_id;
#endif
#ifdef SHOW_LOD_COLORS
    flat uint lod_level;
#endif
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

    //float alphaCheck = gColor.a;
    if (thisMaterial.g_useNormalPackDXT1) {
        normalBump = (texture(sampler2D(thisMaterial.maps[1]), fs_in.TC1,1.0).rgb * 2.0f) - 1.0f;
    } else {
        vec4 normal = texture(sampler2D(thisMaterial.maps[1]), fs_in.TC1,1.0);
        normalBump.xy = normal.ag * 2.0 - 1.0;
        float dp = min(dot(normalBump.xy, normalBump.xy),1.0);
        normalBump.z = clamp(sqrt(-dp+1.0),-1.0,1.0);
        normalBump = normalize(normalBump);
        //alphaCheck = normal.r;
    }
    //if (thisMaterial.alphaTestEnable && alphaCheck < thisMaterial.alphaReference) {
        // discard;
    //}
    gNormal.xyz = normalize(fs_in.TBN * normalBump.xyz)*0.5+0.5;
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
float get_dom_mix(vec3 b) {
    float ov = thisMaterial.g_atlasIndexes.x;
    float s = b.x;
    if (b.y > b.x) {
        s = b.y;
        ov = thisMaterial.g_atlasIndexes.y;
    }
    if (b.z > s) {
        ov = thisMaterial.g_atlasIndexes.z;
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
    const sampler2D diffuseMap_sampler = sampler2D(thisMaterial.maps[0]);
    const sampler2D metallicGlossMap_sampler = sampler2D(thisMaterial.maps[2]);

    gColor = texture(diffuseMap_sampler, fs_in.TC1,1); // color
    gColor *= thisMaterial.g_colorTint;
    vec4 gm = texture(metallicGlossMap_sampler, fs_in.TC1,1);
    gGMF.rg = gm.rg; // gloss/metal

    if (thisMaterial.g_enableAO) gColor.xyz += gColor.xyz * gm.b;
    get_and_write_no_mips();
}
//##################################################################################
layout(index = 2) subroutine(fn_entry) void FX_PBS_ext_dual_entry()
{
    const sampler2D diffuseMap_sampler = sampler2D(thisMaterial.maps[0]);
    const sampler2D metallicGlossMap_sampler = sampler2D(thisMaterial.maps[2]);
    const sampler2D diffuseMap2_sampler = sampler2D(thisMaterial.maps[3]);

    gColor = textureLod(diffuseMap_sampler, fs_in.TC1, get_mip_map_level(diffuseMap_sampler)); // color
    gColor *= textureLod(diffuseMap2_sampler, fs_in.TC2, get_mip_map_level(diffuseMap2_sampler)); // color2
    gColor *= thisMaterial.g_colorTint;
    gColor.rgb *= 2.0; // this will need tweaking
    gGMF.rg = textureLod(metallicGlossMap_sampler, fs_in.TC1, get_mip_map_level(metallicGlossMap_sampler)).rg; // gloss/metal
    get_and_write_no_mips();
}
//##################################################################################
layout(index = 3) subroutine(fn_entry) void FX_PBS_ext_detail_entry()
{
    const sampler2D diffuseMap_sampler = sampler2D(thisMaterial.maps[0]);
    const sampler2D normalMap_sampler = sampler2D(thisMaterial.maps[1]);
    const sampler2D metallicGlossMap_sampler = sampler2D(thisMaterial.maps[2]);
    const sampler2D g_detailMap_sampler = sampler2D(thisMaterial.maps[3]);

    // detail uv scale is in g_detailRejectTiling.zw;
    vec2 uvc = fract(fs_in.TC1) * thisMaterial.g_detailRejectTiling.zw;
    gColor = texture(diffuseMap_sampler, fs_in.TC1);
    gColor *= thisMaterial.g_colorTint;
    
    vec4 gm = texture(metallicGlossMap_sampler, fs_in.TC1);
    float nm_aoc = texture(normalMap_sampler, fs_in.TC1).b;
    float d_aoc = texture(g_detailMap_sampler, uvc).b;

    //gColor.rgb *= mix(nm_aoc, d_aoc, thisMaterial.g_detailInfluences.x);

    gGMF.rga = gm.rgr; // gloss/metal
    vec4 nmap;
    nmap.ag = mix(texture(normalMap_sampler, fs_in.TC1).ag, texture(g_detailMap_sampler,
                (uvc),1.0).ag, 1.0-thisMaterial.g_detailInfluences.xx);

    gNormal.rgb = get_detail_normal(nmap)*0.5+0.5;
    }
//##################################################################################
layout(index = 4) subroutine(fn_entry) void FX_PBS_tiled_atlas_entry()
{
    const sampler2DArray atlasAlbedoHeight_sampler = sampler2DArray(thisMaterial.maps[0]);
    const sampler2DArray atlasNormalGlossSpec_sampler = sampler2DArray(thisMaterial.maps[1]);
    const sampler2DArray atlasMetallicAO_sampler = sampler2DArray(thisMaterial.maps[2]);
    const sampler2DArray atlasBlend_sampler = sampler2DArray(thisMaterial.maps[3]);
    const sampler2D dirtMap_sampler = sampler2D(thisMaterial.maps[4]);

    const float padSize = 0.0625;
    const vec2 uv1 = padSize + fract(fs_in.TC1) * (1.0 - padSize * 2.0);

    vec4 colorAM_x = texture(atlasAlbedoHeight_sampler, vec3(uv1, thisMaterial.g_atlasIndexes.x)) * thisMaterial.g_tile0Tint;
    vec4 colorAM_y = texture(atlasAlbedoHeight_sampler, vec3(uv1, thisMaterial.g_atlasIndexes.y)) * thisMaterial.g_tile1Tint;
    vec4 colorAM_z = texture(atlasAlbedoHeight_sampler, vec3(uv1, thisMaterial.g_atlasIndexes.z)) * thisMaterial.g_tile2Tint;
    vec4 blend = textureLod(atlasBlend_sampler, vec3(fs_in.TC2, thisMaterial.g_atlasIndexes.w), 0.0);

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

    float dom_id = get_dom_mix(blend.xyz);
    vec4 GBMT = texture(atlasNormalGlossSpec_sampler, vec3(uv1, dom_id));
    vec4 MAO  = texture(atlasMetallicAO_sampler, vec3(uv1, dom_id));

    //need to sort this out!
    vec2 dirt_scale = vec2(thisMaterial.dirtParams.y,thisMaterial.dirtParams.z);
    float dirt_blend = thisMaterial.dirtParams.x;

    vec4 DIRT = textureLod(dirtMap_sampler, fs_in.TC1, get_mip_map_level(dirtMap_sampler));
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

    gNormal = normalize(fs_in.TBN * bump)*0.5+0.5;
}
//##################################################################################
layout(index = 5) subroutine(fn_entry) void FX_PBS_tiled_atlas_global_entry()
{
    const sampler2DArray atlasAlbedoHeight_sampler = sampler2DArray(thisMaterial.maps[0]);
    const sampler2DArray atlasNormalGlossSpec_sampler = sampler2DArray(thisMaterial.maps[1]);
    const sampler2DArray atlasMetallicAO_sampler = sampler2DArray(thisMaterial.maps[2]);
    const sampler2DArray atlasBlend_sampler = sampler2DArray(thisMaterial.maps[3]);
    const sampler2D dirtMap_sampler = sampler2D(thisMaterial.maps[4]);
    const sampler2D globalTex_sampler = sampler2D(thisMaterial.maps[5]);

    vec4 globalTex = texture(globalTex_sampler, fs_in.TC2);

    const float padSize = 0.0625;
    const vec2 uv1 = padSize + fract(fs_in.TC1) * (1.0 - padSize * 2.0);

    vec4 colorAM_x = texture(atlasAlbedoHeight_sampler, vec3(uv1, thisMaterial.g_atlasIndexes.x)) * thisMaterial.g_tile0Tint;
    vec4 colorAM_y = texture(atlasAlbedoHeight_sampler, vec3(uv1, thisMaterial.g_atlasIndexes.y)) * thisMaterial.g_tile1Tint;
    vec4 colorAM_z = texture(atlasAlbedoHeight_sampler, vec3(uv1, thisMaterial.g_atlasIndexes.z)) * thisMaterial.g_tile2Tint;
    vec4 blend = textureLod(atlasBlend_sampler, vec3(fs_in.TC2, thisMaterial.g_atlasIndexes.w), 0.0);

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

    float dom_id = get_dom_mix(blend.xyz);
    vec4 GBMT = texture(atlasNormalGlossSpec_sampler, vec3(uv1, dom_id));
    vec4 MAO  = texture(atlasMetallicAO_sampler, vec3(uv1, dom_id));

    //need to sort this out!
    vec2 dirt_scale = vec2(thisMaterial.dirtParams.y,thisMaterial.dirtParams.z);
    float dirt_blend = thisMaterial.dirtParams.x;

    vec4 DIRT = textureLod(dirtMap_sampler, fs_in.TC1, get_mip_map_level(dirtMap_sampler));
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

    gNormal = normalize(fs_in.TBN * bump)*0.5+0.5;
}
//##################################################################################
layout(index = 6) subroutine(fn_entry) void FX_PBS_glass()
{
    // discard;
}
//##################################################################################
layout(index = 7) subroutine(fn_entry) void FX_PBS_ext_repaint()
{
    const sampler2D diffuseMap_sampler = sampler2D(thisMaterial.maps[0]);
    const sampler2D metallicGlossMap_sampler = sampler2D(thisMaterial.maps[2]);

    // g_tile0Tint = g_baseColor
    // g_tile1Tint = g_repaintColor

   vec4 diffuse = texture(diffuseMap_sampler, fs_in.TC1);
   diffuse.rgb = mix(diffuse.rgb, thisMaterial.g_tile0Tint.rgb , diffuse.a);
   diffuse.rgb = mix(diffuse.rgb, thisMaterial.g_tile1Tint.rgb , diffuse.a);
   //diffuse.rgb += diffuse.rgb * (thisMaterial.g_tile1Tint.rgb * diffuse.a);
   gGMF.rga = texture(metallicGlossMap_sampler, fs_in.TC1).rgb*vec3(0.5*diffuse.a,2.0-diffuse.a,0.4-diffuse.a); // gloss/metal

   gColor = diffuse;
   get_and_write_no_mips();
}
//##################################################################################
layout(index = 8) subroutine(fn_entry) void FX_lightonly_alpha_entry()
{
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
    const float renderType = 64.0/255.0; // 64 = PBS, 63 = light/bump

    entries[thisMaterial.shader_type]();
    gColor.rgb = pow(gColor.rgb, vec3(1.0 / 1.3));
    gColor.a = 0.0;

    gPosition = fs_in.worldPosition;
    gGMF.b = renderType;
    gSurfaceNormals = fs_in.surfaceNormal;

#ifdef PICK_MODELS
    gPick.r = fs_in.model_id + 1;
#endif

#ifdef SHOW_LOD_COLORS
    // Just for debugging
    if (fs_in.lod_level == 1)      { gColor.r += 0.4; }
    else if (fs_in.lod_level == 2) { gColor.g += 0.4; }
    else if (fs_in.lod_level == 3) { gColor.b += 0.4; }
#endif
}
