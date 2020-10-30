#version 450 core

#extension GL_ARB_bindless_texture : require
#extension GL_ARB_shading_language_include : require

#define USE_MATERIALS_SSBO
#include "common.h"

// Output
layout (location = 0) out vec4 gColor;
layout (location = 1) out vec3 gNormal;
layout (location = 2) out vec4 gGMF;
layout (location = 3) out vec3 gPosition;
layout (location = 4) out uint gPick;

uniform int show_Lods;

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

// ================================================================================
// globals
vec3 normalBump;
const float PI = 3.14159265359;
MaterialProperties thisMaterial = material[fs_in.material_id];


// ================================================================================
// functions
// ================================================================================
//color correction
vec4 correct(in vec4 hdrColor, in float exposure, in float gamma_level){  
    // Exposure tone mapping
    vec3 mapped = vec3(1.0) - exp(-hdrColor.rgb * exposure);
    // Gamma correction 
    mapped.rgb = pow(mapped.rgb, vec3(1.0 / gamma_level));  
    return vec4 (mapped, hdrColor.a);
}

void get_normal(in float mip)
{
    float alphaCheck = gColor.a;
    if (thisMaterial.g_useNormalPackDXT1) {
        normalBump = (textureLod(thisMaterial.maps[1], fs_in.TC1, mip).rgb * 2.0f) - 1.0f;
    } else {
        vec4 normal = textureLod(thisMaterial.maps[1], fs_in.TC1, mip);
        normalBump.xy = normal.ag * 2.0 - 1.0;
        normalBump.z = sqrt(1.0 - dot(normalBump.xy, normalBump.xy));
        alphaCheck = normal.r;
    }
    if (thisMaterial.alphaTestEnable && alphaCheck < thisMaterial.alphaReference) {
        discard;
    }
    gNormal.xyz = normalize(fs_in.TBN * normalBump.xyz);
}


// ================================================================================
// Atlas Functions
float mip_map_level(in vec2 iUV)
{
    ivec2 isize = textureSize(thisMaterial.maps[0],0);
    vec2  dx_vtc        = dFdx(iUV * float(isize.x));
    vec2  dy_vtc        = dFdy(iUV * float(isize.y));
    float d = max(dot(dx_vtc, dx_vtc), dot(dy_vtc, dy_vtc));
    
    return round(0.55 * log2(d)); 
}

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


// ================================================================================
// Subroutines
subroutine void fn_entry();
layout(index = 0) subroutine(fn_entry) void default_entry()
{
    gColor = vec4(1, 0, 0, 0);
}


layout(index = 1) subroutine(fn_entry) void FX_PBS_ext_entry()
{
    float mip = mip_map_level(fs_in.TC1);
    gColor = textureLod(thisMaterial.maps[0], fs_in.TC1, mip); // color
    gColor *= thisMaterial.g_colorTint;
    gGMF.rg = textureLod(thisMaterial.maps[2], fs_in.TC1, 0).rg; // gloss/metal
    get_normal(mip);
}


layout(index = 2) subroutine(fn_entry) void FX_PBS_ext_dual_entry()
{
    float mip = mip_map_level(fs_in.TC1);
    gColor = textureLod(thisMaterial.maps[0], fs_in.TC1, mip); // color
    gColor *= textureLod(thisMaterial.maps[3], fs_in.TC2, mip); // color2
    gColor *= thisMaterial.g_colorTint;
    gColor.rgb *= 1.5; // this will need tweaking
    gGMF.rg = textureLod(thisMaterial.maps[2], fs_in.TC1, mip).rg; // gloss/metal
    get_normal(mip);
}


