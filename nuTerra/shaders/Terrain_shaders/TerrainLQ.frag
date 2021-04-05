﻿#version 450 core

layout (location = 0) out vec4 gColor;
layout (location = 1) out vec3 gNormal;
layout (location = 2) out vec4 gGMF;
layout (location = 3) out vec3 gPosition;
layout (location = 4) out uint gPick;

layout(binding = 0) uniform sampler2D global_AM;
layout(binding = 1) uniform sampler2DArray textArrayC;
layout(binding = 2) uniform sampler2DArray textArrayN;
layout(binding = 3) uniform sampler2DArray textArrayG;


uniform vec3 waterColor;
uniform float waterAlpha;

in VS_OUT {
    vec4 Vertex;
    mat3 TBN;
    vec3 worldPosition;
    vec2 UV;
    vec2 Global_UV;
    flat uint map_id;
} fs_in;


/*===========================================================*/
void main(void)
{
    vec4 global = texture(global_AM, fs_in.Global_UV);
    // This is needed to light the global_AM.
    vec4 ArrayTextureC = texture(textArrayC, vec3(fs_in.UV, fs_in.map_id) );
    vec4 ArrayTextureN = texture(textArrayN, vec3(fs_in.UV, fs_in.map_id) );
    vec4 ArrayTextureG = texture(textArrayG, vec3(fs_in.UV, fs_in.map_id) );

    // The obvious
    gColor = ArrayTextureC;
    gNormal.xyz = normalize(fs_in.TBN * ArrayTextureN.xyz);
    gGMF = ArrayTextureG;

    gPosition = fs_in.worldPosition;
    gPick = 0;
}
