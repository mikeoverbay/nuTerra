// gWriter fragment Shader. We will use this as a template for other shaders
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
float mip_map_level(in vec2 iUV, in vec2 iTextureSize)
{
    vec2  dx_vtc        = dFdx(iUV * iTextureSize.x);
    vec2  dy_vtc        = dFdy(iUV * iTextureSize.y);
    float d = max(dot(dx_vtc, dx_vtc), dot(dy_vtc, dy_vtc));
    
    return round(0.65 * log2(d)); 
}

void get_atlas_uvs(inout vec2 UV1,inout vec2 UV2,
                   inout vec2 UV3,inout vec2 UV4)
{
    vec2 tc = fs_in.TC1/round(thisMaterial.g_tileUVScale).xy;
    vec4 At_size = thisMaterial.g_atlasSizes;

    ivec2 isize = textureSize(thisMaterial.maps[0],0);
    vec2 image_size;
    image_size.x = float(isize.x); //to float. AMD hates using int values with floats.
    image_size.y = float(isize.y);

    float padSize = 0.0625;
    float textArea = 0.875;

    vec2 halfPixel = vec2(0.5/image_size.x,0.5/image_size.y); // 1/2 pixel offset;
    vec2 offset = vec2(padSize/At_size.x, padSize/At_size.y) + halfPixel; // border offset scaled by atlas tile count

    //common scale for UV1, UV2 and UV3
    float scaleX = 1.0 / At_size.x;			// UV length of one tile with border.
    float scaleY = 1.0 / At_size.y;
    vec2 UVs;
    UVs.x = (fract(fs_in.TC1.x)*scaleX*textArea) + offset.x;	// UV length with out borders + offset
    UVs.y = (fract(fs_in.TC1.y)*scaleY*textArea) + offset.x;
    //============================================
    vec2 tile;
    float index = thisMaterial.g_atlasIndexes.x;
    tile.y = floor(index/At_size.x);		// gets tile loaction in y
    tile.x = index - tile.y * At_size.x;	// gets tile location in x
    UV1.x = UVs.x + tile.x * scaleX;        // 0.0625 to 0.875 + (loc X * UV with border).
    UV1.y = UVs.y + tile.y * scaleY;        // 0.0625 to 0.875 + (loc Y * UV with border).

    index = thisMaterial.g_atlasIndexes.y;
    tile.y = floor(index/At_size.x);
    tile.x = index - tile.y * At_size.x;
    UV2.x = UVs.x + tile.x * scaleX;
    UV2.y = UVs.y + tile.y * scaleY;

    index = thisMaterial.g_atlasIndexes.z;
    tile.y = floor(index/At_size.x);
    tile.x = index - tile.y * At_size.x;
    UV3.x = UVs.x + tile.x * scaleX;
    UV3.y = UVs.y + tile.y * scaleY;

    //UV4 is used for blend.
    scaleX = 1.0 / At_size.z;
    scaleY = 1.0 / At_size.w;

    index = thisMaterial.g_atlasIndexes.w;
    tile.y = floor(index/At_size.z);
    tile.x = index - tile.y * At_size.z;

    UV4.x = (fract(fs_in.TC2.x)*scaleX)+tile.x*scaleX;
    UV4.y = (fract(fs_in.TC2.y)*scaleY)+tile.y*scaleY;

// ================================================================================

}

// Subroutines
subroutine void fn_entry();


layout(index = 0) subroutine(fn_entry) void default_entry()
{
    gColor = vec4(1, 0, 0, 0);
}


layout(index = 1) subroutine(fn_entry) void FX_PBS_ext_entry()
{
    gColor = texture(thisMaterial.maps[0], fs_in.TC1); // color
    gColor *= thisMaterial.g_colorTint;
    gGMF.rg = texture(thisMaterial.maps[2], fs_in.TC1).rg; // gloss/metal
    get_normal();
}


