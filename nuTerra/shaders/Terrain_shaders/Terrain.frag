// gWriter fragment Shader. We will use this as a template for other shaders
#version 430 core

layout (location = 0) out vec4 gColor;
layout (location = 1) out vec3 gNormal;
layout (location = 2) out vec3 gGMF;
layout (location = 3) out vec3 gPosition;

uniform sampler2D colorMap;
uniform sampler2D normalMap;
uniform sampler2D GMF_Map;

uniform int nMap_type;

in vec3 worldPosition;
in vec2 UV;
in vec3 normal;//temp fro debuging lighting
flat in float is_hole;

//very basic for now
void main(void)
{
    if (is_hole == 1.0) discard;

    // easy.. just transfer the values to the gBuffer Textures and calculate perturbed normal;
    gColor = texture(colorMap, UV);
    gColor.a = 1.0;

    gNormal.xyz = normal;
    gGMF.rg = texture(GMF_Map, UV).rg;
    gGMF.b = 128.0/255.0;

    gPosition = worldPosition;
}
