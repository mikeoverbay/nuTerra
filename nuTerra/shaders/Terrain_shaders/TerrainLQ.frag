#version 450 core

#extension GL_ARB_shading_language_include : require

#define USE_PERVIEW_UBO

#include "common.h" //! #include "../common.h"

layout (location = 0) out vec4 gColor;
layout (location = 1) out vec3 gNormal;
layout (location = 2) out vec4 gGMF;
layout (location = 3) out vec3 gPosition;
layout (location = 4) out uint gPick;

layout(binding = 0) uniform sampler2D global_AM;
layout(binding = 1) uniform sampler2D normalMap;
layout(binding = 2) uniform sampler2D gMask_in;

in VS_OUT {
    vec4 Vertex;
    mat3 TBN;
    vec3 worldPosition;
    vec2 UV;
    vec2 Global_UV;
} fs_in;

// Converion from AG map to RGB vector.
vec4 convertNormal(vec4 norm){
        vec3 n;
        n.xy = clamp(norm.ag*2.0-1.0, -1.0 ,1.0);
        n.z = max(sqrt(1.0 - (n.x*n.x - n.y *n.y)),0.0);
        return vec4(n,0.0);
}

/*===========================================================*/

void main(void)
{ 

    //==============================================================
    if ( texture(gMask_in, gl_FragCoord.xy / resolution).r >0.5 ) discard;
    //==============================================================


    vec4 global = texture(global_AM, fs_in.Global_UV);
    // This is needed to light the global_AM.
    vec4 g_nm = texture(normalMap, fs_in.UV);
    vec4 n = vec4(0.0);
    n.xyz = normalize(fs_in.TBN * vec3(convertNormal(g_nm).xyz));
    //n.x*=-1.0;
  
    // The obvious
    gColor = global;
    gColor.a = 1.0;

    gNormal.xyz = normalize(n.xyz);
    gGMF = vec4(global.a+0.2, 0.0, 128.0/255.0, 0.0);

    gPosition = fs_in.worldPosition;
    gPick = 0;
}