layout(index = 2) subroutine(fn_entry) void FX_PBS_ext_dual_entry()
{
    gColor = texture(thisMaterial.maps[0], fs_in.TC1); // color
    gColor *= texture(thisMaterial.maps[3], fs_in.TC2); // color2
    gColor *= thisMaterial.g_colorTint;
    gColor.rgb *= 1.5; // this will need tweaking
    gGMF.rg = texture(thisMaterial.maps[2], fs_in.TC1).rg; // gloss/metal
    get_normal();
}


layout(index = 3) subroutine(fn_entry) void FX_PBS_ext_detail_entry()
{
    gColor = texture(thisMaterial.maps[0], fs_in.TC1);
    gColor *= thisMaterial.g_colorTint;
    gGMF.rg = texture(thisMaterial.maps[2], fs_in.TC1).rg; // gloss/metal
    get_normal();
}


layout(index = 4) subroutine(fn_entry) void FX_PBS_tiled_atlas_entry()
{
    vec2 UV1, UV2, UV3, UV4;
    get_atlas_uvs(UV1, UV2, UV3, UV4);

    ivec2 isize = textureSize(thisMaterial.maps[0],0);

    float mip = mip_map_level(fs_in.TC2,isize);
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
    //colorAM_3.rgb *= colorAM_3.a;
    //colorAM_2.rgb *= colorAM_2.a;
    //colorAM_1.rgb *= colorAM_1.a;
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
    //gNormal = normalize(fs_in.TBN * bump);
}


layout(index = 5) subroutine(fn_entry) void FX_PBS_tiled_atlas_global_entry()
{
    vec2 UV1, UV2, UV3, UV4;
    get_atlas_uvs(UV1, UV2, UV3, UV4);
        
    vec4 globalTex = texture(thisMaterial.maps[5],fs_in.TC2);

    ivec2 isize = textureSize(thisMaterial.maps[0],0);

    float mip = mip_map_level(fs_in.TC2,isize);
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
    //colorAM_3.rgb *= colorAM_3.a;
    //colorAM_2.rgb *= colorAM_2.a;
    //colorAM_1.rgb *= colorAM_1.a;

    vec4 colorAM = colorAM_3;
    colorAM = mix(colorAM,colorAM_1, BLEND.r);
    colorAM = mix(colorAM,colorAM_2, BLEND.g);

    colorAM = mix(colorAM,DIRT, BLEND.b);
    colorAM *= BLEND.a;
    gColor = colorAM;
    //gColor += globalTex;
    vec4 GBMT = GBMT_3;
    GBMT = mix(GBMT, GBMT_1, BLEND.r);
    GBMT = mix(GBMT, GBMT_2, BLEND.g);
    gGMF.r = GBMT.r;
  
    vec4 MAO = MAO_3;
    MAO = mix(MAO, MAO_1, BLEND.r);
    MAO = mix(MAO, MAO_2, BLEND.g);
    gGMF.g = MAO.r;

    vec3 bump;
    GBMT = mix(globalTex, GBMT, 0.5);
    vec2 tb = vec2(GBMT.ga * 2.0 - 1.0);
    tb = vec2(globalTex.ga * 2.0 - 1.0);
    bump.xy    = tb.xy;
    bump.z = clamp(sqrt(1.0 - ((tb.x*tb.x)+(tb.y*tb.y))),-1.0,1.0);
    gNormal = normalize(fs_in.TBN * bump);
}


layout(index = 6) subroutine(fn_entry) void FX_lightonly_alpha_entry()
{
    // gColor = texture(thisMaterial.maps[0], fs_in.TC1);
    gColor = vec4(0.0,0.0,1.0,1.0); // debug
}


layout(index = 7) subroutine(fn_entry) void FX_unsupported_entry()
{
    gColor = vec4(1.0, 1.0, 1.0, 1.0);
}


subroutine uniform fn_entry entries[8];


// ================================================================================
// Main start
// ================================================================================
void main(void)
{
    float renderType = 64.0/255.0; // 64 = PBS, 63 = light/bump

    entries[thisMaterial.shader_type]();

    gColor = correct(gColor,2.0,0.8);
    gColor.a = 1.0;
    gPosition = fs_in.worldPosition;
    gGMF.b = renderType; // 64 = PBS, 63 = light/bump
}
