#version 450 core

#extension GL_ARB_bindless_texture : require
#extension GL_ARB_shading_language_include : require

#include "common.h" //! #include "../common.h"


layout (location = 0) out vec4 gColor;
layout (location = 1) out vec3 gNormal;
layout (location = 2) out vec4 gGMF;
layout (location = 3) out vec3 gPosition;


uniform vec3 lightColor;
uniform vec3 viewPos;
uniform vec3 lightPosition;

in VS_OUT {
    vec3 co;
    vec3 vertexPosition;
    vec3 vertexNormal;
    vec2 UV;
    float specular;
} fs_in;

void main(void)
{
 
    gPosition = fs_in.vertexPosition;
    gNormal = fs_in.vertexNormal;
    gColor.rgb = fs_in.co;
    gColor.a = 0.0;

    gGMF = vec4(0.2, fs_in.specular, 128.0/255.0, 0.0);

}
