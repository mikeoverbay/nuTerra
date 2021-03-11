#version 450 core

#extension GL_ARB_shading_language_include : require

#define USE_PERVIEW_UBO
#include "common.h" //! #include "../common.h"

layout (location = 0) out vec4 gColor;

layout (binding = 0) uniform sampler2D noiseMap;
layout (binding = 1) uniform sampler2D depthMap;
layout (binding = 2) uniform sampler2D gPosition;
layout (binding = 3) uniform sampler2D gColor_in;
//layout (binding = 4) uniform sampler2D gColor_in_2;

uniform vec3 fog_tint;
uniform float uv_scale;
uniform float time;
uniform vec2 move_vector;
uniform float fog_level;

in VS_OUT {
    flat mat4 invMVP;
    flat mat4 invDecal;
} fs_in;


const vec3 tr = vec3 (0.5 ,0.5 , 0.5);
const vec3 bl = vec3(-0.5, -0.5, -0.5);

void clip(vec3 v) {
    if (v.x > tr.x || v.x < bl.x ) discard;
    if (v.y > tr.y || v.y < bl.y ) discard;
    if (v.z > tr.z || v.z < bl.z ) discard;
}
//----------------------------------------------------------------------------------------
float Hash(in vec2 p, in float scale)
{
    // This is tiling part, adjusts with the scale...
    p = mod(p, scale);
    return fract(sin(dot(p, vec2(35.6898, 24.3563))) * 353753.373453);
}


//----------------------------------------------------------------------------------------
float cellNoise(in vec2 x, in float scale )
{
    x *= scale;

    vec2 p = floor(x);
    vec2 f = fract(x);
    f = f*f*(3.0-2.0*f);
    //f = (1.0-cos(f*3.1415927)) * .5;
    float res = mix(mix(Hash(p,  scale ),
        Hash(p + vec2(1.0, 0.0), scale), f.x),
        mix(Hash(p + vec2(0.0, 1.0), scale),
        Hash(p + vec2(1.0, 1.0), scale), f.x), f.y);
    return res;
}

//----------------------------------------------------------------------------------------
float NoiseFBM(in vec2 p, float numCells, int octaves)
{
    float f = 0.0;
    
    // Change starting scale to any integer value...
    p = mod(p, vec2(numCells));
    float amp = 0.5;
    float sum = 0.0;
    
    for (int i = 0; i < octaves; i++)
    {
        f += cellNoise(p, numCells) * amp;
        sum += amp;
        amp *= 0.5;

        // numCells must be multiplied by an integer value...
        numCells *= 2.0;
    }

    return f / sum;
}

void main()
{
    if ( gl_FrontFacing ) discard;

    // Calculate UVs
    vec2 uv = gl_FragCoord.xy / resolution;

    vec3 position = texture(gPosition,uv).rgb;


    vec4 deferred_mix = texture(gColor_in,uv);

    /*==================================================*/
//    bool flag = texture(gGMF,uv).b*255.0 == 64.0;
//    if (flag) discard;
    //if (flag == 96) { discard; }
    //if (flag != 128) { discard; }

    /*==================================================*/
    // sample the Depth from the Depthsampler
    float depth = texture(depthMap, uv).x;

    // Calculate clip space by recreating it out of the coordinates and depth-sample
    vec4 ScreenPosition = vec4(uv*2.0-1.0, depth, 1.0);
    // Transform position from screen space to world space
    vec4 WorldPosition = fs_in.invMVP * ScreenPosition;
    vec4 ModelPosition = WorldPosition;

    WorldPosition.xyz /= WorldPosition.w;
    WorldPosition.w = 1.0f;
    
    vec3 vPos = position;

    //vPos.y += 30.1;

    WorldPosition= fs_in.invDecal * vec4(vPos.xyz,1.0);

    // transform to decal original and size.
    // 1 x 1 x 1
    clip (WorldPosition.xyz);

    /*==================================================*/
    //Get texture UVs
    WorldPosition.xy += 0.5;

    vec2 loc = vec2( (WorldPosition.xy * vec2(uv_scale)) + move_vector);
    vec4 noise_ = texture(noiseMap,WorldPosition.xy);

    vec4 color = noise_;
    // Do the noise cloud (fractal Brownian motion)
    float c = NoiseFBM( loc , 8.0, 8) * 0.5 + 0.5;
    c = c * c;
    color = ( color * vec4(c,c,c,1.0) )* 2.0 ;

    color.xyz *= fog_tint * deferred_mix.a *6.0;
    

    gColor.rgb = deferred_mix.rgb;
    // terrain painting
    gColor.rgb = mix(deferred_mix.rgb, color.rgb, 0.95-deferred_mix.a);
    // Add some top level fog
    gColor.rgb = mix(gColor.rgb, fog_tint.rgb, 1.0-deferred_mix.a );

    gColor.rgb = mix(deferred_mix.rgb, gColor.rgb, fog_level);
    //gColor.rgb = vec3(deferred_mix*color);

}