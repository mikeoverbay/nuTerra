#version 450 core

#pragma optionNV (unroll all)

#extension GL_ARB_shading_language_include : require

#define USE_COMMON_PROPERTIES_UBO
#include "common.h" //! #include "../common.h"

layout(early_fragment_tests) in;

layout (location = 0) out vec4 gColor;
layout (location = 1) out vec3 gNormal;
layout (location = 2) out vec4 gGMF;
layout (location = 3) out vec3 gPosition;

layout(binding = 1 ) uniform sampler2DArray at[8];
layout(binding = 17) uniform sampler2D mixtexture[4];

layout(binding = 21) uniform sampler2D global_AM;

layout(binding = 22) uniform sampler2DArray textArrayC;
layout(binding = 23) uniform sampler2DArray textArrayN;
layout(binding = 24) uniform sampler2DArray textArrayG;

in VS_OUT {
    mat3 TBN;
    vec4 Vertex;
    vec3 worldPosition;
    vec2 UV;
    vec2 Global_UV;
    float ln;
    flat uint map_id;
} fs_in;

#ifdef SHOW_TEST_TEXTURES
//==============================================================
// texture outline stuff
float B[8];
//==============================================================
#endif

/*===========================================================*/
// https://www.gamedev.net/articles/programming/graphics/advanced-terrain-texture-splatting-r3287/
vec4 blend(vec4 texture1, float a1, vec4 texture2, float a2) {
 float depth = 0.95;
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
    n.xy = clamp(norm.ag*2.0-1.0, -1.0 ,1.0);;
    float dp = min(dot(n.xy, n.xy),1.0);
    n.z = clamp(sqrt(-dp+1.0),-1.0,1.0);
    n = normalize(n);
    n.x *= -1.0;
    return vec4(n,0.0);
}


/*===========================================================*/

vec2 get_transformed_uv(in vec4 U, in vec4 V) {

    vec4 vt = vec4(-fs_in.Vertex.x+50.0, fs_in.Vertex.y, fs_in.Vertex.z, 1.0);
    vt *= vec4(1.0, -1.0, 1.0,  1.0);
    vec2 out_uv = vec2(dot(U,vt), dot(-V,vt));
    return out_uv;
    }

vec4 crop( sampler2DArray samp, in vec2 uv , in float layer, int id, in vec4 offset)
{
    vec2  dx_vtc        = dFdx(uv*1024.0);
    vec2  dy_vtc        = dFdy(uv*1024.0);
    float delta_max_sqr = max(dot(dx_vtc, dx_vtc), dot(dy_vtc, dy_vtc));
    float mipLevel = 0.5 * log2(delta_max_sqr);

    uv += vec2(0.50,0.50);
    //uv += vec2(offset.x ,offset.y);

    vec2 cropped = fract(uv) * vec2(0.875, 0.875) + vec2(0.0625, 0.0625);

#ifdef SHOW_TEST_TEXTURES
    //----- test texture outlines -----
    B[id] = 0.0;
    if (cropped.x < 0.065 ) B[id] = 1.0;
    if (cropped.x > 0.935 ) B[id] = 1.0;
    if (cropped.y < 0.065 ) B[id] = 1.0;
    if (cropped.y > 0.935 ) B[id] = 1.0;
    //-----
#endif

    return textureLod( samp, vec3(cropped, layer), mipLevel);
    }

vec4 crop2( sampler2DArray samp, in vec2 uv , in float layer, in vec4 offset)
{
    vec2  dx_vtc        = dFdx(uv*1024.0);
    vec2  dy_vtc        = dFdy(uv*1024.0);
    float delta_max_sqr = max(dot(dx_vtc, dx_vtc), dot(dy_vtc, dy_vtc));
    float mipLevel = 0.5 * log2(delta_max_sqr);

    uv += vec2(0.50,0.50);
    //uv += vec2(offset.x ,offset.y);

    vec2 cropped = fract(uv) * vec2(0.875, 0.875) + vec2(0.0625, 0.0625);

    return textureLod( samp, vec3(cropped, layer), mipLevel);
    }

vec4 crop3( sampler2DArray samp, in vec2 uv , in float layer, in vec4 offset)
{

    uv += vec2(0.5,0.5);
    uv *= vec2(0.125, 0.125);

    vec2  dx_vtc        = dFdx(uv*1024.0);
    vec2  dy_vtc        = dFdy(uv*1024.0);
    float delta_max_sqr = max(dot(dx_vtc, dx_vtc), dot(dy_vtc, dy_vtc));

    float mipLevel = 0.5 * log2(delta_max_sqr);

    //uv += vec2(offset.x , offset.y);
    vec2 cropped = fract(uv)* vec2(0.875, 0.875) + vec2(0.0625, 0.0625);

    return textureLod( samp, vec3(cropped, layer), mipLevel);
    }

/*===========================================================*/
/*===========================================================*/
/*===========================================================*/
/*===========================================================*/
/*===========================================================*/

