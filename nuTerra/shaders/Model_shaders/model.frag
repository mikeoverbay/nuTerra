﻿// gWriter fragment Shader. We will use this as a template for other shaders
#version 450 core

#extension GL_ARB_bindless_texture : require

// Output
layout (location = 0) out vec4 gColor;
layout (location = 1) out vec3 gNormal;
layout (location = 2) out vec3 gGMF;
layout (location = 3) out vec3 gPosition;

// Input from vertex shader
in VS_OUT
{
    vec2 TC1;
    vec2 TC2;
    vec3 worldPosition;
    mat3 TBN;
    flat uint material_id;
} fs_in;

struct MaterialProperties
{
    vec4 g_atlasIndexes;      /* 0   .. 16 */
    vec4 g_atlasSizes;        /* 16  .. 32 */
    vec4 g_colorTint;         /* 32  .. 48 */
    vec4 dirtParams;          /* 48  .. 64 */
    vec4 dirtColor;           /* 64  .. 80 */
    vec4 g_tile0Tint;         /* 80  .. 96 */
    vec4 g_tile1Tint;         /* 96  .. 112 */
    vec4 g_tile2Tint;         /* 112 .. 128 */
    vec4 g_tileUVScale;       /* 128 .. 144 */
    sampler2D maps[6];        /* 144 .. 192 */
    uint shader_type;         /* 192 .. 196 */
    bool g_useNormalPackDXT1; /* 196 .. 200 */
    float alphaReference;     /* 200 .. 204 */
    bool alphaTestEnable;     /* 204 .. 208 */
    bool g_useColorTint;      /* 208 .. 212 */
};

// Material block
layout (binding = 2, std430) readonly buffer MATERIALS
{
    MaterialProperties material[];
};


// Shader types
#define FX_PBS_ext                1
#define FX_PBS_ext_dual           2
#define FX_PBS_ext_detail         3
#define FX_PBS_tiled_atlas        4
#define FX_PBS_tiled_atlas_global 5
#define FX_lightonly_alpha        6
#define FX_unsupported            7

// ================================================================================
// globals
MaterialProperties thisMaterial = material[fs_in.material_id];
vec3 normalBump;
const float PI = 3.14159265359;
// ================================================================================

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

void get_normal()
{
    float alphaCheck = gColor.a;
    if (thisMaterial.g_useNormalPackDXT1) {
        normalBump = (texture(thisMaterial.maps[1], fs_in.TC1).rgb * 2.0f) - 1.0f;
    } else {
        vec4 normal = texture(thisMaterial.maps[1], fs_in.TC1);
        normalBump.xy = normal.ag * 2.0 - 1.0;
        normalBump.z = sqrt(1.0 - dot(normalBump.xy, normalBump.xy));
        alphaCheck = normal.r;
    }
    if (thisMaterial.alphaTestEnable && alphaCheck < thisMaterial.alphaReference) {
        discard;
    }
    gNormal.xyz = fs_in.TBN * normalBump.xyz;
}

