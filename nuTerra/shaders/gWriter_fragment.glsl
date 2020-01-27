//
//gWriter fragment Shader. We will use this as a template for other shaders
//
#version 330 compatibility
layout (location = 0) out vec4 gColor;
layout (location = 1) out vec4 gPosition;
layout (location = 2) out vec4 gNormal;
layout (location = 3) out vec3 gGMF;

uniform sampler2D colorMap;
uniform sampler2D normalMap;
uniform sampler2D GMF_Map;

in vec2 UV;
in vec3 Vertex_Normal;
in vec3 v_Position;

// fucntions ///////////////////////////////////////////////////
// http://www.thetenthplanet.de/archives/1180

vec3 getNormal()
{
    // Retrieve the tangent space matrix
    vec3 pos_dx = dFdx(v_Position);
    vec3 pos_dy = dFdy(v_Position);
    vec3 tex_dx = dFdx(vec3(UV, 0.0));
    vec3 tex_dy = dFdy(vec3(UV, 0.0));
    vec3 t = (tex_dy.t * pos_dx - tex_dx.t * pos_dy) / (tex_dx.s * tex_dy.t - tex_dy.s * tex_dx.t);
    vec3 ng = normalize(Vertex_Normal);

    t = normalize(t - ng * dot(ng, t));
    vec3 b = normalize(cross(ng, t));
    mat3 tbn = mat3(t, b, ng);
    vec3 n = ng;
    n.xy = texture2D(normalMap, UV).ag*2.0-1.0;
    n.z = sqrt(1.0 - (n.x*n.x)+(n.y*n.y));
	n.x*= -1.0;
    n = tbn * normalize(n.xzy);
    return n;
}
////////////////////////////////////////////////////////////////
void main(void)
{
// easy.. just transfer the values to the gBuffer Textures and calculate perturbed normal;
gColor = texture2D(colorMap, UV);
gColor.a = 1.0;

gPosition.rgb = v_Position.xyz;

gNormal.xyz = getNormal()*0.5+0.5;
gNormal.w = 0.5; // specular in gNormal's w

gGMF.rg = texture2D(GMF_Map, UV).rg;
gGMF.b = texture2D(normalMap, UV).a; // not all decal maps have height info in alpha.
}
