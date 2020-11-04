#version 450 core

#extension GL_ARB_bindless_texture : require
#extension GL_ARB_shading_language_include : require

#define USE_MATERIALS_SSBO
#include "common.h" //! #include "../common.h"

out vec4 gColor;

uniform vec3 lightColor;
uniform vec3 viewPos;

in VS_OUT {
    vec3 N;
    vec2 UV;
    vec3 FragPos;
    flat uint material_id;
} fs_in;

void main(void)
{
    MaterialProperties thisMaterial = material[fs_in.material_id];

    vec3 texColor =  texture(thisMaterial.maps[0], fs_in.UV).rgb;

    vec3 lightPosition = vec3 (30.0,30.0,30.0);

    float ambientStrength = 0.7;
    vec3 ambient = ambientStrength * lightColor * texColor;

    vec3 norm = normalize(fs_in.N);
    vec3 lightDir = normalize(lightPosition - fs_in.FragPos);

    float diff = max(dot(norm, lightDir), 0.0);
    vec3 diffuse = diff * lightColor * texColor;

    vec3 viewDir = normalize(viewPos - fs_in.FragPos);
    vec3 reflectDir = reflect(-lightDir, norm);

    float spec = pow(max(dot(viewDir, reflectDir), 0.0), 8)*0.6;
    vec3 specular =  spec * lightColor;
    vec3 result = (ambient + diffuse + specular);

    gColor.rgb = result.rgb;
    gColor.a = 1.0;
}