// ================================================================================
// Atlas Functions
float mip_map_level(in vec2 texture_coordinate)
{
    vec2  dx_vtc        = dFdx(texture_coordinate);
    vec2  dy_vtc        = dFdy(texture_coordinate);
    float delta_max_sqr = max(dot(dx_vtc, dx_vtc), dot(dy_vtc, dy_vtc));
    
    return 0.5 * log2(delta_max_sqr); 
    return 5.0;
}
void get_atlas_uvs(inout vec2 UV1,inout vec2 UV2,
                   inout vec2 UV3,inout vec2 UV4, inout vec2 UV4_T)
{
    vec2 tc = fs_in.TC1/round(thisMaterial.g_tileUVScale).xy;
    vec4 At_size = thisMaterial.g_atlasSizes;

    ivec2 isize = textureSize(thisMaterial.maps[0],0);
    vec2 image_size;
    image_size.x = float(isize.x); //to float. AMD hates using int values with floats.
    image_size.y = float(isize.y);

    float uox = 0.0625;
    float uoy = 0.0625;

    float usx = 0.875;
    float usy = 0.875;
    vec2 hpix = vec2(0.5/image_size.x,0.5/image_size.y);// / At_size.xy;
    vec2 offset = vec2(uox/At_size.x, uoy/At_size.y) + hpix;

    //common scale for UV1, UV2 and UV3
    float scaleX = 1.0 / At_size.x;
    float scaleY = 1.0 / At_size.y;
    vec2 UVs;
    UVs.x = fract(fs_in.TC1.x)*scaleX*usx; 
    UVs.y = fract(fs_in.TC1.y)*scaleY*usy;
    //============================================
    vec2 tile;
    float index = thisMaterial.g_atlasIndexes.x;
    tile.y = floor(index/At_size.x);
    tile.x = index - tile.y * At_size.x;
    UV1.x = UVs.x + offset.x + tile.x * scaleX;
    UV1.y = UVs.y + offset.y + tile.y * scaleY;

    index = thisMaterial.g_atlasIndexes.y;
    tile.y = floor(index/At_size.x);
    tile.x = index - tile.y * At_size.x;
    UV2.x = UVs.x + offset.x + tile.x * scaleX;
    UV2.y = UVs.y + offset.y + tile.y * scaleY;

    index = thisMaterial.g_atlasIndexes.z;
    tile.y = floor(index/At_size.x);
    tile.x = index - tile.y * At_size.x;
    UV3.x = UVs.x + offset.x + tile.x * scaleX;
    UV3.y = UVs.y + offset.y + tile.y * scaleY;

    //UV4 is used for blend.
    scaleX = 1.0 / At_size.z;
    scaleY = 1.0 / At_size.w;

    index = thisMaterial.g_atlasIndexes.w;
    tile.y = floor(index/At_size.z);
    tile.x = index - tile.y * At_size.z;

    offset = vec2(uox/At_size.z, uoy/At_size.w);

    UV4.x = (fract(fs_in.TC2.x)*scaleX)+tile.x*scaleX;
    UV4.y = (fract(fs_in.TC2.y)*scaleY)+tile.y*scaleY;
    UV4_T.x = (fract(tc.x)*scaleX)+tile.x;
    UV4_T.y = (fract(tc.y)*scaleX)+tile.y;
// ================================================================================

}
// ================================================================================
// Main start
// ================================================================================
void main(void)
{
    float renderType = 64.0/255.0; // 64 = PBS, 63 = light/bump
    vec2 UV1, UV2, UV3, UV4, UV4_T;

    switch (thisMaterial.shader_type) {
    case FX_PBS_ext:
        gColor = texture(thisMaterial.maps[0], fs_in.TC1); // color
        gColor *= thisMaterial.g_colorTint;
        gGMF.rg = texture(thisMaterial.maps[2], fs_in.TC1).rg; // gloss/metal
        get_normal();
        break;

    case FX_PBS_ext_dual:
        gColor = texture(thisMaterial.maps[0], fs_in.TC1); // color
        gColor *= texture(thisMaterial.maps[3], fs_in.TC2); // color2
        gColor *= thisMaterial.g_colorTint;
        gColor.rgb *= 1.5; // this will need tweaking
        gGMF.rg = texture(thisMaterial.maps[2], fs_in.TC1).rg; // gloss/metal
        get_normal();
        break;

    case FX_PBS_ext_detail:

        gColor = texture(thisMaterial.maps[0], fs_in.TC1);
        gColor *= thisMaterial.g_colorTint;
        gGMF.rg = texture(thisMaterial.maps[2], fs_in.TC1).rg; // gloss/metal
        get_normal();
        break;

    case FX_PBS_tiled_atlas:

        get_atlas_uvs(UV1, UV2, UV3, UV4, UV4_T);


        float mip = mip_map_level(fs_in.TC1*thisMaterial.g_atlasSizes.xy)*0.5;
        vec4 BLEND = texture2D(thisMaterial.maps[3],UV4);

        vec4 colorAM_1 = textureLod(thisMaterial.maps[0],UV1,mip) * thisMaterial.g_tile0Tint;
        vec4 GBMT_1 =    textureLod(thisMaterial.maps[1],UV1,mip);
        vec4 MAO_1 =     textureLod(thisMaterial.maps[2],UV1,mip);

        vec4 colorAM_2 = textureLod(thisMaterial.maps[0],UV2,mip) * thisMaterial.g_tile1Tint;
        vec4 GBMT_2 =    textureLod(thisMaterial.maps[1],UV2,mip);
        vec4 MAO_2 =     textureLod(thisMaterial.maps[2],UV2,mip);

        vec4 colorAM_3 = textureLod(thisMaterial.maps[0],UV3,mip) * thisMaterial.g_tile2Tint;
        vec4 GBMT_3 =    textureLod(thisMaterial.maps[1],UV3,mip);
        vec4 MAO_3 =     textureLod(thisMaterial.maps[2],UV3,mip);

        //need to sort this out!
        vec2 dirt_scale = vec2(thisMaterial.dirtParams.y,thisMaterial.dirtParams.z);
        float dirt_blend = thisMaterial.dirtParams.x;

        vec4 DIRT = textureLod(thisMaterial.maps[4],UV4,mip);
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

        vec4 GBMT = GBMT_3;
             GBMT = mix(GBMT, GBMT_1, BLEND.r);
             GBMT = mix(GBMT, GBMT_2, BLEND.g);
        gGMF.r = GBMT.r;
  
        vec4 MAO = MAO_3;
             MAO = mix(MAO, MAO_1, BLEND.r);
             MAO = mix(MAO, MAO_2, BLEND.g);
        gGMF.g = MAO.r;
        
        vec3 bump;
        vec2 tb = vec2(GBMT.ga * 2.0 - 1.0);
        bump.xy    = tb.xy;
        bump.z = clamp(sqrt(1.0 - ((tb.x*tb.x)+(tb.y*tb.y))),-1.0,1.0);
        gNormal = normalize(bump);
        break;

    case FX_PBS_tiled_atlas_global:

        get_atlas_uvs(UV1, UV2, UV3, UV4, UV4_T);


        mip = mip_map_level(fs_in.TC1*thisMaterial.g_atlasSizes.xy)*0.5;
        BLEND = texture2D(thisMaterial.maps[3],UV4);

        colorAM_1 = textureLod(thisMaterial.maps[0],UV1,mip) * thisMaterial.g_tile0Tint;
        GBMT_1 =    textureLod(thisMaterial.maps[1],UV1,mip);
        MAO_1 =     textureLod(thisMaterial.maps[2],UV1,mip);

        colorAM_2 = textureLod(thisMaterial.maps[0],UV2,mip) * thisMaterial.g_tile1Tint;
        GBMT_2 =    textureLod(thisMaterial.maps[1],UV2,mip);
        MAO_2 =     textureLod(thisMaterial.maps[2],UV2,mip);

        colorAM_3 = textureLod(thisMaterial.maps[0],UV3,mip) * thisMaterial.g_tile2Tint;
        GBMT_3 =    textureLod(thisMaterial.maps[1],UV3,mip);
        MAO_3 =     textureLod(thisMaterial.maps[2],UV3,mip);

        //need to sort this out!
        dirt_scale = vec2(thisMaterial.dirtParams.y,thisMaterial.dirtParams.z);
        dirt_blend = thisMaterial.dirtParams.x;

        DIRT = textureLod(thisMaterial.maps[4],UV4,mip);
        DIRT.rgb *= thisMaterial.dirtColor.rgb;

        //============================================
        // Some 40 plus hours of trial and error to get this working.
        // The mix texture has to be compressed down/squished.
        BLEND.r = smoothstep(BLEND.r*colorAM_1.a,0.00,0.09);
        BLEND.g = smoothstep(BLEND.g*colorAM_2.a,0.00,0.25);
        BLEND.b = smoothstep(BLEND.b,0.00,0.6);// uncertain still... but this value seems to work well
        BLEND = correct(BLEND,4.0,0.8);
        //============================================
        colorAM = colorAM_3;
        colorAM = mix(colorAM,colorAM_1, BLEND.r);
        colorAM = mix(colorAM,colorAM_2, BLEND.g);

        colorAM = mix(colorAM,DIRT, BLEND.b);
        colorAM *= BLEND.a;
        gColor = colorAM;

        GBMT = GBMT_3;
        GBMT = mix(GBMT, GBMT_1, BLEND.r);
        GBMT = mix(GBMT, GBMT_2, BLEND.g);
        gGMF.r = GBMT.r;
  
        MAO = MAO_3;
        MAO = mix(MAO, MAO_1, BLEND.r);
        MAO = mix(MAO, MAO_2, BLEND.g);
        gGMF.g = MAO.r;
        
        bump;
        tb = vec2(GBMT.ga * 2.0 - 1.0);
        bump.xy    = tb.xy;
        bump.z = clamp(sqrt(1.0 - ((tb.x*tb.x)+(tb.y*tb.y))),-1.0,1.0);
        gNormal = normalize(bump);
        break;

    case FX_lightonly_alpha:
        // gColor = texture(thisMaterial.maps[0], fs_in.TC1);
        gColor = vec4(0.0,0.0,1.0,1.0); // debug
        break;

    case FX_unsupported:
        gColor = vec4(1.0, 1.0, 1.0, 1.0);
        break;

    default:
        gColor = vec4(0.0, 0.0, 0.0, 1.0);
    }

    gColor.a = 1.0;
    gPosition = fs_in.worldPosition;
    gGMF.b = renderType; // 64 = PBS, 63 = light/bump
}
