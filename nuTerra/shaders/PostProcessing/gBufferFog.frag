#version 450 core

#extension GL_ARB_bindless_texture : require
#extension GL_ARB_shading_language_include : require

#define USE_PERVIEW_UBO

#include "common.h" //! #include "../common.h"

layout (location = 0) out vec4 colorOut;

layout (binding = 0) uniform sampler2D gPosition;
layout (binding = 1) uniform sampler2D gColor;
layout (binding = 2) uniform sampler2D gGMF;

uniform float AMBIENT;
uniform float FOG_LEVEL;
uniform vec3 fog_color_in;
uniform float viewDistance;
uniform float Fogdensity;

uniform float Fog_density;
in VS_OUT {
    vec2 TexCoords;
} fs_in;


void main(void){

    vec3 FragPos = texture (gPosition, fs_in.TexCoords).rgb;
    vec4 colorIN = texture(gColor,  fs_in.TexCoords);
    vec4 gmfa = texture(gGMF,  fs_in.TexCoords);
    int flag = int(gmfa.b*255);
    float mapHeight = FragPos.y;
   // FOG calculation... using distance from camera and height on map.
    // It's a more natural height based fog than plastering the screen with it.
    float height = 1.5-(sin((FragPos.y / mapHeight)*(3.14158*0.65)));

    const float LOG2 = 1.442695;
    float z = viewDistance ;

    if (flag ==160) {z*=0.75;}//cut fog level down if this is water.

    float density = (Fogdensity * height) * 0.75;

    float fogFactor = exp2(-density * density * z * z * LOG2);
    fogFactor = clamp(fogFactor, 0.0, 1.0);
    vec3 fog_color = fog_color_in* vec3(AMBIENT) *3.0;
  
    colorOut.rgb = mix(fog_color.rgb, colorIN.rgb, fogFactor);
    colorOut.r *=0.1;
}
