﻿// gWriter fragment Shader. We will use this as a template for other shaders
#version 430 core

layout (location = 0) out vec4 gColor;
layout (location = 1) out vec3 gNormal;
layout (location = 2) out vec3 gGMF;
layout (location = 3) out vec3 gPosition;

uniform sampler2D colorMap;
uniform sampler2D normalMap;
uniform sampler2D GMF_Map;

uniform int nMap_type;
uniform int alphaEnable;
uniform int alphaReference;

in vec2 UV;
in vec2 UV2;
in vec3 worldPosition;
in mat3 TBN;

in vec3 normal;//temp fro debuging lighting


vec3 getNormal()
{
    vec3 n;
    if (nMap_type == 1 ) {
        // GA map
        // We must clamp and max these to -1.0 to 1.0 to stop artifacts!
        n.xy = clamp(texture(normalMap, UV).ag*2.0-1.0, -1.0 ,1.0);
        n.y = max(sqrt(1.0 - (n.x*n.x + n.y *n.y)),0.0);
        n.xyz = n.xzy;
    } else {
        // RGB map
        n = texture(normalMap, UV).rgb*2.0-1.0;
    }
    n = normalize(TBN * n);
    return n;
}

void main(void)
{
    if (alphaEnable == 1){
        float a = texture(normalMap, UV).r;
        float aRef = float(alphaReference)/255.0;
        if (aRef > a) {
            discard;
        }
    }
    // easy.. just transfer the values to the gBuffer Textures and calculate perturbed normal;
    gColor = texture(colorMap, UV);
    gColor.a = 1.0;

    gNormal.xyz = getNormal();
    gGMF.rg = texture(GMF_Map, UV2).rg;
    gGMF.b = 64.0/255.0;

    gPosition = worldPosition;
}