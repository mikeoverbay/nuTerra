#version 450 core

#extension GL_ARB_shading_language_include : require

#define USE_COMMON_PROPERTIES_UBO
#include "common.h" //! #include "../common.h"

layout (location = 0) out vec4 gColor;

in VS_OUT {
    vec2 UV;
} fs_in;

// This function estimates mipmap levels
float MipLevel( vec2 uv, float size )
{
    vec2 dx = dFdx( uv * size );
    vec2 dy = dFdy( uv * size );
    float d = max( dot( dx, dx ), dot( dy, dy ) );

    return max( 0.5 * log2( d ), 0 );
}

void main(void)
{
    float mipCount = log2(props.PageTableSize);
    float mip = floor(MipLevel(fs_in.UV.xy, props.VirtualTextureSize) - props.MipBias);
    mip = clamp(mip, 0, mipCount);
    vec2 offset = floor(fs_in.UV.xy * props.PageTableSize);
    gColor = vec4(floor(vec3(offset / exp2(mip), mip)) / 255.0, 1.0); 
}
