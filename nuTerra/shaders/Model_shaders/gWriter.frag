#version 450 core

layout (location = 0) out vec4 gColor;
layout (location = 1) out vec3 gNormal;
layout (location = 2) out vec3 gGMF;
layout (location = 3) out vec3 gPosition;

uniform sampler2D colorMap;
uniform sampler2D normalMap;
uniform sampler2D GMF_Map;

uniform int nMap_type;

in vec2 UV;
in vec3 Vertex_Normal;
in vec3 worldPosition;

vec3 getNormal()
{
    // Retrieve the tangent space matrix
    vec3 pos_dx = dFdx(worldPosition);
    vec3 pos_dy = dFdy(worldPosition);
    vec3 tex_dx = dFdx(vec3(UV, 0.0));
    vec3 tex_dy = dFdy(vec3(UV, 0.0));
    vec3 t = (tex_dy.t * pos_dx - tex_dx.t * pos_dy) / (tex_dx.s * tex_dy.t - tex_dy.s * tex_dx.t);
    vec3 ng = normalize(Vertex_Normal);

    t = normalize(t - ng * dot(ng, t));
    vec3 b = normalize(cross(ng, t));
    mat3 tbn = mat3(t, b, ng);
    vec3 n;
    // GA or RGB maps?
    if (nMap_type == 1 ) {
        // GA map
        // We much clamp and max these to -1.0 to 1.0 to stop artifacts!
        n.xy = clamp(texture(normalMap, UV).ag * 2.0 - 1.0, -1.0, 1.0);
        n.y = max(sqrt(1.0 - (n.x * n.x + n.y * n.y)), 0.0);
        n.xyz = n.xzy;
    } else {
        //RGB map
        n = texture(normalMap, UV).rgb * 2.0 - 1.0;   
    }
    n = normalize(tbn * n);
    return n;
}
////////////////////////////////////////////////////////////////
void main(void)
{
    // easy.. just transfer the values to the gBuffer Textures and calculate perturbed normal;
    gColor = texture(colorMap, UV);
    gColor.a = 1.0;

    gNormal.xyz = getNormal();

    gGMF.rg = texture(GMF_Map, UV).rg;
    gGMF.b = texture(normalMap, UV).a; // not all decal maps have height info in alpha.

	gPosition = worldPosition;
}
