#version 450 core

#extension GL_ARB_shading_language_include : require

#define USE_PERVIEW_UBO
#include "common.h" //! #include "../common.h"

layout(location = 0) in vec2 vertexPosition;
layout(location = 1) in vec2 UVs;

layout(binding = 1) uniform sampler2D heigth_map;
layout(binding = 2) uniform sampler2D normal_map;

uniform float y_range;
uniform float y_offset;

uniform vec2 scale;
uniform vec2 center_offset;

uniform mat4 modelMatrix;

layout(location = 0) out VS_OUT {
    vec3 co;
    vec3 vertexPosition;
    vec3 vertexNormal;
    vec2 UV;
    float specular;
} vs_out;

vec3 convert_normal(in vec4 n){
vec3 norm;
norm.xy = n.ag;
norm.z = clamp(sqrt(1.0-(n.x * n.x) +(n.y * n.y)),-1.0,1.0);
return normalize(norm);
}

void main(void)
{
    vec2 UV = UVs;
    vs_out.UV = UV;
    
    vec4 n = texture(normal_map,UV);
    vs_out.specular = n.r;
    vs_out.vertexNormal = convert_normal(n);

    vec3 color = texture(heigth_map,UV).rgb;
    vs_out.co = color;
    vec3 pos;
    pos.xz = vertexPosition.xy * scale;
    //pos.xz -= center_offset ;
    pos.y = texture(heigth_map,UV).x;
    pos.y = pos.y;
    //pos.y *= 100.0;
    pos.y = pos.y * y_range + y_offset-1.5;
    vs_out.vertexPosition = vec3(modelMatrix * vec4(-pos, 1.0));

    gl_Position = viewProj * modelMatrix * vec4(pos, 1.0);

}