void main(void)
{
    vec2 mix_coords;
    //==============================================================

    mix_coords = fs_in.UV;
    mix_coords.x = 1.0 - mix_coords.x;

    // Get the mix values from the mix textures 1-4 and move to vec2.
    vec2 MixLevel[4];
    MixLevel[0].rg = texture(mixtexture[0], mix_coords.xy).ag;
    MixLevel[1].rg = texture(mixtexture[1], mix_coords.xy).ag;
    MixLevel[2].rg = texture(mixtexture[2], mix_coords.xy).ag;
    MixLevel[3].rg = texture(mixtexture[3], mix_coords.xy).ag;

    vec4 global = texture(global_AM, fs_in.Global_UV);
    //-------------------------------------------------------

    vec4 t[8];
    vec4 n[8];
    float f = 0.0;

    for (int i = 0; i < 8; ++i) {
        // create UV projections
        vec2 tuv = get_transformed_uv(L.U[i], L.V[i]); 

        // Get AM maps,crop and set Test outline blend flag
        t[i] = crop(at[i], tuv, 0.0, i, L.s[i]);
        vec4 mt = crop2(at[i], tuv, 2.0, L.s[i]);

        // specular is in red channel of the normal maps.
        // Ambient occlusion is in the Blue channel.
        // Green and Alpha are normal values.
        n[i] = crop2(at[i], tuv, 1.0, L.s[i]);
        vec4 mn = crop2(at[i], tuv, 3.0, L.s[i]);

        // get the ambient occlusion
        t[i].rgb *= n[i].b;
        mt.rgb *= mn.b;

        // mix macro
        t[i].rgb = t[i].rgb * min(L.r1[i].x, 1.0) + mt.rgb * (L.r2[i].y + 1.0);
        n[i].rgb = n[i].rgb * min(L.r1[i].x, 1.0) + mn.rgb * (L.r2[i].y + 1.0);

        // months of work to figure this out!
        MixLevel[i / 2][i % 2] *= t[i].a + L.r1[i].x;

        const float power = 1.6666;
        MixLevel[i / 2][i % 2] = pow(MixLevel[i / 2][i % 2], power);
        f += MixLevel[i / 2][i % 2];
    }

    vec4 out_n = vec4(0.0);
    vec4 base = vec4(0.0);
    for (int i = 0; i < 8; ++i) {
        MixLevel[i / 2][i % 2] /= f;
        MixLevel[i / 2][i % 2] = max(MixLevel[i / 2][i % 2], 0.0139);

        base += t[i] * MixLevel[i / 2][i % 2];
        out_n += n[i] * MixLevel[i / 2][i % 2];
    }

    //global
    vec4 gc = global;
    float c_l = length(base.rgb) + base.a + global.a;
    float g_l = length(global.rgb) - global.a;
    gc.rgb = global.rgb;
    //rem to remove global content
    base.rgb = (base.rgb * c_l + gc.rgb * g_l)/1.8;

    //wetness
    base = blend(base,base.a,vec4(props.waterColor,props.waterAlpha),global.a);

    // Texture outlines
#ifdef SHOW_TEST_TEXTURES
    vec4 colors[8] = {
        vec4(1.0,  1.0,  0.0,  0.0),
        vec4(0.0,  1.0,  0.0,  0.0),
        vec4(0.0,  0.0,  1.0,  0.0),
        vec4(1.0,  1.0,  0.0,  0.0),
        vec4(1.0,  0.0,  1.0,  0.0),
        vec4(1.0,  0.65, 0.0,  0.0),
        vec4(1.0,  0.49, 0.31, 0.0),
        vec4(0.5,  0.5,  0.5,  0.0)
    };
    for (int i = 0; i < 8; ++i) {
        base = mix(base, base + colors[i], B[i] * MixLevel[i / 2][i % 2]);
    }
#endif

    float specular = out_n.r;

    out_n = convertNormal(out_n);
    out_n.xyz = fs_in.TBN * out_n.xyz;
    
    // Get pre=mixed map textures
    vec4 ArrayTextureC = texture(textArrayC, vec3(fs_in.UV, fs_in.map_id) );
    vec4 ArrayTextureN = texture(textArrayN, vec3(fs_in.UV, fs_in.map_id) );
    vec4 ArrayTextureG = texture(textArrayG, vec3(fs_in.UV, fs_in.map_id) );

    ArrayTextureN.xyz = fs_in.TBN * ArrayTextureN.xyz;

    // This blends the pre-mixed maps over distance.
    base = mix(ArrayTextureC, base, fs_in.ln);
    out_n = mix(ArrayTextureN, out_n, fs_in.ln);

    //there are no metal values for the terrain so we hard code 0.1;
    // specular is in the red channel of the normal maps;
    vec4 gmm_out = vec4(0.2, specular, 128.0/255.0, 0.0);
    gGMF = mix(ArrayTextureG, gmm_out, fs_in.ln);

    //gColor = gColor* 0.001 + r1_8;
    gColor.rgb = base.rgb;//*0.01+t1.rgb;
    gColor.a = global.a*0.8;

    gNormal.xyz = normalize(out_n.xyz);

    gPosition = fs_in.worldPosition;
}