layout(index = 3) subroutine(fn_entry) void FX_PBS_ext_detail_entry()
{
    float mip = mip_map_level(fs_in.TC1);
    gColor = texture(thisMaterial.maps[0], fs_in.TC1);
    gColor *= thisMaterial.g_colorTint;
    gGMF.rg = texture(thisMaterial.maps[2], fs_in.TC1).rg; // gloss/metal
    get_normal(mip);
}


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

    float mip = mip_map_level(fs_in.TC2);
    vec4 BLEND = texture2D(thisMaterial.maps[3],uv4);

    vec4 colorAM_1 = textureLod(thisMaterial.maps[0],uv1,mip) * thisMaterial.g_tile0Tint;
    vec4 colorAM_2 = textureLod(thisMaterial.maps[0],uv2,mip) * thisMaterial.g_tile1Tint;
    vec4 colorAM_3 = textureLod(thisMaterial.maps[0],uv3,mip) * thisMaterial.g_tile2Tint;

    vec4 GBMT, MAO;
    vec2 DOM_UV;
    switch (get_dom_mix(BLEND.xyz)){
        case 0:
            DOM_UV = uv1;
            break;

        case 1:
            DOM_UV = uv2;
            break;

        case 2:
            DOM_UV = uv3;
        }

    GBMT =    textureLod(thisMaterial.maps[1],DOM_UV,mip);
    MAO =     textureLod(thisMaterial.maps[2],DOM_UV,mip);

    //need to sort this out!
    vec2 dirt_scale = vec2(thisMaterial.dirtParams.y,thisMaterial.dirtParams.z);
    float dirt_blend = thisMaterial.dirtParams.x;

    vec4 DIRT = textureLod(thisMaterial.maps[4],uv4,mip);
    DIRT.rgb *= thisMaterial.dirtColor.rgb;

    //============================================
    // Some 40 plus hours of trial and error to get this working.
    // The mix texture has to be compressed down/squished.
    BLEND.r = smoothstep(BLEND.r*colorAM_1.a,0.00,0.09);
    BLEND.g = smoothstep(BLEND.g*colorAM_2.a,0.00,0.25);
    BLEND.b = smoothstep(BLEND.b,0.00,0.6);// uncertain still... but this value seems to work well
    BLEND = correct(BLEND,4.0,0.8);
    //============================================

    vec4 colorAM = colorAM_3;
    colorAM = mix(colorAM,colorAM_1, BLEND.r);
    colorAM = mix(colorAM,colorAM_2, BLEND.g);
          
    colorAM = mix(colorAM,DIRT, BLEND.b);
    colorAM *= BLEND.a;
    gColor = colorAM;

    //save Gloss.Metal
    gGMF.r = GBMT.r;
    gGMF.g = MAO.r;
        
    vec3 bump;
    vec2 tb = vec2(GBMT.ga * 2.0 - 1.0);
    bump.xy    = tb.xy;
    bump.z = clamp(sqrt(1.0 - ((tb.x*tb.x)+(tb.y*tb.y))),-1.0,1.0);
    gNormal = normalize(fs_in.TBN * bump);
}


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

    float mip = mip_map_level(fs_in.TC2);
    vec4 BLEND = texture2D(thisMaterial.maps[3],uv4);

    vec4 colorAM_1 = textureLod(thisMaterial.maps[0],uv1,mip) * thisMaterial.g_tile0Tint;
    vec4 colorAM_2 = textureLod(thisMaterial.maps[0],uv2,mip) * thisMaterial.g_tile1Tint;
    vec4 colorAM_3 = textureLod(thisMaterial.maps[0],uv3,mip) * thisMaterial.g_tile2Tint;

    vec4 GBMT, MAO;
    vec2 DOM_UV;
    switch (get_dom_mix(BLEND.xyz)){
        case 0:
            DOM_UV = uv1;
            break;

        case 1:
            DOM_UV = uv2;
            break;

        case 2:
            DOM_UV = uv3;
        }

    GBMT = textureLod(thisMaterial.maps[1],DOM_UV,mip);
    MAO =  textureLod(thisMaterial.maps[2],DOM_UV,mip);

    //need to sort this out!
    vec2 dirt_scale = vec2(thisMaterial.dirtParams.y,thisMaterial.dirtParams.z);
    float dirt_blend = thisMaterial.dirtParams.x;

    vec4 DIRT = textureLod(thisMaterial.maps[4],uv4,mip);
    DIRT.rgb *= thisMaterial.dirtColor.rgb;

    //============================================
    // Some 40 plus hours of trial and error to get this working.
    // The mix texture has to be compressed down/squished.
    BLEND.r = smoothstep(BLEND.r*colorAM_1.a,0.00,0.09);
    BLEND.g = smoothstep(BLEND.g*colorAM_2.a,0.00,0.25);
    BLEND.b = smoothstep(BLEND.b,0.00,0.6);// uncertain still... but this value seems to work well
    BLEND = correct(BLEND,4.0,0.8);
    //============================================

    vec4 colorAM = colorAM_3;
    colorAM = mix(colorAM,colorAM_1, BLEND.r);
    colorAM = mix(colorAM,colorAM_2, BLEND.g);

    colorAM = mix(colorAM,DIRT, BLEND.b);
    colorAM *= BLEND.a;
    gColor = colorAM;

    //save Gloss.Metal
    gGMF.r = GBMT.r;
    gGMF.g = MAO.r;

    vec3 bump;

    GBMT = mix(GBMT, globalTex, 0.5); // mix in the global NormalMap

    vec2 tb = vec2(GBMT.ga * 2.0 - 1.0);
    bump.xy    = tb.xy;
    bump.z = clamp(sqrt(1.0 - ((tb.x*tb.x)+(tb.y*tb.y))),-1.0,1.0);
    gNormal = normalize(fs_in.TBN * bump);
}


layout(index = 6) subroutine(fn_entry) void FX_PBS_glass()
{
    discard;
}





layout(index = 7) subroutine(fn_entry) void FX_PBS_ext_repaint()
{
    float mip = mip_map_level(fs_in.TC1);
    // g_tile0Tint = g_baseCOlor
    // g_tile1Tint = g_repaintColor

   vec4 diffuse = texture(thisMaterial.maps[0], fs_in.TC1);
   diffuse.rgb = mix(diffuse.rgb, thisMaterial.g_tile1Tint.rgb , diffuse.a);
   //diffuse.rgb += diffuse.rgb * (thisMaterial.g_tile1Tint.rgb * diffuse.a);
   gGMF.rg = texture(thisMaterial.maps[2], fs_in.TC1).rg; // gloss/metal

   gColor = diffuse;
   get_normal(mip);
}






layout(index = 8) subroutine(fn_entry) void FX_lightonly_alpha_entry()
{
    // gColor = texture(thisMaterial.maps[0], fs_in.TC1);
    gColor = vec4(0.0,0.0,1.0,0.0); // debug
}

layout(index = 9) subroutine(fn_entry) void FX_unsupported_entry()
{
    gColor = vec4(1.0, 1.0, 1.0, 0.0);
}

subroutine uniform fn_entry entries[10];

// ================================================================================
// Main start
// ================================================================================
void main(void)
{
    float renderType = 64.0/255.0; // 64 = PBS, 63 = light/bump

    entries[thisMaterial.shader_type]();

    gColor.a = 1.0;

    gPosition = fs_in.worldPosition;
    gGMF.b = renderType;
    gGMF.a - 0.0;
// Just for debugging
if (show_Lods == 1)
    {
        if (fs_in.lod_level == 1)      { gColor.r += 0.4; }
        else if (fs_in.lod_level == 2) { gColor.g += 0.4; }
        else if (fs_in.lod_level == 3) { gColor.b += 0.4; }
    }
    gPick.r = fs_in.model_id + 1;

}
// ================================================================================
