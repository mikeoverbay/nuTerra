#version 450 core

#extension GL_ARB_shading_language_include : require

#define USE_COMMON_PROPERTIES_UBO
#include "common.h" //! #include "../common.h"

layout (location = 0) out vec4 gColor;

layout(binding = 0) uniform sampler2D global_AM;

layout(binding = 1 ) uniform sampler2DArray at[8];
layout(binding = 9) uniform sampler2D mixtexture[4];


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

    vec4 vt = vec4(-fs_in.UV.x*100.0+50.0, 0.0, fs_in.UV.y*100.0, 1.0);
    vt *= vec4(1.0, -1.0, 1.0,  1.0);
    vec2 out_uv = vec2(dot(U,vt), dot(-V,vt));
    out_uv += vec2(0.50,0.50);
    return out_uv;
    }

vec4 crop( sampler2DArray samp, in vec2 uv , in float layer, int id)
{
    vec2  dx_vtc        = dFdx(uv*1024.0);
    vec2  dy_vtc        = dFdy(uv*1024.0);
    float delta_max_sqr = max(dot(dx_vtc, dx_vtc), dot(dy_vtc, dy_vtc));
    float mipLevel = 0.5 * log2(delta_max_sqr);

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

vec4 crop2( sampler2DArray samp, in vec2 uv , in float layer)
{
    vec2  dx_vtc        = dFdx(uv*1024.0);
    vec2  dy_vtc        = dFdy(uv*1024.0);
    float delta_max_sqr = max(dot(dx_vtc, dx_vtc), dot(dy_vtc, dy_vtc));
    float mipLevel = 0.5 * log2(delta_max_sqr);

    vec2 cropped = fract(uv) * vec2(0.875, 0.875) + vec2(0.0625, 0.0625);

    return textureLod( samp, vec3(cropped, layer), mipLevel);
    }

vec4 crop3( sampler2DArray samp, in vec2 uv , in float layer)
{

    uv *= vec2(0.125, 0.125);

    vec2  dx_vtc        = dFdx(uv*1024.0);
    vec2  dy_vtc        = dFdy(uv*1024.0);
    float delta_max_sqr = max(dot(dx_vtc, dx_vtc), dot(dy_vtc, dy_vtc));

    float mipLevel = 0.5 * log2(delta_max_sqr);

    //uv += vec2(offset.x , offset.y);
    vec2 cropped = fract(uv)* vec2(0.875, 0.875) + vec2(0.0625, 0.0625);

    return textureLod( samp, vec3(cropped, layer), mipLevel);
    }


void main(void)
{
    const vec2 mix_coords = vec2(1.0 - fs_in.UV.x, fs_in.UV.y);

    float Mix[8];
    Mix[0] = texture(mixtexture[0], mix_coords.xy).a;
    Mix[1] = texture(mixtexture[0], mix_coords.xy).g;
    Mix[2] = texture(mixtexture[1], mix_coords.xy).a;
    Mix[3] = texture(mixtexture[1], mix_coords.xy).g;

    Mix[4] = texture(mixtexture[2], mix_coords.xy).a;
    Mix[5] = texture(mixtexture[2], mix_coords.xy).g;
    Mix[6] = texture(mixtexture[3], mix_coords.xy).a;
    Mix[7] = texture(mixtexture[3], mix_coords.xy).g;

    const vec4 global = texture(global_AM, fs_in.Global_UV);

    vec4 t[8];      // am map
    vec4 mt[8];     // am macro 
    float mth[8];   // macro height in alpha
    float th[8];    // am height
    vec4 n[8];      // normal map
    vec4 mn[8];     // macro normal map
    float f = 0.0;


    for (int i = 0; i < 8; ++i) {
        // create UV projections
        const vec2 tuv = get_transformed_uv(L.U[i], L.V[i]); 

        // Get AM maps,crop and set Test outline blend flag
        t[i] = crop(at[i], tuv, 0.0, i);

        mt[i] = crop3(at[i], tuv, 2.0);

    //u_xlat10 = max(u_xlat10, vec4(0.00392156886, 0.00392156886, 0.00392156886, 0.00392156886));
        mth[i] = max(mt[i].w,0.00392156886);

    //u_xlat14.xyz = u_xlat12.xyz;
        vec3 tv = mt[i].xyz;

    //u_xlat14.xyz = clamp(u_xlat14.xyz, 0.0, 1.0);
        tv = clamp(tv, vec3(0.0), vec3(1.0));

    //u_xlat14.xyz = (-u_xlat12.xyz) + u_xlat14.xyz;
        tv = -mt[i].xyz + tv;

    //u_xlat12.xyz = g_blockDataPS[1].blendMacroInfluence[3].xxx * u_xlat14.xyz + u_xlat12.xyz;
        mt[i].xyz = L.r2[i].xxx * tv + mt[i].xyz;

        // specular is in red channel of the normal maps.
        // Ambient occlusion is in the Blue channel.
        // Green and Alpha are normal values.
        n[i] = crop2(at[i], tuv, 1.0);
        mn[i] = crop3(at[i], tuv, 3.0);

        // get the ambient occlusion
        t[i].rgb *= n[i].b;
        mt[i].rgb *= mn[i].b;

        // mix macro
        t[i].rgb = t[i].rgb * min(L.r2[i].x, 1.0) + mt[i].rgb * (L.r2[i].y + 1.0);
        n[i].rgb = n[i].rgb * min(L.r2[i].x, 1.0) + mn[i].rgb * (L.r2[i].y + 1.0);
        //t[i].rgb = mt[i].rgb;
        //n[i].rgb = mn[i].rgb;
        // months of work to figure this out!
        Mix[i] *= t[i].a + L.r1[i].x;

        const float power = 1.0 / 0.7;
        Mix[i] = pow(Mix[i], power);
        f += Mix[i];
    }

    vec4 out_n = vec4(0.0);
    vec4 base = vec4(0.0);
    for (int i = 0; i < 8; ++i) {
        Mix[i] /= f;

        base += t[i] * Mix[i];
        out_n += n[i] * Mix[i];
    }

    // global
    float c_l = length(base.rgb) + base.a + global.a+0.25;
    float g_l = length(global.rgb) - global.a-base.a;

    // rem to remove global content
    base.rgb = (base.rgb * c_l + global.rgb * g_l) / 1.8;

    // wetness
    base = blend(base, base.a+0.75, vec4(props.waterColor, props.waterAlpha), global.a);

    float specular = out_n.r;

    //gColor = gColor* 0.001 + r1_8;
    gColor.rgb = base.rgb;
    gColor.a = global.a * 0.8;
}
